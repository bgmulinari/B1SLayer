using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace B1SLayer;

internal static class MultipartHelper
{
    internal static async Task<List<HttpResponseMessage>> ParseMultipartResponseAsync(HttpResponseMessage response)
    {
        var responseMessages = new List<HttpResponseMessage>();

        var boundary = GetBoundary(response.Content.Headers.ContentType);
        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(contentStream);

        await ProcessMultipartAsync(reader, boundary, responseMessages);

        return responseMessages;
    }

    private static string GetBoundary(MediaTypeHeaderValue contentType)
    {
        var boundary = contentType.Parameters.SingleOrDefault(p => p.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(boundary))
        {
            throw new InvalidOperationException("Missing boundary in multipart content");
        }
        return boundary.Trim('"');
    }

    private static async Task ProcessMultipartAsync(TextReader reader, string boundary, List<HttpResponseMessage> responseMessages)
    {
        var partContent = new StringBuilder();
        var isPartReading = false;

        await foreach (var line in ReadLinesAsync(reader))
        {
            if (line.StartsWith("--" + boundary))
            {
                if (isPartReading)
                {
                    await ProcessPartAsync(new StringReader(partContent.ToString()), responseMessages);
                    partContent.Clear();
                }
                isPartReading = true;
            }
            else if (isPartReading)
            {
                partContent.AppendLine(line);
            }
        }

        if (isPartReading && partContent.Length > 0)
        {
            await ProcessPartAsync(new StringReader(partContent.ToString()), responseMessages);
        }
    }

    private static async Task ProcessPartAsync(TextReader reader, List<HttpResponseMessage> responseMessages)
    {
        var headers = new List<string>();
        bool isFirstLine = true;
        bool isHttpResponse = false;
        string boundary = null;
        HttpResponseMessage response = null;

        await foreach (var line in ReadLinesAsync(reader))
        {
            if (isFirstLine)
            {
                if (line.StartsWith("HTTP"))
                {
                    isHttpResponse = true;
                    response = new HttpResponseMessage
                    {
                        StatusCode = (HttpStatusCode)int.Parse(line.Split(' ')[1])
                    };
                    isFirstLine = false;
                    continue;
                }
                else if (line.StartsWith("Content-Type: multipart/mixed"))
                {
                    boundary = line.Split(';')
                                   .SingleOrDefault(p => p.Trim().StartsWith("boundary="))?
                                   .Split('=')[1]?.Trim('"');
                    if (!string.IsNullOrEmpty(boundary))
                    {
                        await ProcessMultipartAsync(reader, boundary, responseMessages);
                        return;
                    }
                }
            }

            if (isHttpResponse)
            {
                if (string.IsNullOrEmpty(line))
                {
                    break;
                }
                headers.Add(line);
            }
        }

        if (isHttpResponse && response != null)
        {
            foreach (var header in headers)
            {
                var headerParts = header.Split([':'], 2);
                if (headerParts.Length == 2)
                {
                    if (headerParts[0].StartsWith("HTTP"))
                    {
                        response.StatusCode = (HttpStatusCode)int.Parse(headerParts[0].Split(' ')[1]);
                    }
                    else
                    {
                        response.Headers.TryAddWithoutValidation(headerParts[0], headerParts[1].Trim());
                    }
                }
            }
            response.Content = new StringContent((await reader.ReadToEndAsync()).TrimEnd());
            responseMessages.Add(response);
        }
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(TextReader reader)
    {
        string line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            yield return line;
        }
    }

    internal static HttpContent CreateHttpContent(HttpRequestMessage request)
    {
        var memoryStream = new MemoryStream();
        var writer = new StreamWriter(memoryStream);

        writer.WriteLine($"{request.Method} {request.RequestUri} HTTP/{request.Version}");

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
            request.Content.CopyToAsync(memoryStream).Wait();
        }
        else
        {
            writer.WriteLine();
            writer.Flush();
        }

        memoryStream.Position = 0;
        var streamContent = new StreamContent(memoryStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/http");

        return streamContent;
    }
}
