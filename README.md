# B1SLayer

[![B1SLayer](https://img.shields.io/nuget/v/B1SLayer.svg)](https://www.nuget.org/packages/B1SLayer/)
[![Downloads](https://img.shields.io/nuget/dt/B1SLayer.svg)](https://www.nuget.org/packages/B1SLayer/)

A lightweight SAP Business One Service Layer client for .NET

B1SLayer aims to provide:

- Fluent and easy Service Layer requests
- Automatic session management
- Automatic retry of failed requests

## How to use it

Firstly I highly recommend reading [my blog post on SAP Community](https://community.sap.com/t5/enterprise-resource-planning-blogs-by-members/b1slayer-a-clean-and-easy-way-to-consume-sap-business-one-service-layer/ba-p/13526121) where I go
into more details, but here's a couple examples of what's possible (but not limited to) with B1SLayer:

````c#
/* The connection object. All Service Layer requests and the session management are handled by this object
 * and therefore only one instance per company/user should be used across the entire application.
 * If you want to connect to multiple databases or use different users, you will need multiple instances.
 * There's no need to manually Login! The session is managed automatically and renewed whenever necessary.
 */
var serviceLayer = new SLConnection("https://sapserver:50000/b1s/v1", "CompanyDB", "manager", "12345");

// Performs a GET on /Orders(823) and deserializes the result in a custom model class
var order = await serviceLayer.Request("Orders", 823).GetAsync<MyOrderModel>();

// Performs a GET on /BusinessPartners with query string and header parameters supported by Service Layer
// The result is deserialized in a List of a custom model class
var bpList = await serviceLayer.Request("BusinessPartners")
    .Filter("startswith(CardCode, 'c')")
    .Select("CardCode, CardName")
    .OrderBy("CardName")
    .WithPageSize(50)
    .WithCaseInsensitive()
    .GetAsync<List<MyBusinessPartnerModel>>();

// Performs a GET on /AlternateCatNum specifying the record through a composite primary key
var altCatNum = await serviceLayer
    .Request("AlternateCatNum(ItemCode='A00001',CardCode='C00001',Substitute='BP01')")
    .GetAsync<MyAltCatModel>();

// Performs multiple GET requests on /Items until all entities in the database are obtained
// The result is an IList of your custom model class (unwrapped from the 'value' array)
var allItemsList = await serviceLayer.Request("Items").GetAllAsync<MyItemModel>();

// Performs a POST on /Orders with the provided object as the JSON body, 
// creating a new order and deserializing the created order in a custom model class
var createdOrder = await serviceLayer.Request("Orders").PostAsync<MyOrderModel>(myNewOrderObject);

// Performs a PATCH on /BusinessPartners('C00001'), updating the CardName of the Business Partner
await serviceLayer.Request("BusinessPartners", "C00001")
    .PatchAsync(new { CardName = "Updated BP name" });

// Performs a PATCH on /ItemImages('A00001'), adding or updating the item image
await serviceLayer.Request("ItemImages", "A00001")
    .PatchWithFileAsync(@"C:\ItemImages\A00001.jpg");

// Performs a POST on /Attachments2 with the provided file as the attachment
var attachmentEntry = await serviceLayer.PostAttachmentAsync(@"C:\files\myfile.pdf");

// Batch requests! Performs multiple operations in SAP in a single HTTP request
var req1 = new SLBatchRequest(
    HttpMethod.Post, // HTTP method
    "BusinessPartners", // resource
    new { CardCode = "C00001", CardName = "I'm a new BP" }) // object to be sent as the JSON body
    .WithReturnNoContent(); // Adds the header "Prefer: return-no-content" to the request

var req2 = new SLBatchRequest(HttpMethod.Patch,
    "BusinessPartners('C00001')",
    new { CardName = "This is my updated name" });

var req3 = new SLBatchRequest(HttpMethod.Delete, "BusinessPartners('C00001')");

HttpResponseMessage[] batchResult = await serviceLayer.PostBatchAsync(req1, req2, req3);

// Performs a POST on /Logout, ending the current session
await serviceLayer.LogoutAsync();
````

## OAuth / OpenID Connect

Besides username and password, B1SLayer can authenticate with access tokens from the SAP Business One authentication
server (10.0 FP 2208 and later). Tokens can't be exchanged for a session, so every request carries the token and its
company ID. Sessions keep working exactly as above.
The [full guide](https://github.com/bgmulinari/B1SLayer/blob/3.X.X/OAUTH.md) covers the behavior in detail and
references SAP's documentation.

**Daemon service (client credentials).** Available as of 10.0 FP 2502. Register a Daemon Service in the Extension Single
Sign-On Manager and let B1SLayer handle the rest:

````c#
// Keep it for as long as the token source is used, as the source requests its tokens through it
var oidc = new SLOidcClient(new SLOidcClientOptions
{
    SldAddress = new Uri("https://sapserver:40000"),
    ClientId = config.ClientId,
    ClientCredential = SLClientCredential.FromPrivateKey(config.PrivateKey) // or FromClientSecret(...)
});

// Cached, renewed before it expires, and shared by concurrent requests
var tokenSource = oidc.CreateClientCredentialsTokenSource();

// The company is selected by its SLD company ID, which isn't the database name
var companyId = await oidc.GetCompanyIdAsync(await tokenSource.GetAccessTokenAsync(SLAccessTokenRequest.Default), "SBODEMOUS");

var serviceLayer = new SLConnection(new SLConnectionOptions
{
    ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
    AccessTokenSource = tokenSource,
    CompanyId = companyId
});

var items = await serviceLayer.Request("Items").Select("ItemCode").Top(2).GetAsync<List<MyItemModel>>();
````

**Web apps (authorization code).** Signing users in stays with your OIDC library, as SAP recommends, and B1SLayer takes
the tokens it produces. One connection serves all users, and the identity is decided at send time:

````c#
var serviceLayer = new SLConnection(new SLConnectionOptions
{
    ServiceLayerRoot = new Uri("https://sapserver:50000/b1s/v2"),
    IdentityResolver = cancellationToken => Task.FromResult(
        ambientUser.Value is { } user
            ? SLRequestIdentity.FromTokenSource(user.TokenSource, user.CompanyId)
            : throw new InvalidOperationException("The current user could not be determined."))
});
````

A request never carries both a session cookie and a token, and a token request never falls back to the session account.
A connection can also have session credentials, for example for background jobs, in which case a resolver can return
`SLRequestIdentity.Session` for them.

If the Service Layer answers 401 to a token the SLD accepts, the token's audience probably lacks the Service Layer
client ID, which SAP requires for Single Page Apps as of FP 2602. The guide covers this, along with the CORS
configuration a browser client needs and tokens from other identity providers.

**Web Client tile extensions.** `UseAmbientSession = true` lets a Blazor WebAssembly tile hosted by the SAP Business One
Web Client inherit the signed-in user, with no login or token. However, the Content Security Policy of the Web Client
10.0 FP 2608 doesn't allow WebAssembly, so such a tile doesn't start there. See
the [guide](https://github.com/bgmulinari/B1SLayer/blob/3.X.X/OAUTH.md#web-client-tile-extensions).

**Certificates are validated for OAuth traffic by default**, unlike session requests. For the self-signed certificates
of a typical landscape, pin them through `AccessTokenServerCertificateValidationCallback` and
`SLOidcClientOptions.ServerCertificateValidationCallback`, or opt out with
`ValidateAccessTokenServerCertificate = false` and `SLOidcClientOptions.ValidateServerCertificate = false`.

## Get it on NuGet

`PM> Install-Package B1SLayer`

`dotnet add package B1SLayer`
