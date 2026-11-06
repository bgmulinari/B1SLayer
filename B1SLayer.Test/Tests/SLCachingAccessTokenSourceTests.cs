using System.Net;

namespace B1SLayer.Test;

public class SLCachingAccessTokenSourceTests
{
    private int _acquisitions;
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private SLCachingAccessTokenSource CreateSource(TimeSpan? lifetime = null, TimeSpan? skew = null, TimeSpan? delay = null, SLTokenResponse initialToken = null)
    {
        return new SLCachingAccessTokenSource(async cancellationToken =>
            {
                var number = Interlocked.Increment(ref _acquisitions);

                if (delay != null)
                {
                    await Task.Delay(delay.Value, cancellationToken);
                }

                return new SLTokenResponse($"token-{number}", _now + (lifetime ?? TimeSpan.FromMinutes(25)));
            },
            skew,
            initialToken,
            () => _now);
    }

    /// <summary>
    ///     Waits for a background renewal, which callers don't wait for themselves.
    /// </summary>
    private static async Task WaitForTokenAsync(SLCachingAccessTokenSource source, string expected)
    {
        for (var i = 0; i < 500; i++)
        {
            if (await source.GetAccessTokenAsync(SLAccessTokenRequest.Default) == expected)
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"The source never returned {expected}.");
    }

    [Fact]
    public async Task CachesTheTokenUntilItIsAboutToExpire()
    {
        var source = CreateSource(skew: TimeSpan.FromSeconds(60));

        Assert.Equal("token-1", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));

