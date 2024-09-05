# B1SLayer

[![B1SLayer](https://img.shields.io/nuget/v/B1SLayer.svg)](https://www.nuget.org/packages/B1SLayer/)
[![Downloads](https://img.shields.io/nuget/dt/B1SLayer.svg)](https://www.nuget.org/packages/B1SLayer/)

A lightweight SAP Business One Service Layer client for .NET

B1SLayer aims to provide:
- Fluent and easy Service Layer requests
- Automatic session management
- Automatic retry of failed requests

## How to use it

Firstly I highly recommend reading [my blog post on SAP Community](https://community.sap.com/t5/enterprise-resource-planning-blogs-by-members/b1slayer-a-clean-and-easy-way-to-consume-sap-business-one-service-layer/ba-p/13526121) where I go into more details, but here's a couple examples of what's possible (but not limited to) with B1SLayer:

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

## Get it on NuGet

`PM> Install-Package B1SLayer`

`dotnet add package B1SLayer`

#### Special thanks

B1Slayer is based and depends on the awesome [Flurl](https://github.com/tmenier/Flurl) library, which I highly recommend checking out. Thanks, Todd!
