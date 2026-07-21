---
title: "B1SLayer: A clean and easy way to consume SAP Business One Service Layer with .NET"
description: "Complete guide to B1SLayer, a lightweight SAP Business One Service Layer client for .NET with automatic session management and fluent API"
author: "Bruno Mulinari"
date: 2022-05-23
source: "https://community.sap.com/t5/enterprise-resource-planning-blog-posts-by-members/b1slayer-a-clean-and-easy-way-to-consume-sap-business-one-service-layer/ba-p/13526121"
tags:
  - SAP
  - Business One
  - Service Layer
  - .NET
  - API Client
---
## Introduction

If you, as a developer, ever worked in multiple.NET projects that consume the Service Layer, you probably faced the issue where different projects may have different implementations on how they communicate with Service Layer. Often times these implementations are less than ideal and can lead to a number of issues and headaches that could be avoided.  
  
Also, although RESTful APIs have concepts that are fairly easy to grasp, Service Layer has some particularities that can make it difficult to integrate with, specially for new developers. There is also the option to use the WCF/OData client, but this approach can be cumbersome, which is probably why most developers I know have chosen to write their own solutions instead.  
  
B1SLayer aims to solve all that by abstracting Service Layer's complexity and taking care of various things under the hood automatically, saving precious development time and resulting in a cleaner and easier to understand code. B1SLayer seamlessly takes care of the authentication and session management, provides an easy way to perform complex requests with its fluent API, automatically retries failed requests and much more.

## Getting started