        _now += TimeSpan.FromMinutes(23);
        Assert.Equal("token-1", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));

        // Within the skew of its expiry
        _now += TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1);
        await WaitForTokenAsync(source, "token-2");
    }

    [Fact]
    public async Task ShortLivedToken_IsNotRenewedOnEveryRequest()
    {
        // With a skew longer than the token's lifetime, a plain subtraction would renew on every single request
        var source = CreateSource(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));

        await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
        _now += TimeSpan.FromSeconds(10);
        await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);

        Assert.Equal(1, _acquisitions);

        _now += TimeSpan.FromSeconds(6);
        await WaitForTokenAsync(source, "token-2");

        Assert.Equal(2, _acquisitions);
    }

    [Fact]
    public async Task UnknownExpiry_IsUsedUntilRejected()
    {
        var source = new SLCachingAccessTokenSource(_ => Task.FromResult(new SLTokenResponse($"token-{Interlocked.Increment(ref _acquisitions)}")), null, null, () => _now);

        await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
        _now += TimeSpan.FromDays(30);
        Assert.Equal("token-1", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));

        Assert.Equal("token-2", await source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "token-1")));
    }

    [Fact]
    public async Task ConcurrentRequests_AcquireOnce()
    {
        var source = CreateSource(delay: TimeSpan.FromMilliseconds(100));

        var tokens = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => source.GetAccessTokenAsync(SLAccessTokenRequest.Default))));

        Assert.All(tokens, x => Assert.Equal("token-1", x));
        Assert.Equal(1, _acquisitions);
    }

    [Fact]
    public async Task ConcurrentForcedRefreshes_OfTheSameToken_RefreshOnce()
    {
        var source = CreateSource(delay: TimeSpan.FromMilliseconds(100));
        var rejected = await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
        _now += TimeSpan.FromMinutes(1);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() => source.GetAccessTokenAsync(new SLAccessTokenRequest(true, rejected)))));

        Assert.All(tokens, x => Assert.Equal("token-2", x));
        Assert.Equal(2, _acquisitions);
    }

    [Fact]
    public async Task ForcedRefresh_OfAnOlderToken_ReturnsTheCurrentOne()
    {
        // This request failed with token-1, but someone else has already replaced it
        var source = CreateSource();
        await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
        _now += TimeSpan.FromMinutes(1);
        await source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "token-1"));

        Assert.Equal("token-2", await source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "token-1")));
        Assert.Equal(2, _acquisitions);
    }

    [Fact]
    public async Task ForcedRefresh_WithoutARejectedToken_AlwaysRefreshes()
    {
        var source = CreateSource();
        await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);

        Assert.Equal("token-2", await source.GetAccessTokenAsync(new SLAccessTokenRequest(true)));
    }

    [Fact]
    public async Task ConcurrentRequestsThroughAConnection_RefreshOnceOnUnauthorized()
    {
        // All requests fail with the same token at the same time, as happens when it expires under load
        var http = new MockHttp();
        http.ForCallsTo("*/Items").With(x => x.Headers.Any(h => h.Value == "Bearer token-1")).RespondWith("{}", 401);
        http.ForCallsTo("*/Items").RespondWith("{}");

        var source = CreateSource(delay: TimeSpan.FromMilliseconds(50));

        var connection = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
            AccessTokenSource = source,
            CompanyId = "1875",
            HttpMessageHandler = http,
            RetryDelay = TimeSpan.Zero
        });

        await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
        _now += TimeSpan.FromMinutes(1);

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => connection.Request("Items").GetStringAsync()));

        Assert.Equal(2, _acquisitions);
        http.ShouldHaveCalled("*/Items").WithHeader("Authorization", "Bearer token-2").Times(50);
        http.Dispose();
    }

    [Fact]
    public async Task ForcedRefresh_AfterAnotherCallerRefreshedSinceTheCheck_DoesNotRefreshAgain()
    {
        var acquisitions = 0;
        SLCachingAccessTokenSource source = null;
        var interleave = false;

        // The clock is read right after the cached token was, which is where another caller can get its refresh in
        source = new SLCachingAccessTokenSource(_ => Task.FromResult(new SLTokenResponse($"token-{Interlocked.Increment(ref acquisitions)}", _now.AddHours(1))),
            null,
            new SLTokenResponse("token-0", _now.AddHours(1)),
            () =>
            {
                if (interleave)
                {
                    interleave = false;
                    Assert.Equal("token-1", source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "token-0")).GetAwaiter().GetResult());
                }

                return _now;
            });

        interleave = true;

        Assert.Equal("token-1", await source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "token-0")));
        Assert.Equal(1, acquisitions);
    }

    [Fact]
    public async Task FailedEarlyRenewal_KeepsUsingTheUnexpiredToken_AndRetriesAfterAnInterval()
    {
        var fail = true;
        var acquisitions = 0;
        var failed = new SemaphoreSlim(0);

        var source = new SLCachingAccessTokenSource(_ =>
            {
                Interlocked.Increment(ref acquisitions);

                if (fail)
                {
                    failed.Release();
                    throw new SLAuthenticationException("token endpoint down");
                }

                return Task.FromResult(new SLTokenResponse("renewed", _now.AddHours(1)));
            },
            TimeSpan.FromSeconds(60),
            new SLTokenResponse("current", _now.AddMinutes(25)),
            () => _now);

        // Within the skew, but not expired yet
        _now += TimeSpan.FromMinutes(24.5);
        Assert.Equal("current", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
        Assert.True(await failed.WaitAsync(TimeSpan.FromSeconds(5)));

        // The attempt has failed, but its cleanup may still be running, which only ever leads to no second attempt
        await Task.Delay(50);

        // Not tried again right away, so a failing server doesn't get a token request per request
        Assert.Equal("current", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
        await Task.Delay(50);
        Assert.Equal(1, acquisitions);

        // Tried again after the interval, which picks up the recovery
        fail = false;
        _now += TimeSpan.FromSeconds(11);
        await WaitForTokenAsync(source, "renewed");
        Assert.Equal(2, acquisitions);
    }

    [Fact]
    public async Task RejectedToken_JustAcquired_IsReturnedAgain_UntilTheIntervalHasPassed()
    {
        var source = CreateSource();
        var rejected = await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);

        _now += TimeSpan.FromSeconds(29);
        Assert.Equal(rejected, await source.GetAccessTokenAsync(new SLAccessTokenRequest(true, rejected)));
        Assert.Equal(1, _acquisitions);

        _now += TimeSpan.FromSeconds(2);
        Assert.Equal("token-2", await source.GetAccessTokenAsync(new SLAccessTokenRequest(true, rejected)));
    }

    [Fact]
    public async Task RejectedInitialToken_IsAlwaysReplaced_AsItsAgeIsUnknown()
    {
        var source = CreateSource(initialToken: new SLTokenResponse("stored", _now.AddMinutes(25)));

        Assert.Equal("token-1", await source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "stored")));
    }

    [Fact]
    public async Task PersistentRejection_DoesNotRequestATokenForEveryFailedRequest()
    {
        var http = new MockHttp();
        http.ForCallsTo("*/Items").RespondWith("{}", 401);
        var source = CreateSource();

        var connection = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
            AccessTokenSource = source,
            CompanyId = "1875",
            HttpMessageHandler = http,
            RetryDelay = TimeSpan.Zero
        });

        for (var i = 0; i < 5; i++)
        {
            var exception = await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").GetStringAsync());
            Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        }

        Assert.Equal(1, _acquisitions);
        http.ShouldHaveCalled("*/Items").Times(5);

        _now += TimeSpan.FromSeconds(31);
        await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").GetStringAsync());
        Assert.Equal(2, _acquisitions);
        http.Dispose();
    }

    [Fact]
    public async Task FailedPersistence_KeepsTheNewToken_AndSurfacesOnce()
    {
        var saves = 0;

        var source = new SLCachingAccessTokenSource(
            _ => Task.FromResult(new SLTokenResponse($"token-{Interlocked.Increment(ref _acquisitions)}", _now.AddMinutes(25))),
            TimeSpan.FromSeconds(60),
            new SLTokenResponse("current", _now.AddMinutes(25)),
            () => _now,
            (_, _) =>
            {
                Interlocked.Increment(ref saves);
                throw new InvalidOperationException("store down");
            });

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "current")));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal("token-1", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
        Assert.Equal(1, _acquisitions);
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task FailedPersistence_OfAnEarlyRenewal_SurfacesOnTheNextCall()
    {
        var saved = new TaskCompletionSource();

        var source = new SLCachingAccessTokenSource(
            _ => Task.FromResult(new SLTokenResponse("renewed", _now.AddHours(1))),
            TimeSpan.FromSeconds(60),
            new SLTokenResponse("current", _now.AddMinutes(25)),
            () => _now,
            async (_, _) =>
            {
                await Task.Yield();
                saved.SetResult();
                throw new InvalidOperationException("store down");
            });

        _now += TimeSpan.FromMinutes(24.5);
        Assert.Equal("current", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
        await saved.Task;

        SLAuthenticationException exception = null;

        for (var i = 0; i < 500 && exception == null; i++)
            try
            {
                await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
                await Task.Delay(10);
            }
            catch (SLAuthenticationException ex)
            {
                exception = ex;
            }

        Assert.IsType<InvalidOperationException>(exception?.InnerException);
        Assert.Equal("renewed", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
    }

    [Fact]
    public async Task FailedRenewal_OfAnExpiredToken_Surfaces()
    {
        var source = new SLCachingAccessTokenSource(_ => throw new SLAuthenticationException("token endpoint down"), TimeSpan.FromSeconds(60), new SLTokenResponse("current", _now.AddMinutes(25)), () => _now);

        _now += TimeSpan.FromMinutes(25);

        await Assert.ThrowsAsync<SLAuthenticationException>(() => source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
    }

    [Fact]
    public async Task FailedForcedRefresh_DoesNotFallBackToTheRejectedToken()
    {
        var source = new SLCachingAccessTokenSource(_ => throw new SLAuthenticationException("token endpoint down"), TimeSpan.FromSeconds(60), new SLTokenResponse("current", _now.AddMinutes(25)), () => _now);

        await Assert.ThrowsAsync<SLAuthenticationException>(() => source.GetAccessTokenAsync(new SLAccessTokenRequest(true, "current")));
    }

    [Fact]
    public async Task EarlyRenewal_DoesNotMakeCallersWait()
    {
        var release = new TaskCompletionSource();
        var acquisitions = 0;

        var source = new SLCachingAccessTokenSource(async _ =>
            {
                Interlocked.Increment(ref acquisitions);
                await release.Task;
                return new SLTokenResponse("renewed", _now.AddHours(1));
            },
            TimeSpan.FromSeconds(60),
            new SLTokenResponse("current", _now.AddMinutes(25)),
            () => _now);

        _now += TimeSpan.FromMinutes(24.5);

        // The renewal hangs, and the callers aren't affected by it
        for (var i = 0; i < 10; i++) Assert.Equal("current", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));

        release.SetResult();
        await WaitForTokenAsync(source, "renewed");
        Assert.Equal(1, acquisitions);
    }

    [Fact]
    public async Task FailedAcquisition_IsSharedByWaitersAndRetriedByTheNextCall()
    {
        var release = new TaskCompletionSource();
        var attempts = 0;

        var source = new SLCachingAccessTokenSource(async _ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    await release.Task;
                    throw new InvalidOperationException("identity provider down");
                }

                return new SLTokenResponse("recovered", _now.AddHours(1));
            },
            null,
            null,
            () => _now);

        var waiters = Enumerable.Range(0, 10).Select(_ => source.GetAccessTokenAsync(SLAccessTokenRequest.Default)).ToList();
        release.SetResult();

        foreach (var waiter in waiters)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => waiter);
        }

        Assert.Equal(1, attempts);
        Assert.Equal("recovered", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
    }

    [Fact]
    public async Task CallerCancellation_OnlyStopsThatCaller_AndKeepsTheAcquisitionGoing()
    {
        // Cancelling must not abort a refresh in flight: a rotated refresh token could be lost with it
        var release = new TaskCompletionSource();
        CancellationToken acquireToken = default;

        var source = new SLCachingAccessTokenSource(async cancellationToken =>
            {
                acquireToken = cancellationToken;
                await release.Task;
                return new SLTokenResponse("token", _now.AddHours(1));
            },
            null,
            null,
            () => _now);

        using var cancellation = new CancellationTokenSource();
        var cancelled = source.GetAccessTokenAsync(SLAccessTokenRequest.Default, cancellation.Token);
        var other = source.GetAccessTokenAsync(SLAccessTokenRequest.Default);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        Assert.False(other.IsCompleted);
        Assert.False(acquireToken.IsCancellationRequested);

        release.SetResult();
        Assert.Equal("token", await other);
    }

    [Fact]
    public async Task AcquisitionReturningNull_FailsClearly()
    {
        var source = new SLCachingAccessTokenSource(_ => Task.FromResult<SLTokenResponse>(null));

        await Assert.ThrowsAsync<SLAuthenticationException>(() => source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
    }

    [Fact]
    public async Task Acquisition_IgnoringItsToken_DoesNotHoldUpTheNextOne()
    {
        var calls = 0;

        var source = new SLCachingAccessTokenSource(_ => Interlocked.Increment(ref calls) == 1
            ? new TaskCompletionSource<SLTokenResponse>().Task
            : Task.FromResult(new SLTokenResponse("second", DateTimeOffset.UtcNow.AddHours(1))))
        {
            AcquireTimeout = TimeSpan.FromMilliseconds(50)
        };

        await Assert.ThrowsAsync<SLAuthenticationException>(() => source.GetAccessTokenAsync(SLAccessTokenRequest.Default));

        Assert.Equal("second", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
    }

    [Fact]
    public async Task PersistenceCallback_IgnoringItsToken_SurfacesOnceWithTheTokenInUse()
    {
        var source = new SLCachingAccessTokenSource(
            _ => Task.FromResult(new SLTokenResponse("token", DateTimeOffset.UtcNow.AddHours(1))),
            null,
            null,
            null,
            (_, _) => new TaskCompletionSource<bool>().Task)
        {
            AcquireTimeout = TimeSpan.FromMilliseconds(50)
        };

        await Assert.ThrowsAsync<SLAuthenticationException>(() => source.GetAccessTokenAsync(SLAccessTokenRequest.Default));

        Assert.Equal("token", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
    }

    [Fact]
    public async Task MalformedToken_IsNotCached()
    {
        var calls = 0;

        var source = new SLCachingAccessTokenSource(_ => Task.FromResult(new SLTokenResponse(Interlocked.Increment(ref calls) == 1 ? "not valid" : "valid")));

        await Assert.ThrowsAsync<SLAuthenticationException>(() => source.GetAccessTokenAsync(SLAccessTokenRequest.Default));

        Assert.Equal("valid", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
    }

    [Fact]
    public async Task MalformedInitialToken_IsReplacedOnFirstUse()
    {
        var source = new SLCachingAccessTokenSource(
            _ => Task.FromResult(new SLTokenResponse("valid")),
            null,
            new SLTokenResponse("not valid"),
            null);

        Assert.Equal("valid", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
    }

    [Fact]
    public async Task Invalidate_DuringAnAcquisition_DiscardsItsToken()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var source = new SLCachingAccessTokenSource(async _ =>
        {
            var call = Interlocked.Increment(ref calls);

            if (call == 1)
            {
                started.SetResult(true);
                await release.Task;
            }

            return new SLTokenResponse($"token-{call}", DateTimeOffset.UtcNow.AddHours(1));
        });

        var beforeInvalidation = source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
        await started.Task;

        source.Invalidate();
        var afterInvalidation = source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
        release.SetResult(true);

        Assert.Equal("token-1", await beforeInvalidation);
        Assert.Equal("token-2", await afterInvalidation);
        Assert.Equal("token-2", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Invalidate_DuringAnAcquisitionThatFails_LeavesLaterCallersUnaffected()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var source = new SLCachingAccessTokenSource(async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.SetResult(true);
                await release.Task;
                throw new InvalidOperationException("old credentials");
            }

            return new SLTokenResponse("replacement", DateTimeOffset.UtcNow.AddHours(1));
        });

        var beforeInvalidation = source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
        await started.Task;

        source.Invalidate();
        var afterInvalidation = source.GetAccessTokenAsync(SLAccessTokenRequest.Default);
        release.SetResult(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => beforeInvalidation);
        Assert.Equal("replacement", await afterInvalidation);
    }

    [Fact]
    public async Task Invalidate_ForcesANewAcquisition()
    {
        var source = CreateSource();
        await source.GetAccessTokenAsync(SLAccessTokenRequest.Default);

        source.Invalidate();

        Assert.Equal("token-2", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
    }

    [Fact]
    public async Task InitialToken_IsServedWithoutAcquiring()
    {
        var source = CreateSource(initialToken: new SLTokenResponse("seed", _now.AddHours(1)));

        Assert.Equal("seed", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
        Assert.Equal(0, _acquisitions);
    }

    [Fact]
    public async Task ExpiredInitialToken_IsReplaced()
    {
        var source = CreateSource(initialToken: new SLTokenResponse("seed", _now.AddHours(-1)));

        Assert.Equal("token-1", await source.GetAccessTokenAsync(SLAccessTokenRequest.Default));
    }

    [Fact]
    public void Arguments_AreValidated()
    {
        Assert.Throws<ArgumentNullException>(() => new SLCachingAccessTokenSource(null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SLCachingAccessTokenSource(_ => Task.FromResult(new SLTokenResponse("a")), TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentException>(() => new SLTokenResponse(null));
        Assert.DoesNotContain("token", new SLCachingAccessTokenSource(_ => Task.FromResult(new SLTokenResponse("token"))).ToString().Replace("TokenSource", string.Empty));
    }
}
