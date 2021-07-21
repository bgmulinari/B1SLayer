# B1SLayer

[![B1SLayer](https://img.shields.io/nuget/v/B1SLayer.svg?maxAge=3600&label=B1SLayer)](https://www.nuget.org/packages/B1SLayer/)

A lightweight SAP Business One Service Layer client for .NET

B1SLayer aims to provide:
- Fluent and easy Service Layer requests
- Automatic session management
- Automatic retry of failed requests

## How to use it

Bellow a couple examples of what's possible (but not limited to) with B1SLayer:

````c#
/* The connection object. All requests and the session managament are handled by this object and therefore 
 * only one instance per company/user should be used, initialized only once at the application startup.
 * Ideally this would be a static object to be used across the entire application.
 * There's no need to manually Login! The session is managed automatically and renewed whenever necessary.
 */
var serviceLayer = new SLConnection("https://sapserver:50000/b1s/v1", "CompanyDB", "manager", "12345");

// Request monitoring/logging available through the methods BeforeCall, AfterCall and OnError.
// The FlurlCall object provides various details about the request and the response.
serviceLayer.AfterCall(async call =>
{
    Console.WriteLine($"Request: {call.HttpRequestMessage.Method} {call.HttpRequestMessage.RequestUri}");
    Console.WriteLine($"body sent: {call.RequestBody}");
    Console.WriteLine($"Response: {call.HttpResponseMessage?.StatusCode}");
    Console.WriteLine(await call.HttpResponseMessage?.Content?.ReadAsStringAsync());
    Console.WriteLine($"Call duration: {call.Duration.Value.TotalSeconds} seconds");
});

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

// Performs a POST on /Orders with the provided object as the JSON body, 
// creating a new order and deserializing the created order in a custom model class
var createdOrder = await serviceLayer.Request("Orders").PostAsync<MyOrderModel>(myNewOrderObject);

// Performs a PATCH on /BusinessPartners('C00001'), updating the CardName of the Business Partner
await serviceLayer.Request("BusinessPartners", "C00001").PatchAsync(new { CardName = "Updated BP name" });

// Performs a PATCH on /ItemImages('A00001'), adding or updating the item image
await serviceLayer.Request("ItemImages", "A00001").PatchWithFileAsync(@"C:\ItemImages\A00001.jpg");

// Performs a POST on /Attachments2 with the provided file as the attachment (other overloads available)
var attachmentEntry = await serviceLayer.PostAttachmentAsync(@"C:\files\myfile.pdf");

// Batch requests! Performs multiple operations in SAP in a single HTTP request
var batchRequests = new SLBatchRequest[]
{
    new SLBatchRequest(HttpMethod.Post, // HTTP method
        "BusinessPartners", // resource
        new { CardCode = "C00001", CardName = "I'm a new BP" } // object to be sent as the JSON body
    ),
    new SLBatchRequest(HttpMethod.Patch, 
        "BusinessPartners('C00001')", 
        new { CardName = "This is my updated name" }
    ),  
    new SLBatchRequest(HttpMethod.Delete, 
        "BusinessPartners('C00001')"
    )
};

HttpResponseMessage[] batchResult = await serviceLayer.PostBatchAsync(batchRequests);

// Performs a POST on /Logout, ending the current session
await serviceLayer.LogoutAsync();
````

## Get it on NuGet

`PM> Install-Package B1SLayer`

`dotnet add package B1SLayer`

#### Special thanks

B1Slayer is based and depends on the awesome [Flurl](https://github.com/tmenier/Flurl) library, which I highly recommend checking out. Thanks, Todd!
