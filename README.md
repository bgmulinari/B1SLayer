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
// The connection object. Only one instance per company/user should be used in the application
// The Service Layer session is managed automatically and renewed whenever necessary
var serviceLayer = new SLConnection("https://sapserver:50000/b1s/v1", "COMPANYDB", "manager", "12345");

// Performs a GET on /Orders(823) and deserializes the result in a custom model class
var order = await serviceLayer.Request("Orders", 823).GetAsync<MyOrderModel>();

// Performs GET on /BusinessPartners with query string and header parameters supported by Service Layer
// The result is deserialized in a List of a custom model class
var bpList = await serviceLayer.Request("BusinessPartners")
    .Filter("startswith(CardCode, 'c')")
    .Select("CardCode, CardName")
    .OrderBy("CardName")
    .WithPageSize(50)
    .WithCaseInsensitive()
    .GetAsync<List<MyBusinessPartnerModel>>();

// Performs a POST on /Orders with the provided object as the JSON body, 
// creating a new order and deserializing the result in a custom model class
var newOrder = await serviceLayer.Request("Orders").PostAsync<MyOrderModel>(myNewOrderObject);

// Performs PATCH on /BusinessPartners('C00001'), updating the CardName of the Business Partner
await serviceLayer.Request("BusinessPartners", "C00001").PatchAsync(new { CardName = "Updated BP name" });

// Performs a POST on /Attachments2 with the provided file as the attachment
var newAttachment = await serviceLayer.PostAttachmentAsync(@"C:\files\myfile.pdf");

// Batch requests! Performs multiple operations in SAP in a single HTTP request
var batchRequests = new BatchRequest[]
{
    new BatchRequest(HttpMethod.Post, // HTTP method
        "BusinessPartners", // resource
        new { CardCode = "C00001", CardName = "I'm a new BP" } // object to be sent as the JSON body
    ),
    new BatchRequest(HttpMethod.Patch, 
        "BusinessPartners('C00001')", 
        new { CardName = "This is my updated name" }
    ),  
    new BatchRequest(HttpMethod.Delete, 
        "BusinessPartners('C00001')"
    )
};

var batchResult = await serviceLayer.PostBatchAsync(batchRequests);
````

## Get it on NuGet:

`PM> Install-Package B1SLayer`

#### Special thanks

B1Slayer is based and depends on the awesome [Flurl](https://github.com/tmenier/Flurl) library, which I highly recommend checking out. Thanks, Todd!
