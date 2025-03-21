﻿using Flurl.Http;
using Flurl.Http.Content;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

/// <summary>
/// Provides helper methods for handling multipart HTTP responses and creating HTTP content.
/// </summary>
internal static class MultipartHelper
{
    /// <summary>
    /// Reads a multipart HTTP response and parses it into an array of <see cref="HttpResponseMessage"/> objects.
    /// </summary>
    /// <param name="response">The HTTP response containing the multipart content.</param>
    /// <returns>An array of <see cref="HttpResponseMessage"/> objects representing the individual parts of the multipart response.</returns>
    public static async Task<HttpResponseMessage[]> ReadMultipartResponseAsync(HttpResponseMessage response)
    {
        var innerResponses = new List<HttpResponseMessage>();
        var content = await response.Content.ReadAsStringAsync();
        var parts = content.Split(["HTTP/"], StringSplitOptions.RemoveEmptyEntries).Skip(1);

        foreach (var part in parts)
        {
            var requestData = part.Split(["\n\r\n"], StringSplitOptions.RemoveEmptyEntries);
            requestData = requestData.Where(x => !x.StartsWith("--") && !x.StartsWith("\r\n")).ToArray();
            var headers = requestData[0].Split(["\r\n"], StringSplitOptions.RemoveEmptyEntries).Skip(1);
            var httpResponse = new HttpResponseMessage();
            var statusLine = requestData[0].Split([" "], StringSplitOptions.RemoveEmptyEntries);

            if (statusLine.Length > 1)
            {
                httpResponse.Version = new Version(statusLine[0]);
                httpResponse.StatusCode = (HttpStatusCode)int.Parse(statusLine[1]);
            }

            if (requestData.Length > 1)
            {
                httpResponse.Content = new StringContent(requestData[1]);
                httpResponse.Content.Headers.Remove("Content-Type");
            }

            httpResponse.Content ??= new ByteArrayContent([]);

            foreach (var header in headers)
            {
                var headerParts = header.Split([": "], StringSplitOptions.RemoveEmptyEntries);

                if (headerParts.Length == 2)
                {
                    var headerName = headerParts[0].Trim('\r', '\n');
                    var headerValue = headerParts[1].Trim('\r', '\n');

                    if (headerName.StartsWith("Content-"))
                        httpResponse.Content.Headers.TryAddWithoutValidation(headerName, headerValue);
                    else
                        httpResponse.Headers.TryAddWithoutValidation(headerName, headerValue);
                }
            }

            innerResponses.Add(httpResponse);
        }

        return innerResponses.ToArray();
    }

    /// <summary>
    /// Creates an HTTP content from the provided HTTP request message.
    /// </summary>
    /// <param name="request">The HTTP request message.</param>
    /// <returns>A task representing the asynchronous operation, containing the created HTTP content.</returns>
    internal static async Task<HttpContent> CreateHttpContentAsync(HttpRequestMessage request)
    {
        var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream, new UTF8Encoding(false), 1024, leaveOpen: true);
        writer.WriteLine($"{request.Method} {request.RequestUri.PathAndQuery} HTTP/{request.Version}");
        writer.WriteLine($"Host: {request.RequestUri.Host}:{request.RequestUri.Port}");

        foreach (var header in request.Headers)
        {
            writer.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
        }

        if (request.Content != null)
        {
            foreach (var header in request.Content.Headers)
            {
                writer.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
            }
            writer.WriteLine();
            writer.Flush();
            memoryStream.Position = memoryStream.Length;
            await request.Content.CopyToAsync(memoryStream);
        }
        else
        {
            writer.WriteLine();
            writer.Flush();
        }

        memoryStream.Position = 0;
        var streamContent = new StreamContent(memoryStream);
        streamContent.Headers.Add("Content-Type", "application/http; msgtype=request");
        return streamContent;
    }

    /// <summary>
    /// Flurl extension method to provide a PATCH method for multipart requests.
    /// </summary>
    internal static Task<IFlurlResponse> PatchMultipartAsync(this IFlurlRequest request, Action<CapturedMultipartContent> buildContent, HttpCompletionOption httpCompletionOption = default, CancellationToken cancellationToken = default)
    {
        var cmc = new CapturedMultipartContent(request.Settings);
        buildContent(cmc);
        return request.SendAsync(new HttpMethod("PATCH"), cmc, httpCompletionOption, cancellationToken);
    }
}