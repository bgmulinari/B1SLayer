# OAuth / OpenID Connect

B1SLayer can authenticate to the Service Layer with access tokens issued by the SAP Business One authentication server, available as of 10.0 FP 2208, as an alternative to a username and password or the SSO connection context. Those keep working exactly as before.

On the wire, a token request carries `Authorization: Bearer <token>` and `X-b1-companyid: <SLD company ID>`. The company ID is required, and without it the Service Layer responds with 401. `POST /Login` doesn't accept a token, answering a bearer `Authorization` header with 401 and code 300, so a token can't be exchanged for a session, and every request carries the token.

## Choosing a mode

| You have | Use | Identity |
|---|---|---|
| A daemon service with a technical user | `AccessTokenSource` + `CompanyId` | The same on every request |
| A single signed-in user | `AccessTokenSource` + `CompanyId` | The same on every request |
| A multi-user app with one cached connection | `IdentityResolver` | Decided per request, at send time |

SAP supports these client types, and all of them reach the Service Layer in the same way:

| SAP client type | Getting the tokens | In B1SLayer |
|---|---|---|
| Daemon Service (as of 10.0 FP 2502) | Client credentials grant | `SLOidcClient` handles it, see [Daemon service](#daemon-service) |
| Web App | Authorization code flow, through your OIDC library | `CreateRefreshTokenSource`, see [Web apps](#web-apps-and-the-authorization-code-flow) |
| Desktop App, Mobile App | Authorization code flow with PKCE, as a public client, through your OIDC library | `CreateRefreshTokenSource` with no `ClientCredential` |
| Single Page App | Authorization code flow, in the browser | Only in a .NET client such as Blazor WebAssembly, and it [needs CORS and the Service Layer audience](#single-page-apps) |

## Daemon service

This client type is available as of 10.0 FP 2502. Register a Daemon Service in the SAP Business One Extension Single Sign-On Manager, with a technical user whose code starts with `b1ext_`. Signed JWT is the credential type SAP highly recommends.

```csharp
// Keep it for as long as the token source is used, as the source requests its tokens through it
var oidc = new SLOidcClient(new SLOidcClientOptions
{
    SldAddress = new Uri("https://sapserver:40000"),
    ClientId = config.ClientId,
    ClientCredential = SLClientCredential.FromPrivateKey(config.PrivateKey)
});

var tokenSource = oidc.CreateClientCredentialsTokenSource();
var token = await tokenSource.GetAccessTokenAsync(SLAccessTokenRequest.Default);
var companyId = await oidc.GetCompanyIdAsync(token, "SBODEMOUS");

var serviceLayer = new SLConnection(new SLConnectionOptions
{
    ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
    AccessTokenSource = tokenSource,
    CompanyId = companyId
});
```

`SLClientCredential` offers three options:

- `FromPrivateKey`: the base64 PKCS#8 key from the SSO Manager, without PEM armor.
- `FromSigner`: for keys in a vault or HSM, where the delegate returns the RS256 signature. Signing and sending the request must take under about 50 seconds, as SAP's Keycloak rejects an assertion issued over 60 seconds earlier.
- `FromClientSecret`: sent in the request body, as in SAP's documentation.

The client credentials grant has no refresh token, so the source requests a new token when the current one is about to expire or is rejected.

Requests run as the technical user, so grant it the authorizations it needs in the SAP Business One client.

## Web apps and the authorization code flow

Signing users in belongs to your OIDC library, which handles the browser redirects, `state`, `nonce`, PKCE with S256, the code exchange and ID token validation. B1SLayer deliberately has no helper for this: the code exchange is a small part of the flow, and the rest is what actually protects it.

`SLOidcClient` tells your library where the provider is and hands the resulting tokens to the connection:

```csharp
// Configure your OIDC library with these, instead of hard-coding them
var metadata = await oidc.GetProviderMetadataAsync();
// metadata.Issuer, metadata.DiscoveryUri, metadata.EndSessionEndpoint, ...

// After the sign-in, show the companies of the user and let them choose
var userInfo = await oidc.GetCurrentUserInfoAsync(accessToken);
var companyId = userInfo.CompanyBindings.Single(x => x.CompanySchemaName == chosenSchema).CompanyId;

// Per user: caches the access token and refreshes it when needed, accepting a rotated refresh token
var tokenSource = oidc.CreateRefreshTokenSource(
    new SLTokenResponse(accessToken, expiresAtUtc, refreshToken, idToken),
    (tokens, cancellationToken) => tokenStore.SaveAsync(userId, tokens, cancellationToken));
```

A refresh token normally ends with the user's SSO session. SAP's guide doesn't cover outliving it, but `offline_access` is the standard OpenID Connect scope for that, if your landscape allows it. To use your own token storage instead, implement `ISLAccessTokenSource`, or wrap a delegate with `SLAccessTokenSource.FromDelegate`. Use `SLCachingAccessTokenSource` if you only need caching. When `ForceRefresh` is set on the request, the Service Layer has just rejected a token, so don't return a cached one.

If `onTokensRefreshed` fails, the source keeps using the new tokens, and the next request fails once with a `SLAuthenticationException` carrying that failure as its `InnerException`. The next refresh calls it again with newer tokens. Until then your store is out of date, so with rotating refresh tokens a process restart may force the user to sign in again.

When a refresh token is no longer valid, requests fail with a `SLAuthenticationException` whose `OAuthError` is `invalid_grant`, and the user must sign in again. `SLTokenResponse.RefreshExpiresAtUtc` tells you when to expect that, and is null when the refresh token has no expiry of its own. It is filled in by `SLOidcClient`, so tokens you build yourself only have it if you pass it in.

For logout, redirect the user to `metadata.EndSessionEndpoint` with `id_token_hint`. The Service Layer keeps accepting an access token until it expires, even after logout, so keep the access token lifetime in mind: SAP documents 30 minutes, configurable in Keycloak. `SLTokenResponse.IdToken` isn't validated by B1SLayer and must not be used as proof of identity. A refresh can omit the ID token, in which case `CreateRefreshTokenSource` keeps the previous one and `RefreshTokenAsync` returns null, so keep the previous one yourself.

SAP also describes OIDC back-channel logout, where the provider calls an endpoint of your app. That endpoint and its logout token validation belong to your web framework or OIDC library. `metadata.JwksUri` and `metadata.Issuer` are there for it.

## Single Page Apps

A browser client needs two things configured on the server, which B1SLayer can't do for it:

- **CORS**, as SAP requires for Single Page Apps. Enable it in `b1s.conf` (`CorsEnable`, `CorsAllowedOrigins`) and allow the headers of a token request through `CorsAllowedHeaders`: at least `Authorization` and `X-b1-companyid`, plus any others you send. See "Working with SAP Business One Service Layer". A failing preflight shows up as an opaque network failure, a `HttpRequestException`, rather than a status code.
- **The Service Layer audience**, see below.

In Blazor WebAssembly, the browser's own cookies can't be removed through headers, so token requests set the fetch option `credentials: "omit"`. That covers a Service Layer exposed on the same origin as the app, where a `B1SESSION` cookie in the browser would otherwise override the token.

Token requests don't follow redirects in a browser, not even within their origin. A browser hides a redirect's status code and `Location` from the code that asked not to follow it, so B1SLayer can't apply its same-origin rule, and the request fails with a `SLException` whose status code is 0.

## Access token audience

The Service Layer can reject a token over its audience, even when the token is otherwise valid. As of 10.0 FP 2602, SAP enables audience validation by default for Single Page Apps: the `aud` claim must include the Service Layer client ID, for example `b1-B1ServiceLayers-1222-main`, or the response is a 401.

This is configured on the server, as B1SLayer only sends the token it gets:

- As of FP 2608, choose the Service Layer client ID as an audience when registering the client in the Extension Single Sign-On Manager.
- Before FP 2608, add an audience mapper to the client scope in Keycloak.
- `EnableAudienceValidation=false` in `b1s.conf` turns the validation off, which SAP intends for testing and development.

If every request gets a 401 with a token that the SLD accepts, check the audience first. `SLCachingAccessTokenSource` limits the cost of this: it replaces a rejected token at most once every 30 seconds, see [401 responses](#401-responses).

## Tokens from other identity providers

With principal propagation, an administrator registers a partner's identity provider in the Extension Single Sign-On Manager and binds it to companies. The Service Layer then accepts that provider's JWT access tokens directly, with the same two headers, so they work through `AccessTokenSource` or `IdentityResolver`:

```csharp
var serviceLayer = new SLConnection(new SLConnectionOptions
{
    ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"), // or the external URL of a proxy that exposes it
    AccessTokenSource = SLAccessTokenSource.FromDelegate((request, cancellationToken) => myIdentityProvider.GetAccessTokenAsync(cancellationToken)),
    CompanyId = companyId
});
```

- `SLOidcClient` isn't involved, as your identity provider issues the tokens.
- `GetCompanyIdAsync` asks the SLD, so it may not be reachable or applicable from outside the landscape. SAP's Entra ID sample keeps the company IDs in its configuration instead.
- The token must be a JWT carrying the identity claim configured for the provider, with the user's email address, and any audience registered for it.

## One connection, many users

`IdentityResolver` runs once for each request, when it is sent, so the same request object and call site work for every user. A retry keeps the identity of its request, so it can't run as another account:

```csharp
var serviceLayer = new SLConnection(new SLConnectionOptions
{
    ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
    CompanyDB = "SBODEMOUS",
    UserName = "manager",
    Password = "12345",
    IdentityResolver = cancellationToken => Task.FromResult(ambientUser.Value switch
    {
        { } user => SLRequestIdentity.FromTokenSource(user.TokenSource, user.CompanyId),
        null when isBackgroundJob.Value => SLRequestIdentity.Session,
        null => throw new InvalidOperationException("The current user could not be determined.")
    })
});
```

- Keep one token source per user, as the source is what holds a token. Creating `SLRequestIdentity` on each call is cheap.
- Returning `SLRequestIdentity.Session` is an explicit decision to run as the session account. Don't read "no user" as that decision, since a lost ambient value would then silently run the request with the session account's permissions. Throw instead, as above. An exception from the resolver reaches the caller of the request.
- Returning `null` fails the request. It isn't read as the session identity.
- Session credentials are optional. Without them, returning the session identity fails the request.

## Behavior

### A request never carries both credentials

The Service Layer honors a session cookie over a bearer token, even an invalid one, so a request carrying both would silently run as the cookie's account. All sends go through one place, including on each redirect hop, which handles this as follows:

- A token request drops every `Cookie` header, whether set by the caller or a `BeforeCall` handler, overwrites `Authorization` and `X-b1-companyid`, and tells a browser to omit its cookies. It ignores the `ROUTEID` cookie the Service Layer sets on its responses, so there is no load balancer affinity. The inner requests of a token batch have those same headers dropped.
- On a connection using access tokens, a session request with a bearer token in its `Authorization` header, including an inner request of a batch, fails with `InvalidOperationException`, because dropping either credential would run the request differently than intended. Other schemes, like `Basic` for a proxy, are unaffected.
- A connection with no `AccessTokenSource` or `IdentityResolver` leaves its headers to you, so a bearer token for an API gateway in front of the Service Layer is sent along with the session cookie. The gateway must remove it before forwarding `POST /Login`, which the Service Layer would otherwise reject, and a token in `HttpClient.DefaultRequestHeaders` goes on that request too. Don't use a header there to select the Service Layer user, as the cookie wins: use `AccessTokenSource` or `IdentityResolver` for that.
- A `Cookie` header in `HttpClient.DefaultRequestHeaders` fails token requests in the same way, as it can't be removed per request and could follow a redirect to another origin.
- `SLConnection.HttpClient` is the transport, not a send path. Requests you send directly through it aren't covered.
- If you provide `SLConnectionOptions.HttpClient`, disable cookies on its handler, since B1SLayer can't inspect it. A connection with both session credentials and access tokens rejects a provided `HttpClient` with an `ArgumentException`, as a login through it could store the session's cookie for the token requests. A handler given through `HttpMessageHandler` has this done for you, unless it has already served requests, when it can no longer be changed. On a connection using access tokens, such a handler with cookies enabled is rejected with an `ArgumentException`.

### 401 responses

| | Session request | Token request |
|---|---|---|
| On a 401 | Logs in again and retries | Asks the token source for a fresh token and retries once |
| Limit | One extra attempt beyond `NumberOfAttempts` | One extra attempt beyond `NumberOfAttempts` |
| If 401 isn't in `HttpStatusCodesToRetry` | No retry | No retry |
| A second 401 | Follows `NumberOfAttempts` | Surfaced immediately, as a fresh token didn't help |
| The source returns the rejected token again | Not applicable | Surfaced without resending |
| Fallback to a session login | Not applicable | **Never**, even when the connection has session credentials |

Other statuses in `HttpStatusCodesToRetry`, like 502, retry as usual.

`SLCachingAccessTokenSource`, and so `SLOidcClient`'s sources, returns a token it acquired less than 30 seconds ago when that token is rejected, instead of replacing it. The 401 then surfaces without a token request, so a cause that a new token can't fix, such as a missing audience or a wrong company ID, doesn't cost a token request and a rotated refresh token per failed request. A token you passed in as `initialTokens` is always replaced, as its real age is unknown.

Only a 401 asks for a fresh token. The Service Layer answers 401 to a token with a bad signature, but 400 with code 306 to one it can't decode or whose signing key it doesn't know, and that surfaces as a `SLException` with no refresh, as a fresh token from the same source wouldn't normally help. This is what to expect from a token that isn't a JWT.

A failing token source surfaces right away as `SLAuthenticationException`, with the original exception as `InnerException` and no retry. Caller cancellations surface as `OperationCanceledException`.

`OnError` handlers are told about failures to get the identity or the access token, like an `invalid_grant`, with `RequestMessage` describing the request that wasn't sent. `BeforeCall` and `AfterCall` don't run for them, as nothing goes out. A background renewal that fails isn't reported, as requests keep working with the current token: the failure is only reported if the token expires without being renewed.

The request timeout also bounds resolving the identity and getting the token, each on its own, resulting in a `SLTimeoutException`. An acquisition by `SLCachingAccessTokenSource` carries on in the background, so its result isn't lost.

### Login and Logout

`LoginAsync` and `LogoutAsync` always concern the session account, and never carry a token.

- On a connection with session credentials, they work as usual and don't change which identity requests run with.
- On a token-only connection, they throw `InvalidOperationException`.
- Automatic login never fires for a token request.
- `PingAsync` sends no credentials at all.
- `SLConnectionOptions.Language` is a parameter of the login, so it has no effect on token requests.

### Redirects

A token request follows redirects only within its original origin (scheme, host and port), sending the token again. A handler given through `HttpMessageHandler` has its own automatic redirects disabled for this, or is rejected with an `ArgumentException` if it has already served requests with them enabled, and a provided `HttpClient` must have them disabled by you. Any other redirect isn't followed, and it surfaces as a `SLException` with the redirect's status code, so the token stays with the Service Layer. `SLOidcClient` doesn't follow redirects at all when it creates its own handler.

### Caching

`SLCachingAccessTokenSource` renews a token 60 seconds before it expires by default, capped at half its lifetime so a short-lived token isn't renewed on every request. Concurrent callers share one acquisition, and when several requests fail with the same token, only one refresh goes out.

An early renewal runs in the background while requests keep using the current token, so a slow or unresponsive server doesn't delay them. If it fails, it is tried again no sooner than 10 seconds later. Once the token has expired, or when the Service Layer rejected it, requests wait for a new one and the failure surfaces.

The acquisition isn't tied to any caller: a caller's cancellation only stops that caller from waiting. Otherwise, cancelling a refresh could lose a rotated refresh token that the server has already issued.

`SLOidcClient` caches the provider metadata for `ProviderMetadataLifetime`, 1 hour by default. If discovering it again fails with a transport error, a timeout or any HTTP failure, including the 4xx SAP documents for a failing SLD, the previous metadata keeps being used and discovery is tried again after 30 seconds, so an SLD restart neither stops token renewals nor makes each one wait for a timeout. Invalid metadata, like a mismatching issuer, is never accepted.

A failure of `SLOidcClient` called directly is a `SLAuthenticationException`, with a `HttpRequestException` as `InnerException` when the server couldn't be reached, or a `SLTimeoutException` after `RequestTimeout`.

## Transport security

Session requests keep accepting any certificate by default, as SAP Business One servers typically use self-signed ones. OAuth traffic validates by default (RFC 6749 section 10.9, RFC 9700):

| Traffic | Default | Options |
|---|---|---|
| Session requests | Not validated | `ValidateServerCertificate`, `ServerCertificateValidationCallback` |
| Requests with an access token | Validated | `ValidateAccessTokenServerCertificate`, `AccessTokenServerCertificateValidationCallback` |
| `SLOidcClient` (SLD, discovery, token endpoint) | Validated | `SLOidcClientOptions.ValidateServerCertificate`, `SLOidcClientOptions.ServerCertificateValidationCallback` |

The session options never affect token requests. Certificate validation only happens when a TLS connection is established, and connections are pooled per handler, so a single handler can't validate per request: a token request could reuse a connection a session request established without validation. A connection that has both session credentials and access tokens therefore has two handlers and two connection pools.

If you provide `HttpClient` or `HttpMessageHandler`, that instance serves both kinds of requests through one pool, and certificate validation is up to you. In that case, `AccessTokenServerCertificateValidationCallback` is rejected rather than silently ignored.

For self-signed landscapes, pinning is better than opting out:

```csharp
const string thumbprint = "..."; // of the certificate you have verified

var serviceLayer = new SLConnection(new SLConnectionOptions
{
    ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
    AccessTokenSource = tokenSource,
    CompanyId = companyId,
    AccessTokenServerCertificateValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == thumbprint
});
```

The SLD is the root of trust for discovery. Its answer decides where a client credential gets sent, and that decision is only as trustworthy as the connection to the SLD.

Access tokens require an HTTPS `ServiceLayerRoot` (RFC 6750 section 5.3), and `SLOidcClient` requires HTTPS for the SLD, the issuer and its endpoints. `AllowAccessTokensOverHttp` and `AllowInsecureHttp` are explicit opt-outs, meant for a local TLS-terminating proxy or for testing.

## Secrets

Tokens, secrets, private keys and client assertions don't appear in exceptions, in `ToString` or in the session cache.

- The `Authorization` header is only on a message while it is being sent, so the `BeforeCall`, `AfterCall` and `OnError` handlers can log `RequestMessage` safely. `SLCallContext.Identity` tells them which identity a call runs with.
- `SLAuthenticationException.ResponseContent` is always null, as a token response is never included.

## SAP documentation

From "Identity and Authentication Management in SAP Business One", 10.0 SP 2611, on the SAP Help Portal:

| Topic | Page | Page ID |
|---|---|---|
| Recommended approach | Scopes | `bc66c71a21eb42298fc1543a93adc79f` |
| Client registration | SAP Business One Extension Single Sign-On Manager | `88dcdfca3a11492da0acdbbbcc07c770` |
| | Registering a New Client | `b57bd7530eec4f20a55c978a4d497d56` |
| Overall flow | End-to-End Scenario Overview | `80cd25b24ba1439eb413231d94df4a32` |
| Daemon service | Walkthrough for Daemon Service | `daae6f2762a14b6180134713f956b157` |
| | Getting an Access Token | `e54f9ff1c90b49fb8c9adf3355233ba6` |
| | Getting a Company ID | `34bf0d0292094a039f9eae03c67e3225` |
| | Accessing the Service Layer | `850cec6c0ecc477f86a637632c5e5471` |
| Web app | OIDC Login | `1d3a6b13ceb34397b1e4ccd9cb65c9f2` |
| | Retrieving Company List | `8a2a029e41bb4f0bb100895c8602ffad` |
| | Refreshing a Token | `93f3e5cc25fb4d0a891456c612510e39` |
| | OIDC Logout | `a13a8ba80ed0414781342de8d69d046f` |
| | OIDC Back-Channel Logout | `c227a6082e8843b2a91c454d4cb1024a` |
| Single Page Apps, audience | Accessing the Service Layer (Single Page Apps) | `50822ec775304dd5b62c222cae9ca24a` |
| | Configuring Audience in SAP Business One Extension Single Sign-On Manager | `be738c5cabc1419eba572c71f7f69fce` |
| SLD APIs | GetOpenIDConnectProvider | `8318718d35a748e8ab4ca4bde52f2dec` |
| | CurrentUserInfo | `17f4ec228af241f5bc0e4ed1a96603a6` |
| Token lifetime | Configuring the Lifetime of the Access Tokens in Keycloak | `0de0a124d0934ba4b03b92a7160ea921` |

From "How to Develop Web Client Extensions", for tile extensions and URL Mashups:

| Page | Page ID |
|---|---|
| Interacting with Service Layer (Blazor) | `b65f84dc51044a3f8bbc6789a2c33c5c` |
| Interacting with Service Layer (React) | `11b5d688c0c4417786228e50728b5a52` |
| Single Sign-On with Web Client and Extensions | `77046260cf224bacbe24a816d5b1235e` |
| Creating a URL Mashup App | `28461b436583429b9d17c2db43567323` |

From the separate guide "Principal Propagation for SAP Business One", for tokens from other identity providers:

| Page | Page ID |
|---|---|
| Working with Principal Propagation in SAP Business One Extension SSO Manager | `e86e084556d34da880d8a0bc4e87786f` |
| Exposing Service Layer Through Proxy Applications | `f87c4438bca9462fab44f187b4021fdd` |

SAP says `CurrentUserInfo` also returns the user's roles, without specifying their format, so `SLCurrentUserInfo` doesn't model them. `SLCurrentUserInfo.RawJson` has the object as the SLD returned it.

### Where B1SLayer deliberately differs from SAP's samples

| SAP's sample | B1SLayer | Why |
|---|---|---|
| Client assertion valid for ±120 minutes | 5 minutes, with its start 60 seconds in the past | RFC 7523 recommends short-lived assertions |
| Certificate validation disabled (`rejectUnauthorized: false`) | Validates by default | RFC 6749 section 10.9, RFC 9700 |
| No check of the discovery document | Its `issuer` must match the discovery URI, allowing only a terminating slash | OpenID Connect Discovery section 4.3. If the SLD and the provider report different forms of the same host, the token requests fail until that is aligned |
| Uses the first company of the list | Selects by schema name and fails if ambiguous | Avoids silently using the wrong company |
| Client ID must be `b1-ext-<guid>` | Not validated | Real landscapes also issue `b1-ext-daemon-<guid>` and `b1-ext-web-<guid>` |

SAP's documentation doesn't describe a request that carries both a session cookie and a bearer token. The behavior described under [A request never carries both credentials](#a-request-never-carries-both-credentials) was observed on Service Layer version 1000340: with a valid cookie, a request ran as the cookie's user with an invalid bearer token present. The same goes for the responses to a token at `POST /Login` and to a token the Service Layer can't decode, described above, which are observed behavior and not documented by SAP.

One point follows SAP rather than the RFC: a client secret is sent in the request body, as in SAP's documentation, although RFC 6749 section 2.3.1 prefers HTTP Basic.

## Running the live tests

`SLLiveOAuthTests` runs against a real landscape with a registered daemon client, and only reads from SAP. It is skipped unless these environment variables are set:

| Variable | Value |
|---|---|
| `B1SLAYER_LIVE_SLD` | The SLD address, for example `https://sapserver:40000` |
| `B1SLAYER_LIVE_SERVICE_LAYER` | The Service Layer root, for example `https://sapserver:50000/b1s/v2` |
| `B1SLAYER_LIVE_CLIENT_ID` | The client ID |
| `B1SLAYER_LIVE_PRIVATE_KEY` | The base64 PKCS#8 private key |
| `B1SLAYER_LIVE_COMPANY_SCHEMA` | The company schema name |
| `B1SLAYER_LIVE_INSECURE_TLS` | `1` for a landscape with self-signed certificates |
