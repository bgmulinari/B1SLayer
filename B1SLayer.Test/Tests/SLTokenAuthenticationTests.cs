using System.Net;
using System.Text;

using Microsoft.Extensions.Caching.Distributed;

namespace B1SLayer.Test;

public class SLTokenAuthenticationTests : TestBase
{
    private const string CompanyId = "1875";
    private const string Token = "user.token-1";
    private const string Unauthorized = "{\"error\":{\"code\":\"301\",\"message\":{\"value\":\"Invalid token\"}}}";

    private SLConnectionOptions CreateOptions(bool withSessionCredentials, MockHttp httpTest)
    {
        var options = new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
            HttpMessageHandler = httpTest ?? HttpTest,
            RetryDelay = TimeSpan.Zero
        };

        if (withSessionCredentials)
        {
            options.CompanyDB = "CompanyDB";
            options.UserName = $"manager-{Guid.NewGuid():N}";
            options.Password = "12345";
        }

        return options;
    }

    private SLConnection CreateTokenConnection(ISLAccessTokenSource source, bool withSessionCredentials = false, MockHttp httpTest = null, Action<SLConnectionOptions> configure = null)
    {
        var options = CreateOptions(withSessionCredentials, httpTest);
        options.AccessTokenSource = source;
        options.CompanyId = CompanyId;
        configure?.Invoke(options);
        return new SLConnection(options);
    }

    private SLConnection CreateResolverConnection(Func<CancellationToken, Task<SLRequestIdentity>> resolver, bool withSessionCredentials = true, MockHttp httpTest = null)
    {
        var options = CreateOptions(withSessionCredentials, httpTest);
        options.IdentityResolver = resolver;
        return new SLConnection(options);
    }

    [Fact]
    public async Task StalledTokenSource_IsBoundedByTheRequestTimeout()
    {
        // Ignores its cancellation token, so only the request timeout can release the caller
        var source = SLAccessTokenSource.FromDelegate((_, _) => new TaskCompletionSource<string>().Task);
        var connection = CreateTokenConnection(source);

        await Assert.ThrowsAsync<SLTimeoutException>(() => connection.Request("Items").WithTimeout(TimeSpan.FromMilliseconds(50)).GetStringAsync());
    }

    [Fact]
    public async Task StalledIdentityResolver_IsBoundedByTheRequestTimeout()
    {
        var connection = CreateResolverConnection(_ => new TaskCompletionSource<SLRequestIdentity>().Task);

        await Assert.ThrowsAsync<SLTimeoutException>(() => connection.Request("Items").WithTimeout(TimeSpan.FromMilliseconds(50)).GetStringAsync());
    }

    [Fact]
    public async Task CallerCancellation_WhileAcquiringAToken_IsNotReportedAsATimeout()
    {
        var source = SLAccessTokenSource.FromDelegate((_, _) => new TaskCompletionSource<string>().Task);
        var connection = CreateTokenConnection(source);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.Request("Items").GetStringAsync(cancellation.Token));
    }

    [Fact]
    public async Task TokenRequest_TellsTheBrowserToOmitItsCookies()
    {
        HttpTest.RespondWith("{}");
        var connection = CreateTokenConnection(new RecordingTokenSource(Token));
        IDictionary<string, object> fetchOptions = null;
        connection.AfterCall(context => context.RequestMessage.Options.TryGetValue(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"), out fetchOptions));

        await connection.Request("Items").GetStringAsync();

        Assert.Equal("omit", fetchOptions["credentials"]);
    }

    [Fact]
    public async Task StartedHandlerWithCookiesEnabled_IsRejectedForAccessTokens_ButNotForSessions()
    {
        using var handler = new HttpClientHandler();

        using (var client = new HttpClient(handler, false))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync("http://127.0.0.1:1/"));
        }

        Assert.True(handler.UseCookies);

        Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
            AccessTokenSource = new RecordingTokenSource(Token),
            CompanyId = CompanyId,
            HttpMessageHandler = handler
        }));

        Assert.NotNull(new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
            CompanyDB = "CompanyDB",
            UserName = "manager",
            Password = "12345",
            HttpMessageHandler = handler
        }));
    }

    [Fact]
    public async Task StartedHandlerWithAutomaticRedirects_IsRejectedForAccessTokens()
    {
        using var handler = new HttpClientHandler { UseCookies = false };

        using (var client = new HttpClient(handler, false))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync("http://127.0.0.1:1/"));
        }

        Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
            AccessTokenSource = new RecordingTokenSource(Token),
            CompanyId = CompanyId,
            HttpMessageHandler = handler
        }));
    }

    [Fact]
    public async Task StartedHandlerWithCookiesAndRedirectsDisabled_IsAcceptedForAccessTokens()
    {
        using var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false };

        using (var client = new HttpClient(handler, false))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync("http://127.0.0.1:1/"));
        }

        Assert.NotNull(new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
            AccessTokenSource = new RecordingTokenSource(Token),
            CompanyId = CompanyId,
            HttpMessageHandler = handler
        }));
    }

    private void AssertTokenCall(string urlPattern, string token = Token, string companyId = CompanyId, int? times = null)
    {
        // A cookie header on any of these calls would make the Service Layer ignore the token
        var assertion = HttpTest.ShouldHaveCalled(urlPattern)
            .WithHeader("Authorization", "Bearer " + token)
            .WithHeader("X-b1-companyid", companyId)
            .WithoutHeader("Cookie");

        if (times != null)
        {
            assertion.Times(times.Value);
        }
    }

    // Every public send path of the library, which all must go through the same credential enforcement
    public static TheoryData<string> SendPaths() =>
    [
        "GetAsync", "GetWithETagAsync", "GetWithInlineCountAsync", "GetAllAsync", "GetStringAsync", "GetAnonymousTypeAsync", "GetBytesAsync", "GetStreamAsync", "GetCountAsync",
        "PostAsync<T>", "PostStringAsync<T>", "PostAsync(body)", "PostAsync(empty)", "PostReceiveStringAsync", "PostStringAsync",
        "PatchAsync", "PatchStringAsync", "PatchWithFileAsync", "PutAsync", "PutStringAsync", "DeleteAsync",
        "PostAttachmentAsync", "PostAttachmentsAsync(streams)", "PatchAttachmentAsync", "GetAttachmentAsBytesAsync", "GetAttachmentAsStreamAsync",
        "PostBatchAsync", "$metadata"
    ];

    private static async Task InvokeSendPath(SLConnection connection, string path)
    {
        var request = connection.Request("Items");

        switch (path)
        {
            case "GetAsync": await request.GetAsync<object>(); break;
            case "GetWithETagAsync": await request.GetWithETagAsync<object>(); break;
            case "GetWithInlineCountAsync": await request.GetWithInlineCountAsync<object>(); break;
            case "GetAllAsync": await request.GetAllAsync<object>(); break;
            case "GetStringAsync": await request.GetStringAsync(); break;
            case "GetAnonymousTypeAsync": await request.GetAnonymousTypeAsync(new { value = (object)null }); break;
            case "GetBytesAsync": await request.GetBytesAsync(); break;
            case "GetStreamAsync": await request.GetStreamAsync(); break;
            case "GetCountAsync": await request.GetCountAsync(); break;
            case "PostAsync<T>": await request.PostAsync<object>(new { A = 1 }); break;
            case "PostStringAsync<T>": await request.PostStringAsync<object>("{}"); break;
            case "PostAsync(body)": await request.PostAsync(new { A = 1 }); break;
            case "PostAsync(empty)": await request.PostAsync(); break;
            case "PostReceiveStringAsync": await request.PostReceiveStringAsync(new { A = 1 }); break;
            case "PostStringAsync": await request.PostStringAsync("{}"); break;
            case "PatchAsync": await request.PatchAsync(new { A = 1 }); break;
            case "PatchStringAsync": await request.PatchStringAsync("{}"); break;
            case "PatchWithFileAsync": await request.PatchWithFileAsync("a.txt", [1, 2, 3]); break;
            case "PutAsync": await request.PutAsync(new { A = 1 }); break;
            case "PutStringAsync": await request.PutStringAsync("{}"); break;
            case "DeleteAsync": await request.DeleteAsync(); break;
            case "PostAttachmentAsync": await connection.PostAttachmentAsync("a.txt", [1, 2, 3]); break;
            case "PostAttachmentsAsync(streams)": await connection.PostAttachmentsAsync(new Dictionary<string, Stream> { ["a.txt"] = new MemoryStream([1, 2, 3]) }); break;
            case "PatchAttachmentAsync": await connection.PatchAttachmentAsync(1, "a.txt", [1, 2, 3]); break;
            case "GetAttachmentAsBytesAsync": await connection.GetAttachmentAsBytesAsync(1); break;
            case "GetAttachmentAsStreamAsync": await connection.GetAttachmentAsStreamAsync(1); break;
            case "PostBatchAsync": await connection.PostBatchAsync(new SLBatchRequest(HttpMethod.Get, "Items")); break;
            case "$metadata": await connection.Request("$metadata").GetStringAsync(); break;
            default: throw new ArgumentOutOfRangeException(nameof(path));
        }
    }

    private void RespondToEverySendPath()
    {
        HttpTest.ForCallsTo("*/b1s/v2/Items/$count").RespondWith("3");
        HttpTest.ForCallsTo("*/b1s/v2/Attachments2*").WithVerb(HttpMethod.Post).RespondWithJson(new { AbsoluteEntry = 1 });
        HttpTest.RespondWithJson(new { value = Array.Empty<object>() });
    }

    [Theory]
    [MemberData(nameof(SendPaths))]
    public async Task EverySendPath_InTokenMode_CarriesOnlyTheToken(string path)
    {
        RespondToEverySendPath();
        var connection = CreateTokenConnection(new RecordingTokenSource(Token), true);

        await InvokeSendPath(connection, path);

        Assert.NotEmpty(HttpTest.CallLog);
        Assert.All(HttpTest.CallLog, call => Assert.DoesNotContain(call.Headers, x => x.Key == "Cookie"));
        AssertTokenCall("*/b1s/v2/*");
        HttpTest.ShouldNotHaveCalled("*/Login");
    }

    [Theory]
    [MemberData(nameof(SendPaths))]
    public async Task EverySendPath_WithSessionIdentityOnSameConnection_CarriesOnlyTheCookie(string path)
    {
        RespondToEverySendPath();
        var connection = CreateResolverConnection(_ => Task.FromResult(SLRequestIdentity.Session));

        await InvokeSendPath(connection, path);

        Assert.All(HttpTest.CallLog.Where(x => !x.Uri.AbsolutePath.EndsWith("/Login")),
            call =>
            {
                Assert.Contains(call.Headers, x => x is { Key: "Cookie", Value: SessionCookieHeader });
                Assert.DoesNotContain(call.Headers, x => x.Key is "Authorization" or "X-b1-companyid");
            });
        HttpTest.ShouldHaveCalled("*/Login").WithoutHeader("Authorization").Times(1);
    }

    [Fact]
    public async Task SessionMode_IsUnaffected_NoTokenHeadersAndLoginAsBefore()
    {
        var connection = CreateConnection("v2");

        await connection.Request("Items").GetStringAsync();

        Assert.False(connection.IsUsingAccessTokens);
        Assert.True(connection.HasSessionCredentials);
        HttpTest.ShouldHaveCalled("*/Login").WithVerb(HttpMethod.Post).WithoutHeader("Authorization").Times(1);
        HttpTest.ShouldHaveCalled("*/Items").WithHeader("Cookie", SessionCookieHeader).WithoutHeader("Authorization").WithoutHeader("X-b1-companyid").Times(1);
    }

    [Fact]
    public async Task SessionMode_Unauthorized_StillLogsInAgain()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401).RespondWith("{}");
        var connection = CreateConnection("v2");
        connection.RetryDelay = TimeSpan.Zero;

        await connection.Request("Items").GetStringAsync();

        HttpTest.ShouldHaveCalled("*/Login").Times(2);
        HttpTest.ShouldHaveCalled("*/Items").Times(2);
    }

    [Fact]
    public async Task TokenMode_SetsCookieResponse_IsNeverSentBack()
    {
        // The Service Layer sets ROUTEID on token requests, which must not turn the next request into one carrying a cookie
        HttpTest.ForCallsTo("*/Items").RespondWith("{}", cookies: new { ROUTEID = ".node1" });
        var connection = CreateTokenConnection(new RecordingTokenSource(Token));

        await connection.Request("Items").GetStringAsync();
        await connection.Request("Items").GetStringAsync();

        AssertTokenCall("*/Items", times: 2);
    }

    [Fact]
    public async Task TokenMode_UserSetCookieHeader_IsDropped()
    {
        var connection = CreateTokenConnection(new RecordingTokenSource(Token));

        await connection.Request("Items").WithHeader("Cookie", "B1SESSION=stolen").GetStringAsync();

        AssertTokenCall("*/Items", times: 1);
    }

    [Fact]
    public async Task TokenMode_BeforeCallHandlerAddingCredentials_CanNotOverrideIdentity()
    {
        var connection = CreateTokenConnection(new RecordingTokenSource(Token));
        connection.BeforeCall(context =>
        {
            context.RequestMessage.Headers.TryAddWithoutValidation("Cookie", "B1SESSION=stolen");
            context.RequestMessage.Headers.TryAddWithoutValidation("Authorization", "Bearer other");
            context.RequestMessage.Headers.TryAddWithoutValidation("X-b1-companyid", "9999");
        });

        await connection.Request("Items").GetStringAsync();

        AssertTokenCall("*/Items", times: 1);
    }

    [Fact]
    public async Task TokenMode_UserSetAuthorizationOrCompanyHeader_IsReplaced()
    {
        var connection = CreateTokenConnection(new RecordingTokenSource(Token));

        await connection.Request("Items")
            .WithHeader("Authorization", "Bearer other")
            .WithHeader("X-b1-companyid", "9999")
            .GetStringAsync();

        AssertTokenCall("*/Items", times: 1);
    }

    [Fact]
    public async Task TokenMode_CookieInHttpClientDefaultHeaders_FailsInsteadOfSendingBoth()
    {
        var connection = CreateTokenConnection(new RecordingTokenSource(Token));
        connection.HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "B1SESSION=stolen");

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Request("Items").GetStringAsync());

        Assert.Empty(HttpTest.CallLog);
    }

    [Fact]
    public async Task TokenMode_FailureToPrepareTheRequest_DoesNotExposeTheTokenToHandlers()
    {
        var connection = CreateTokenConnection(new RecordingTokenSource(Token));
        connection.HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "B1SESSION=stolen");
        var observed = new List<string>();
        connection.OnError(context => observed.Add(context.RequestMessage.ToString()));
        connection.AfterCall(context => observed.Add(context.RequestMessage.ToString()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Request("Items").GetStringAsync());

        Assert.Equal(2, observed.Count);
        Assert.All(observed, x => Assert.DoesNotContain(Token, x));
    }

    [Fact]
    public async Task SessionIdentity_OnTokenConnection_WithUserSetBearerHeader_FailsInsteadOfSendingBoth()
    {
        // Sent as it is, the cookie would win and the request would silently run as the session account
        var connection = CreateResolverConnection(_ => Task.FromResult(SLRequestIdentity.Session));

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Request("Items").WithHeader("Authorization", "Bearer user-token").GetStringAsync());

        HttpTest.ShouldNotHaveCalled("*/Items");
    }

    [Fact]
    public async Task SessionIdentity_OnTokenConnection_WithBearerInHttpClientDefaultHeaders_FailsInsteadOfSendingBoth()
    {
        var connection = CreateResolverConnection(_ => Task.FromResult(SLRequestIdentity.Session));
        connection.HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer user-token");

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Request("Items").GetStringAsync());

        HttpTest.ShouldNotHaveCalled("*/Items");
    }

    [Fact]
    public async Task SessionConnection_WithoutAccessTokens_LeavesABearerHeaderToTheCaller()
    {
        // For example, for an API gateway in front of the Service Layer that requires its own token
        var connection = CreateConnection("v2");

        await connection.Request("Items").WithHeader("Authorization", "Bearer gateway-token").GetStringAsync();

        HttpTest.ShouldHaveCalled("*/Items").WithHeader("Authorization", "Bearer gateway-token").WithHeader("Cookie", SessionCookieHeader).Times(1);
    }

    [Fact]
    public async Task SessionConnection_WithoutAccessTokens_LeavesABearerDefaultHeaderToTheCaller()
    {
        var connection = CreateConnection("v2");
        connection.HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer gateway-token");

        await connection.Request("Items").GetStringAsync();

        HttpTest.ShouldHaveCalled("*/Items").WithHeader("Authorization", "Bearer gateway-token").WithHeader("Cookie", SessionCookieHeader).Times(1);
    }

    [Fact]
    public async Task SessionIdentity_WithNonBearerAuthorizationHeader_KeepsWorkingAsBefore()
    {
        // For example, for a proxy in front of the Service Layer: not a token, so existing behavior is left untouched
        var connection = CreateConnection("v2");

        await connection.Request("Items").WithHeader("Authorization", "Basic dXNlcjpwYXNz").GetStringAsync();

        HttpTest.ShouldHaveCalled("*/Items").WithHeader("Authorization", "Basic dXNlcjpwYXNz").WithHeader("Cookie", SessionCookieHeader).Times(1);
    }

    [Fact]
    public async Task Batch_InTokenMode_DropsCredentialHeadersOfInnerRequests()
    {
        HttpTest.RespondWith("--b\r\n\r\nHTTP/1.1 200 OK\r\n\r\n{}\r\n--b--");
        var connection = CreateTokenConnection(new RecordingTokenSource(Token), true);

        var innerRequest = new SLBatchRequest(HttpMethod.Get, "Items('A')")
            .WithHeader("Cookie", "B1SESSION=stolen")
            .WithHeader("X-b1-companyid", "9999")
            .WithHeader("Authorization", "Bearer other")
            .WithHeader("B1S-PageSize", "5");

        await connection.PostBatchAsync(innerRequest);

        AssertTokenCall("*/$batch", times: 1);
        var body = HttpTest.CallLog.Single().RequestBody;
        Assert.DoesNotContain("stolen", body);
        Assert.DoesNotContain("9999", body);
        Assert.DoesNotContain("Bearer", body);
        Assert.Contains("B1S-PageSize: 5", body);
    }

    [Fact]
    public async Task Batch_Unauthorized_RefreshesAndRetriesWithFreshContent()
    {
        HttpTest.ForCallsTo("*/$batch").RespondWith(Unauthorized, 401).RespondWith("--b\r\n\r\nHTTP/1.1 200 OK\r\n\r\n{}\r\n--b--");
        var connection = CreateTokenConnection(new RecordingTokenSource("token-1", "token-2"));

        var responses = await connection.PostBatchAsync(new SLBatchRequest(HttpMethod.Get, "Items"));

        Assert.Single(responses);
        HttpTest.ShouldHaveCalled("*/$batch").WithHeader("Authorization", "Bearer token-1").Times(1);
        HttpTest.ShouldHaveCalled("*/$batch").WithHeader("Authorization", "Bearer token-2").WithRequestBody("*GET *Items*").Times(1);
        HttpTest.ShouldNotHaveCalled("*/Login");
    }

    [Fact]
    public async Task PerRequestIdentity_SwitchesAtSendTime_ThroughAmbientContext()
    {
        var ambientUser = new AsyncLocal<string>();
        var sources = new Dictionary<string, ISLAccessTokenSource>
        {
            ["alice"] = new RecordingTokenSource("alice-token"),
            ["bob"] = new RecordingTokenSource("bob-token")
        };

        var connection = CreateResolverConnection(_ => Task.FromResult(ambientUser.Value is { } user
            ? SLRequestIdentity.FromTokenSource(sources[user], user == "alice" ? "1" : "2")
            : SLRequestIdentity.Session));

        // The request object is created before the user is known: the decision belongs to the send
        var request = connection.Request("Items");
        ambientUser.Value = "alice";
        await request.GetStringAsync();

        ambientUser.Value = null;
        await connection.Request("Items").GetStringAsync();

        ambientUser.Value = "bob";
        await connection.Request("Items").GetStringAsync();

        var calls = HttpTest.CallLog.Where(x => x.Uri.AbsolutePath.EndsWith("/Items")).ToList();
        Assert.Equal(3, calls.Count);

        Assert.Contains(calls[0].Headers, x => x is { Key: "Authorization", Value: "Bearer alice-token" });
        Assert.Contains(calls[0].Headers, x => x is { Key: "X-b1-companyid", Value: "1" });
        Assert.DoesNotContain(calls[0].Headers, x => x.Key == "Cookie");

        Assert.Contains(calls[1].Headers, x => x is { Key: "Cookie", Value: SessionCookieHeader });
        Assert.DoesNotContain(calls[1].Headers, x => x.Key is "Authorization" or "X-b1-companyid");

        Assert.Contains(calls[2].Headers, x => x is { Key: "Authorization", Value: "Bearer bob-token" });
        Assert.Contains(calls[2].Headers, x => x is { Key: "X-b1-companyid", Value: "2" });
        Assert.DoesNotContain(calls[2].Headers, x => x.Key == "Cookie");

        HttpTest.ShouldHaveCalled("*/Login").Times(1);
    }

    [Fact]
    public async Task PerRequestIdentity_ConcurrentMixedRequests_NeverCrossIdentities()
    {
        var ambientUser = new AsyncLocal<string>();
        var connection = CreateResolverConnection(_ => Task.FromResult(ambientUser.Value is { } user
            ? SLRequestIdentity.FromTokenSource(SLAccessTokenSource.FromDelegate((_, _) => Task.FromResult(user + "-token")), user)
            : SLRequestIdentity.Session));

        await Task.WhenAll(Enumerable.Range(0, 200)
            .Select(async i =>
            {
                await Task.Yield();
                ambientUser.Value = i % 3 == 0 ? null : $"user{i}";
                await connection.Request($"Items('{ambientUser.Value ?? "session"}')").GetStringAsync();
            }));

        var calls = HttpTest.CallLog.Where(x => x.Uri.AbsolutePath.Contains("/Items")).ToList();
        Assert.Equal(200, calls.Count);

        Assert.All(calls,
            call =>
            {
                var resource = Uri.UnescapeDataString(call.Uri.AbsolutePath);

                if (resource.Contains("session"))
                {
                    Assert.Contains(call.Headers, x => x.Key == "Cookie");
                    Assert.DoesNotContain(call.Headers, x => x.Key is "Authorization" or "X-b1-companyid");
                }
                else
                {
                    var user = resource.Split('\'')[1];
                    Assert.Contains(call.Headers, x => x.Key == "Authorization" && x.Value == $"Bearer {user}-token");
                    Assert.Contains(call.Headers, x => x.Key == "X-b1-companyid" && x.Value == user);
                    Assert.DoesNotContain(call.Headers, x => x.Key == "Cookie");
                }
            });
    }

    [Fact]
    public async Task Resolver_RunsOncePerRequest_EvenAcrossRetries()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith("{}", 502).RespondWith("{}", 502).RespondWith("{}");
        var calls = 0;

        var connection = CreateResolverConnection(_ =>
        {
            calls++;
            return Task.FromResult(SLRequestIdentity.FromTokenSource(new RecordingTokenSource(Token), CompanyId));
        });

        await connection.Request("Items").GetStringAsync();

        HttpTest.ShouldHaveCalled("*/Items").Times(3);
        Assert.Equal(1, calls);

        await connection.Request("Items").GetStringAsync();

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Resolver_ChangingItsAnswer_DoesNotAffectARetryOfTheRequest()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401).RespondWith("{}");
        var firstUser = new RecordingTokenSource("first-user-1", "first-user-2");
        var secondUser = new RecordingTokenSource("second-user");
        var calls = 0;

        var connection = CreateResolverConnection(_ => Task.FromResult(SLRequestIdentity.FromTokenSource(Interlocked.Increment(ref calls) == 1 ? firstUser : secondUser, CompanyId)));

        await connection.Request("Items").GetStringAsync();

        HttpTest.ShouldHaveCalled("*/Items").WithHeader("Authorization", "Bearer first-user-1").Times(1);
        HttpTest.ShouldHaveCalled("*/Items").WithHeader("Authorization", "Bearer first-user-2").Times(1);
        Assert.Empty(secondUser.Requests);
    }

    [Fact]
    public async Task Resolver_ReturningNull_FailsInsteadOfUsingTheSession()
    {
        var connection = CreateResolverConnection(_ => Task.FromResult<SLRequestIdentity>(null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Request("Items").GetStringAsync());

        Assert.Empty(HttpTest.CallLog);
    }

    [Fact]
    public async Task TokenSourceFailure_ReachesOnError_ButNotTheOtherHandlers()
    {
        var failure = new SLAuthenticationException("invalid_grant");
        var connection = CreateTokenConnection(SLAccessTokenSource.FromDelegate((_, _) => Task.FromException<string>(failure)));
        var errors = new List<(Uri Uri, HttpMethod Method, bool HasAuthorization, Exception Exception, bool Completed, int Attempt, SLRequestIdentity Identity, string Body)>();
        var otherCalls = 0;
        connection.BeforeCall(_ => otherCalls++);
        connection.AfterCall(_ => otherCalls++);
        connection.OnError(context => errors.Add((context.RequestMessage.RequestUri, context.RequestMessage.Method, context.RequestMessage.Headers.Contains("Authorization"), context.Exception, context.Completed, context.AttemptNumber, context.Identity, context.RequestBody)));

        var thrown = await Assert.ThrowsAsync<SLAuthenticationException>(() => connection.Request("Items").PostAsync(new { ItemCode = "A" }));

        Assert.Same(failure, thrown);
        var error = Assert.Single(errors);
        Assert.EndsWith("/Items", error.Uri.AbsoluteUri);
        Assert.Equal(HttpMethod.Post, error.Method);
        Assert.False(error.HasAuthorization);
        Assert.Same(failure, error.Exception);
        Assert.False(error.Completed);
        Assert.Equal(1, error.Attempt);
        Assert.False(error.Identity.IsSession);
        Assert.Contains("ItemCode", error.Body);
        Assert.Equal(0, otherCalls);
        Assert.Empty(HttpTest.CallLog);
    }

    [Fact]
    public async Task TokenRefreshFailure_AfterA401_ReachesOnErrorForBothFailures()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401);
        var calls = 0;
        var connection = CreateTokenConnection(SLAccessTokenSource.FromDelegate((_, _) => Interlocked.Increment(ref calls) == 1 ? Task.FromResult(Token) : Task.FromException<string>(new SLAuthenticationException("invalid_grant"))));
        var errors = new List<(int Attempt, Type Type)>();
        connection.OnError(context => errors.Add((context.AttemptNumber, context.Exception.GetType())));

        await Assert.ThrowsAsync<SLAuthenticationException>(() => connection.Request("Items").GetStringAsync());

        Assert.Equal([(1, typeof(SLException)), (2, typeof(SLAuthenticationException))], errors);
    }

    [Fact]
    public async Task ResolverFailure_ReachesOnError_WithNoIdentity()
    {
        var failure = new TimeoutException("no user");
        var connection = CreateResolverConnection(_ => throw failure);
        SLCallContext reported = null;
        Uri requestUri = null;
        connection.OnError(context =>
        {
            reported = context;
            requestUri = context.RequestMessage.RequestUri;
        });

        Assert.Same(failure, await Assert.ThrowsAsync<TimeoutException>(() => connection.Request("Items").GetStringAsync()));

        Assert.Same(failure, reported.Exception);
        Assert.Null(reported.Identity);
        Assert.EndsWith("/Items", requestUri.AbsoluteUri);
        Assert.Empty(HttpTest.CallLog);
    }

    [Fact]
    public async Task TokenSourceFailure_OfABatch_DoesNotExposeInnerCredentialHeaders()
    {
        var connection = CreateTokenConnection(SLAccessTokenSource.FromDelegate((_, _) => Task.FromException<string>(new SLAuthenticationException("invalid_grant"))));
        string reportedBody = null;
        connection.OnError(async context => reportedBody = await context.RequestMessage.Content.ReadAsStringAsync());
        var innerRequest = new SLBatchRequest(HttpMethod.Get, "Items").WithHeader("Authorization", "Bearer inner.token");

        await Assert.ThrowsAsync<SLAuthenticationException>(() => connection.PostBatchAsync(innerRequest));

        Assert.DoesNotContain("inner.token", reportedBody);
    }

    [Fact]
    public async Task RequestThatCanNotBeBuilt_StillReportsTheOriginalFailure()
    {
        var failure = new TimeoutException("no user");
        var connection = CreateResolverConnection(_ => throw failure);
        var handlerCalled = false;
        HttpRequestMessage reportedMessage = null;
        Exception reported = null;
        connection.OnError(context =>
        {
            handlerCalled = true;
            reportedMessage = context.RequestMessage;
            reported = context.Exception;
        });

        // With no token credential, this batch breaks the rule against a session request with a bearer token while being built
        var innerRequest = new SLBatchRequest(HttpMethod.Get, "Items").WithHeader("Authorization", "Bearer inner.token");

        Assert.Same(failure, await Assert.ThrowsAsync<TimeoutException>(() => connection.PostBatchAsync(innerRequest)));

        Assert.True(handlerCalled);
        Assert.Null(reportedMessage);
        Assert.Same(failure, reported);
    }

    [Fact]
    public async Task Resolver_ReturningANullTask_FailsLikeANullIdentity()
    {
        var connection = CreateResolverConnection(_ => null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Request("Items").GetStringAsync());

        Assert.Empty(HttpTest.CallLog);
    }

    [Fact]
    public async Task SessionBatch_WithABearerTokenOnAnInnerRequest_FailsWithoutSending()
    {
        var connection = CreateResolverConnection(_ => Task.FromResult(SLRequestIdentity.Session));
        var innerRequest = new SLBatchRequest(HttpMethod.Get, "Items").WithHeader("Authorization", "Bearer user.token");

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.PostBatchAsync(innerRequest));

        HttpTest.ShouldNotHaveCalled("*/$batch");
    }

    [Fact]
    public async Task Resolver_Throwing_PropagatesWithoutSending()
    {
        var connection = CreateResolverConnection(_ => throw new TimeoutException("no user"));

        await Assert.ThrowsAsync<TimeoutException>(() => connection.Request("Items").GetStringAsync());

        Assert.Empty(HttpTest.CallLog);
    }

    [Fact]
    public async Task Resolver_IgnoringItsToken_StopsWaitingOnCancellation_WithAnInfiniteTimeout()
    {
        var never = new TaskCompletionSource<SLRequestIdentity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = CreateResolverConnection(_ => never.Task);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.Request("Items").WithTimeout(Timeout.InfiniteTimeSpan).GetStringAsync(cancellation.Token));

        Assert.Empty(HttpTest.CallLog);
    }

    [Fact]
    public void ProvidedHttpClient_ServingSessionAndTokenRequests_IsRejected()
    {
        using var httpClient = new HttpClient(new HttpClientHandler());

        var exception = Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://server:50000/b1s/v2"),
            CompanyDB = "SBODEMOUS",
            UserName = "manager",
            Password = "secret",
            HttpClient = httpClient,
            IdentityResolver = _ => Task.FromResult(SLRequestIdentity.Session)
        }));

        Assert.Contains(nameof(SLConnectionOptions.HttpMessageHandler), exception.Message);
    }

    [Fact]
    public void ProvidedHandler_HasItsAutomaticRedirectsDisabled_OnATokenConnection()
    {
        using var handler = new HttpClientHandler();

        _ = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://server:50000/b1s/v2"),
            HttpMessageHandler = new PassThroughHandler(handler),
            AccessTokenSource = new RecordingTokenSource(Token),
            CompanyId = CompanyId
        });

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    [Fact]
    public void ProvidedHttpClient_ServingOnlyTokenRequests_IsAccepted()
    {
        using var httpClient = new HttpClient(new HttpClientHandler());

        _ = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://server:50000/b1s/v2"),
            HttpClient = httpClient,
            AccessTokenSource = new RecordingTokenSource(Token),
            CompanyId = CompanyId
        });
    }

    [Fact]
    public async Task Resolver_ReturningSessionWithoutSessionCredentials_FailsClearly()
    {
        var connection = CreateResolverConnection(_ => Task.FromResult(SLRequestIdentity.Session), false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => connection.Request("Items").GetStringAsync());

        Assert.Contains("no session credentials", exception.Message);
        Assert.Empty(HttpTest.CallLog);
    }

    [Fact]
    public async Task Unauthorized_RefreshesTokenOnceAndRetries()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401).RespondWith("{}");
        var source = new RecordingTokenSource("token-1", "token-2");
        var connection = CreateTokenConnection(source, true);

        await connection.Request("Items").GetStringAsync();

        AssertTokenCall("*/Items", "token-1", times: 1);
        AssertTokenCall("*/Items", "token-2", times: 1);
        HttpTest.ShouldNotHaveCalled("*/Login");

        var requests = source.Requests.ToList();
        Assert.Equal(2, requests.Count);
        Assert.False(requests[0].ForceRefresh);
        Assert.Null(requests[0].RejectedAccessToken);
        Assert.True(requests[1].ForceRefresh);
        Assert.Equal("token-1", requests[1].RejectedAccessToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnauthorizedAgainAfterRefresh_SurfacesWithoutFurtherRetryOrLogin(bool withSessionCredentials)
    {
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401);
        var source = new RecordingTokenSource("token-1", "token-2", "token-3");
        var connection = CreateTokenConnection(source, withSessionCredentials);
        connection.NumberOfAttempts = 5;

        var exception = await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").GetStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        HttpTest.ShouldHaveCalled("*/Items").Times(2);
        Assert.Equal(2, source.Requests.Count);
        HttpTest.ShouldNotHaveCalled("*/Login");
    }

    [Fact]
    public async Task PerRequestToken_Unauthorized_NeverFallsBackToTheSessionAccount()
    {
        HttpTest.ForCallsTo("*/Items").With(x => x.Headers.Any(h => h.Key == "Authorization")).RespondWith(Unauthorized, 401);
        HttpTest.ForCallsTo("*/Items").RespondWith("{\"as\":\"session\"}");

        var source = new RecordingTokenSource("token-1", "token-2");
        var connection = CreateResolverConnection(_ => Task.FromResult(SLRequestIdentity.FromTokenSource(source, CompanyId)));

        var exception = await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").GetStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        HttpTest.ShouldNotHaveCalled("*/Login");
        Assert.All(HttpTest.CallLog, call => Assert.DoesNotContain(call.Headers, x => x.Key == "Cookie"));
    }

    [Fact]
    public async Task SourceReturningTheRejectedToken_StopsWithoutResending()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401);
        var source = new RecordingTokenSource(Token);
        var connection = CreateTokenConnection(source);

        var exception = await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").GetStringAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        HttpTest.ShouldHaveCalled("*/Items").Times(1);
        Assert.Equal(2, source.Requests.Count);
    }

    [Fact]
    public async Task Unauthorized_WithSingleAttempt_RefreshesOnce_LikeASessionRelogin()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401).RespondWith("{}");
        var source = new RecordingTokenSource("token-1", "token-2");
        var connection = CreateTokenConnection(source, configure: x => x.NumberOfAttempts = 1);

        await connection.Request("Items").GetStringAsync();

        Assert.Equal([false, true], source.Requests.Select(x => x.ForceRefresh));
        HttpTest.ShouldHaveCalled("*/Items").Times(2);
    }

    [Fact]
    public async Task Unauthorized_WithSingleAttempt_AfterRefresh_IsNotRetriedAgain()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401);
        var source = new RecordingTokenSource("token-1", "token-2", "token-3");
        var connection = CreateTokenConnection(source, configure: x => x.NumberOfAttempts = 1);

        await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").GetStringAsync());

        HttpTest.ShouldHaveCalled("*/Items").Times(2);
    }

    [Fact]
    public async Task ServerError_WithSingleAttempt_IsNotRetried()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith("{}", 500).RespondWith("{}");
        var source = new RecordingTokenSource("token-1", "token-2");
        var connection = CreateTokenConnection(source, configure: x => x.NumberOfAttempts = 1);

        await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").GetStringAsync());

        HttpTest.ShouldHaveCalled("*/Items").Times(1);
        Assert.Single(source.Requests);
    }

    [Fact]
    public async Task Unauthorized_NotInStatusCodesToRetry_IsNotRetried()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401).RespondWith("{}");
        var source = new RecordingTokenSource("token-1", "token-2");
        var connection = CreateTokenConnection(source);
        connection.HttpStatusCodesToRetry.Remove(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").GetStringAsync());

        HttpTest.ShouldHaveCalled("*/Items").Times(1);
        Assert.Single(source.Requests);
    }

    [Fact]
    public async Task UnauthorizedAllowedByCaller_IsReturnedWithoutRefreshing()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401);
        var source = new RecordingTokenSource("token-1", "token-2");
        var connection = CreateTokenConnection(source);

        var body = await connection.Request("Items").AllowHttpStatus(HttpStatusCode.Unauthorized).GetStringAsync();

        Assert.Equal(Unauthorized, body);
        Assert.Single(source.Requests);
    }

    [Fact]
    public async Task TransientFailures_RetryWithinNumberOfAttempts_WithoutForcingARefresh()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith("{}", 502).RespondWith("{}", 503).RespondWith("{}");
        var source = new RecordingTokenSource(Token);
        var connection = CreateTokenConnection(source);

        await connection.Request("Items").GetStringAsync();

        HttpTest.ShouldHaveCalled("*/Items").Times(3);
        Assert.All(source.Requests, x => Assert.False(x.ForceRefresh));
    }

    [Fact]
    public async Task TransientFailure_AfterRefresh_DoesNotForceAnotherRefresh()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401).RespondWith("{}", 502).RespondWith("{}");
        var source = new RecordingTokenSource("token-1", "token-2");
        var connection = CreateTokenConnection(source);

        await connection.Request("Items").GetStringAsync();

        Assert.Equal([false, true, false], source.Requests.Select(x => x.ForceRefresh));
        AssertTokenCall("*/Items", "token-2", times: 2);
    }

    [Fact]
    public async Task UnauthorizedOnLastAttempt_StillRefreshes()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith("{}", 502).RespondWith(Unauthorized, 401).RespondWith("{}");
        var source = new RecordingTokenSource("token-1", "token-2", "token-3");
        var connection = CreateTokenConnection(source, configure: x => x.NumberOfAttempts = 2);

        await connection.Request("Items").GetStringAsync();

        Assert.Equal([false, false, true], source.Requests.Select(x => x.ForceRefresh));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TokenSourceFailure_SurfacesAsExceptionWithoutFallback(bool withSessionCredentials)
    {
        var failure = new InvalidOperationException("the identity provider is down");
        var connection = CreateTokenConnection(SLAccessTokenSource.FromDelegate((_, _) => throw failure), withSessionCredentials);

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => connection.Request("Items").GetStringAsync());

        Assert.Same(failure, exception.InnerException);
        Assert.Empty(HttpTest.CallLog);
    }

    [Fact]
    public async Task TokenSourceFailure_WhileRefreshingAfterUnauthorized_SurfacesWithoutFallback()
    {
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401);
        var calls = 0;

        var source = SLAccessTokenSource.FromDelegate((_, _) => Interlocked.Increment(ref calls) == 1
            ? Task.FromResult(Token)
            : throw new InvalidOperationException("refresh failed"));

        var connection = CreateTokenConnection(source, true);

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => connection.Request("Items").GetStringAsync());

        Assert.Equal("refresh failed", exception.InnerException?.Message);
        HttpTest.ShouldHaveCalled("*/Items").Times(1);
        HttpTest.ShouldNotHaveCalled("*/Login");
    }

    [Fact]
    public async Task TokenSource_ThrowingSLAuthenticationException_IsNotWrapped()
    {
        var failure = new SLAuthenticationException("invalid_grant", oAuthError: "invalid_grant");
        var connection = CreateTokenConnection(SLAccessTokenSource.FromDelegate((_, _) => throw failure));

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => connection.Request("Items").GetStringAsync());

        Assert.Same(failure, exception);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("=")]
    [InlineData("with space")]
    [InlineData("line\r\nX-Injected: 1")]
    [InlineData("a=b")]
    public async Task TokenSource_ReturningInvalidToken_FailsWithoutSendingIt(string invalidToken)
    {
        var connection = CreateTokenConnection(new RecordingTokenSource(invalidToken));

        var exception = await Assert.ThrowsAsync<SLAuthenticationException>(() => connection.Request("Items").GetStringAsync());

        Assert.Empty(HttpTest.CallLog);

        if (!string.IsNullOrWhiteSpace(invalidToken))
        {
            Assert.DoesNotContain(invalidToken, exception.ToString());
        }
    }

    [Fact]
    public async Task TokenSource_CallerCancellation_IsNotWrapped()
    {
        using var cancellation = new CancellationTokenSource();

        var connection = CreateTokenConnection(SLAccessTokenSource.FromDelegate(async (_, token) =>
        {
            await cancellation.CancelAsync();
            token.ThrowIfCancellationRequested();
            return Token;
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connection.Request("Items").GetStringAsync(cancellation.Token));
    }

    [Fact]
    public async Task TokenSource_OwnTimeout_SurfacesAsAuthenticationFailure()
    {
        var connection = CreateTokenConnection(SLAccessTokenSource.FromDelegate((_, _) => throw new TaskCanceledException("internal timeout")));

        await Assert.ThrowsAsync<SLAuthenticationException>(() => connection.Request("Items").GetStringAsync());
    }

    [Fact]
    public async Task ExplicitLoginAndLogout_OnTokenOnlyConnection_FailClearly()
    {
        var connection = CreateTokenConnection(new RecordingTokenSource(Token));

        var loginException = await Assert.ThrowsAsync<InvalidOperationException>(() => connection.LoginAsync());
        var logoutException = await Assert.ThrowsAsync<InvalidOperationException>(() => connection.LogoutAsync());

        Assert.Contains("no session credentials", loginException.Message);
        Assert.Contains("no session credentials", logoutException.Message);
        Assert.Empty(HttpTest.CallLog);
        Assert.False(connection.HasSessionCredentials);
        Assert.True(connection.IsUsingAccessTokens);
    }

    [Fact]
    public async Task Logout_OnTokenOnlyConnection_PointedAtAnExistingSession_DoesNotEndIt()
    {
        // The key is settable, so a token-only connection could be pointed at the session of another connection
        var sessionConnection = CreateConnection("v2");
        await sessionConnection.LoginAsync();

        var connection = CreateTokenConnection(new RecordingTokenSource(Token));
        connection.SessionCacheKey = sessionConnection.SessionCacheKey;

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.LogoutAsync());

        HttpTest.ShouldNotHaveCalled("*/Logout");
    }

    [Fact]
    public async Task ExplicitLogin_WithSessionCredentials_ConcernsTheSessionAndChangesNoIdentity()
    {
        var connection = CreateTokenConnection(new RecordingTokenSource(Token), true);

        await connection.LoginAsync();
        await connection.Request("Items").GetStringAsync();
        await connection.LogoutAsync();

        HttpTest.ShouldHaveCalled("*/Login").WithoutHeader("Authorization").Times(1);
        HttpTest.ShouldHaveCalled("*/Logout").WithHeader("Cookie", SessionCookieHeader).WithoutHeader("Authorization").Times(1);
        AssertTokenCall("*/Items", times: 1);
    }

    [Fact]
    public async Task Ping_InTokenMode_CarriesNoCredentials()
    {
        HttpTest.ForCallsTo("*/ping/*").RespondWithJson(new { message = "pong" });
        var source = new RecordingTokenSource(Token);
        var connection = CreateTokenConnection(source);

        await connection.PingAsync();

        HttpTest.ShouldHaveCalled("*/ping/*").WithoutHeader("Authorization").WithoutHeader("Cookie").WithoutHeader("X-b1-companyid").Times(1);
        Assert.Empty(source.Requests);
    }

    [Fact]
    public async Task SameOriginRedirect_ReappliesOnlyTheToken()
    {
        HttpTest.ForCallsTo("https://sapserver:50000/b1s/v2/Items")
            .RespondWith(status: 302, headers: new Dictionary<string, string> { ["Location"] = "https://sapserver:50000/b1s/v2/Moved" }, cookies: new { ROUTEID = ".node1" });
        HttpTest.ForCallsTo("https://sapserver:50000/b1s/v2/Moved").RespondWith("{}");
        var connection = CreateTokenConnection(new RecordingTokenSource(Token));

        await connection.Request("Items").GetStringAsync();

        AssertTokenCall("*/Moved", times: 1);
    }

    [Theory]
    [InlineData("https://elsewhere:50000/b1s/v2/Moved")]
    [InlineData("https://sapserver:50001/b1s/v2/Moved")]
    [InlineData("http://sapserver:50000/b1s/v2/Moved")]
    public async Task CrossOriginRedirect_IsNotFollowed_SoTheTokenStaysHome(string target)
    {
        HttpTest.ForCallsTo("https://sapserver:50000/b1s/v2/Items")
            .RespondWith(status: 302, headers: new Dictionary<string, string> { ["Location"] = target });
        var connection = CreateTokenConnection(new RecordingTokenSource(Token));

        var exception = await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").GetStringAsync());

        Assert.Equal(HttpStatusCode.Redirect, exception.StatusCode);
        Assert.Single(HttpTest.CallLog);
    }

    [Fact]
    public async Task CallEventHandlers_NeverSeeTheToken()
    {
        const string secret = "very.secret-token";
        var observed = new StringBuilder();

        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401);
        var connection = CreateTokenConnection(new RecordingTokenSource(secret, secret + "2"));

        void Observe(SLCallContext context)
        {
            observed.AppendLine(context.RequestMessage.ToString());
            observed.AppendLine(context.ResponseMessage?.RequestMessage?.ToString());
            observed.AppendLine(context.Exception?.ToString());
            Assert.False(context.Identity.IsSession);
            Assert.Equal(CompanyId, context.Identity.CompanyId);
        }

        connection.BeforeCall(Observe).AfterCall(Observe).OnError(Observe);

        var exception = await Assert.ThrowsAsync<SLException>(() => connection.Request("Items").GetStringAsync());

        HttpTest.ShouldHaveCalled("*/Items").WithHeader("Authorization", "Bearer " + secret).Times(1);
        Assert.Contains("X-b1-companyid", observed.ToString());
        Assert.DoesNotContain("secret-token", observed.ToString());
        Assert.DoesNotContain("secret-token", exception.ToString());
    }

    [Fact]
    public async Task SessionCache_IsNeverTouchedByTokenRequests()
    {
        var cache = new RecordingCache();
        HttpTest.ForCallsTo("*/Items").RespondWith(Unauthorized, 401).RespondWith("{}", cookies: new { ROUTEID = ".node1" });

        var connection = CreateTokenConnection(new RecordingTokenSource("token-1", "token-2"), true, configure: x => x.DistributedCache = cache);

        await connection.Request("Items").GetStringAsync();

        Assert.Empty(cache.Operations);
    }

    [Fact]
    public async Task SessionCache_WithValidSession_IsNotUsedForATokenRequest()
    {
        // A valid session sitting in the cache is exactly what must not be picked up by a token request
        var connection = CreateTokenConnection(new RecordingTokenSource(Token), true);
        await connection.LoginAsync();

        await connection.Request("Items").GetStringAsync();

        AssertTokenCall("*/Items", times: 1);
    }

    [Fact]
    public void TokenOnlyConnections_DoNotShareASessionCacheKey()
    {
        var first = CreateTokenConnection(new RecordingTokenSource(Token));
        var second = CreateTokenConnection(new RecordingTokenSource(Token));

        Assert.NotEqual(first.SessionCacheKey, second.SessionCacheKey);
    }

    [Fact]
    public void Options_InvalidCombinations_AreRejected()
    {
        var source = new RecordingTokenSource(Token);
        var root = new Uri("https://sapserver:50000/b1s/v2");

        // A token source without a company, and a company without a token source
        Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions { ServiceLayerRoot = root, AccessTokenSource = source }));
        Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions { ServiceLayerRoot = root, AccessTokenSource = source, CompanyId = "a\r\nb" }));
        Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions { ServiceLayerRoot = root, CompanyId = CompanyId, CompanyDB = "A", UserName = "B", Password = "C" }));

        // Conflicting token configurations
        Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = root, AccessTokenSource = source, CompanyId = CompanyId, IdentityResolver = _ => Task.FromResult(SLRequestIdentity.Session)
        }));

        Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = root, IdentityResolver = _ => Task.FromResult(SLRequestIdentity.Session), CompanyId = CompanyId
        }));

        // A partial set of session credentials is more likely a mistake than a token-only connection
        Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = root, AccessTokenSource = source, CompanyId = CompanyId, UserName = "manager"
        }));

        // Requested certificate validation that couldn't be honored
        Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = root, AccessTokenSource = source, CompanyId = CompanyId, HttpMessageHandler = HttpTest, AccessTokenServerCertificateValidationCallback = (_, _, _, _) => true
        }));

        // The session mode's validation stays exactly as it was
        Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions { ServiceLayerRoot = root }));
    }

    [Fact]
    public void Options_HttpServiceLayerRoot_RequiresExplicitOptIn()
    {
        var root = new Uri("http://sapserver:50001/b1s/v2");
        var source = new RecordingTokenSource(Token);

        Assert.Throws<ArgumentException>(() => new SLConnection(new SLConnectionOptions { ServiceLayerRoot = root, AccessTokenSource = source, CompanyId = CompanyId }));

        var connection = new SLConnection(new SLConnectionOptions { ServiceLayerRoot = root, AccessTokenSource = source, CompanyId = CompanyId, AllowAccessTokensOverHttp = true });
        Assert.True(connection.IsUsingAccessTokens);

        // A session connection over HTTP keeps working as before
        Assert.NotNull(new SLConnection(root, "CompanyDB", "manager", "12345"));
    }

    [Fact]
    public async Task SingleSignOnSession_CanCoexistWithPerRequestTokens()
    {
        var useToken = false;

        var connection = new SLConnection(new SLConnectionOptions
        {
            ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
            GetServiceLayerConnectionContext = _ => "B1SESSION=sso;ROUTEID=.node0",
            IdentityResolver = _ => Task.FromResult(useToken ? SLRequestIdentity.FromTokenSource(new RecordingTokenSource(Token), CompanyId) : SLRequestIdentity.Session),
            HttpMessageHandler = HttpTest
        });

        await connection.Request("Items").GetStringAsync();
        useToken = true;
        await connection.Request("Orders").GetStringAsync();

        Assert.True(connection.HasSessionCredentials);
        HttpTest.ShouldHaveCalled("*/Items").WithHeader("Cookie", "B1SESSION=sso*").WithoutHeader("Authorization").Times(1);
        AssertTokenCall("*/Orders", times: 1);
    }

    [Fact]
    public void Identity_ToString_NeverIncludesTheToken()
    {
        const string secret = "very.secret-token";
        var identity = SLRequestIdentity.FromTokenSource(new RecordingTokenSource(secret), CompanyId);

        Assert.DoesNotContain("secret", identity.ToString());
        Assert.Contains(CompanyId, identity.ToString());
        Assert.DoesNotContain("secret", new SLAccessTokenRequest(true, secret).ToString());
        Assert.DoesNotContain("secret", SLRequestCredential.ForToken(identity, secret).ToString());
        Assert.DoesNotContain("secret", new SLTokenResponse(secret, refreshToken: secret, idToken: secret).ToString());
    }

    [Theory]
    [InlineData("eyJhbGciOi.J9-_~+/abc==", true)]
    [InlineData("abc", true)]
    [InlineData("abc=", true)]
    [InlineData("", false)]
    [InlineData("=abc", false)]
    [InlineData("a b", false)]
    [InlineData("a\nb", false)]
    [InlineData("a;b", false)]
    public void BearerTokenSyntax_FollowsRfc6750(string token, bool expected)
    {
        Assert.Equal(expected, SLRequestCredential.IsValidBearerToken(token));
    }

    private sealed class PassThroughHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler);

    private sealed class RecordingCache : IDistributedCache
    {
        public List<string> Operations { get; } = [];

        public byte[] Get(string key)
        {
            Operations.Add("Get");
            return null;
        }

        public Task<byte[]> GetAsync(string key, CancellationToken token = default)
        {
            Operations.Add("GetAsync");
            return Task.FromResult<byte[]>(null);
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Operations.Add("Set");

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Operations.Add("SetAsync");
            return Task.CompletedTask;
        }

        public void Refresh(string key) => Operations.Add("Refresh");

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            Operations.Add("RefreshAsync");
            return Task.CompletedTask;
        }

        public void Remove(string key) => Operations.Add("Remove");

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Operations.Add("RemoveAsync");
            return Task.CompletedTask;
        }
    }
}