The first thing to do is install it to your project. B1SLayer is available on **[NuGet](https://www.nuget.org/packages/B1SLayer/)** and can be easily installed through the [NuGet Package Manager in Visual Studio](https://docs.microsoft.com/en-us/nuget/consume-packages/install-use-packages-visual-studio) or the [.NET CLI](https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-add-package). The library is based on the.NET Standard 2.0 specification and therefore it is compatible with [various.NET implementations and versions](https://docs.microsoft.com/en-us/dotnet/standard/net-standard?tabs=net-standard-2-0#select-net-standard-version).  
  
Once installed, you start by creating your *SLConnection* instance, providing it the required information for it to be able to connect to the API, that is, the Service Layer URL, database name, username and password.  
  
The *SLConnection* instance is the most important object when using B1SLayer. All requests originate from it and it's where the session is managed and renewed automatically. Therefore, once initialized, this instance needs to be kept alive to be reused throughout your application's lifecycle. A common and simple way to achieve this is implementing a singleton pattern:

```csharp
using B1SLayer;

public sealed class ServiceLayer
{
    private static readonly SLConnection _serviceLayer = new SLConnection(
        "https://localhost:50000/b1s/v1/",
        "SBO_COMPANYDBNAME",
        "username",
        "password");
    static ServiceLayer() { }
    private ServiceLayer() { }
    public static SLConnection Connection => _serviceLayer;
}
```

If your project supports the [dependency injection (DI)](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection) design pattern, it's even simpler. You can add the *SLConnection* instance as a singleton service and later request it where you need it:

```csharp
builder.Services.AddSingleton(serviceProvider => new SLConnection(
    "https://localhost:50000/b1s/v1/",
    "SBO_COMPANYDBNAME",
    "username",
    "password"));
```

For the following content in this post, I'm going to assume you have your *SLConnection* instance named "serviceLayer".

## Performing requests

As mentioned earlier, B1SLayer manages Service Layer's authentication and session for you. This means you don't need to perform a login request as it is done automatically whenever necessary, although you can still login manually if you want to.  
  
Another thing to keep in mind, is that B1SLayer does not include model classes for Service Layer's entities. This means you will need to create model classes like *BusinessPartner* or *PurchaseOrder* yourself.  
  
Most B1SLayer requests can be divided into three parts: creation, configuration and execution, all done chaining calls in a fluent manner.

- **Creation** is where you call the *Request* method from your *SLConnection* instance to specify which Service Layer resource you wish to request to, for instance: "BusinessPartners";
- **Configuration** is optional, it's where you can specify parameters for the request, like query string, headers, etc. For instance: "$filter", "$select", "B1S-PageSize";
- **Execution** is where the the HTTP request is actually performed (GET, POST, PATCH, DELETE). It's also where you can specify the return type or the JSON body, optionally.

In the example below, a GET request is created to the "PurchaseOrders" resource for a specific document with DocEntry number 155. The request is then configured to select only a couple fields of this entity and lastly the request is executed, deserializing the JSON result into a *MyPurchaseOrder* type object named *purchaseOrder*.

```csharp
// Resulting HTTP request:
// GET /PurchaseOrders(155)?$select=DocEntry,CardCode,DocTotal
MyPurchaseOrderModel purchaseOrder = await serviceLayer // SLConnection object
    .Request("PurchaseOrders", 155) // Creation
    .Select("DocEntry,CardCode,DocTotal") // Configuration
    .GetAsync<MyPurchaseOrderModel>(); // Execution
```

Pretty straight forward, right? How about a more complex request? In the example below, a GET request is created to the "BusinessPartners" resource, then configured with *Select*, *Filter* and *OrderBy* query string parameters, then *WithPageSize* which adds the "B1S-PageSize" header parameter to the request to specify the number of entities to be returned per request, overwriting the default value of 20. Lastly, the request is performed and the JSON result is deserialized into a *List* of *MyBusinessPartnerModel* type named *bpList*.

```csharp
// Resulting HTTP request:
// GET /BusinessPartners?$select=CardCode,CardName,CreateDate&$filter=CreateDate gt '2010-05-01'&$orderby=CardName
// Headers: B1S-PageSize=100
List<MyBusinessPartnerModel> bpList = await serviceLayer
    .Request("BusinessPartners")
    .Select("CardCode,CardName,CreateDate")
    .Filter("CreateDate gt '2010-05-01'")
    .OrderBy("CardName")
    .WithPageSize(100)
    .GetAsync<List<MyBusinessPartnerModel>>();
```

A POST request is even simpler, as you can see below. The "Orders" resource is requested and a new order document is created with the object *newOrderToBeCreated* serialized as the JSON body. If the entity is created successfully, by default Service Layer returns the created entity as the response, this response then is deserialized into a new a *MyOrderModel* type object named *createdOrder*.

```csharp
// Your object to be serialized as the JSON body for the request
MyOrderModel newOrderToBeCreated = new MyOrderModel { ... };

// Resulting HTTP request:
// POST /Orders
// Body: newOrderToBeCreated serialized as JSON
MyOrderModel createdOrder = await serviceLayer
    .Request("Orders")
    .PostAsync<MyOrderModel>(newOrderToBeCreated);
```

What about PATCH and DELETE requests? Here I'm using an anonymous type object that holds the properties that I want to update in my business partner entity. PATCH and DELETE requests don't return anything, so there is nothing to deserialize.

```csharp
// Your object to be serialized as the JSON body for the request
var updatedBpInfo = new { CardName = "SAP SE", MailAddress = "sap@sap.com" };

// Resulting HTTP request:
// PATCH /BusinessPartners('C00001')
// Body: updatedBpInfo serialized as JSON
await serviceLayer.Request("BusinessPartners", "C00001").PatchAsync(updatedBpInfo);

// Resulting HTTP request:
// DELETE /BusinessPartners('C00001')
await serviceLayer.Request("BusinessPartners", "C00001").DeleteAsync();
```

## Specialized requests

Although the majority of requests to Service Layer will follow the format I presented above, there are some exceptions that needed a special implementation, these are called directly from your *SLConnection* object. Let's get into them.

### Login and logout

Just in case you want to handle the session manually, here's how to do it:

```csharp
// Performs a POST on /Login with the information provided in your SLConnection object
await serviceLayer.LoginAsync();

// Performs a POST on /Logout, ending the current session
await serviceLayer.LogoutAsync();
```

### Attachments

Uploading and downloading attachments is very streamlined with B1SLayer. Have a look:

```csharp
// Performs a POST on /Attachments2, uploading the provided file and returning the
// attachment details in a SLAttachment type object
SLAttachment attachment = await serviceLayer.PostAttachmentAsync(@"C:\temp\myFile.pdf");

// Performs a GET on /Attachments2({attachmentEntry}) with the provided attachment entry,
// downloading the file as a byte array
byte[] myFile = await serviceLayer.GetAttachmentAsBytesAsync(953);
```

Keep in mind that to be able to upload attachments through Service Layer, first some configurations on the B1 client and server are required. Check out the section ["Setting up an Attachment Folder" in the Service Layer user manual](https://help.sap.com/doc/6ab840ef2e8140f3af9eeb7d8fef9f06/10.0/en-US/Working_with_SAP_Business_One_Service_Layer.pdf#page=112) for more details.

### Ping Pong

This feature was added in version 9.3 PL10, providing a direct response from the Apache server that can be used for testing and monitoring. The result is a *SLPingResponse* type object containing the "pong" response and some other details. Check section ["Ping Pong API" in the Service Layer user manual](https://help.sap.com/doc/6ab840ef2e8140f3af9eeb7d8fef9f06/10.0/en-US/Working_with_SAP_Business_One_Service_Layer.pdf#page=153) for more details.

```csharp
// Pinging the load balancer
SLPingResponse loadBalancerResponse = await serviceLayer.PingAsync();

// Pinging a specific node
SLPingResponse nodeResponse = await serviceLayer.PingNodeAsync(2);
```

### Batch requests

Although a powerful and useful feature, batch requests can be quite complicated to implement, but thankfully this is also very simple to do with B1SLayer. If you are not familiar with the concept, I recommend reading section ["Batch Operations" in the user manual](https://help.sap.com/doc/6ab840ef2e8140f3af9eeb7d8fef9f06/10.0/en-US/Working_with_SAP_Business_One_Service_Layer.pdf#page=82). In essence, it's a way to perform multiple operations in Service Layer with a single HTTP request, with a rollback capability if something fails.  
  
Here each individual request you wish to send in a batch is represented as an *SLBatchRequest* object, where you specify the HTTP method, resource and optionally the body. Once you have all requests created, you send them through the method *PostBatchAsync*. The result is an *HttpResponseMessage* array containing the responses of each request you sent.

```csharp
var postRequest = new SLBatchRequest(
    HttpMethod.Post, // HTTP method
    "BusinessPartners", // resource
    new { CardCode = "C00001", CardName = "I'm a new BP" }); // request body

var patchRequest = new SLBatchRequest(
    HttpMethod.Patch,
    "BusinessPartners('C00001')",
    new { CardName = "This is my updated name" });

var deleteRequest = new SLBatchRequest(HttpMethod.Delete, "BusinessPartners('C00001')");

// Here I'm passing each request individually, but you can also
// add them to a collection and pass it instead.
HttpResponseMessage[] batchResult = await serviceLayer
    .PostBatchAsync(postRequest, patchRequest, deleteRequest);
```

## Conclusion

This is my first post here and it ended up longer than I expected, and even still, I couldn't possibly fit every feature of B1SLayer here, otherwise this post would be even longer. Nevertheless, I hope it gives you a good overall understanding of how it works and what it offers. I'll do my best to keep this post updated and up to the community standards, so any feedback is appreciated!  
  
The source code for B1SLayer is available on [**GitHub**](https://github.com/bgmulinari/B1SLayer). If you want to contribute, have any suggestions, doubts or encountered any issue, please, feel free to [open an issue](https://github.com/bgmulinari/B1SLayer/issues) there or leave a comment below. I will try to reply as soon as possible.
