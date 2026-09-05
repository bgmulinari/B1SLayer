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
///     Provides helper methods for handling multipart HTTP responses and creating HTTP content.
/// </summary>
internal static class MultipartHelper
{
    /// <summary>
    ///     Reads a multipart HTTP response and parses it into an array of <see cref="HttpResponseMessage" /> objects.
    /// </summary>
    /// <param name="response">The HTTP response containing the multipart content.</param>
    /// <returns>An array of <see cref="HttpResponseMessage" /> objects representing the individual parts of the multipart response.</returns>
    public static async Task<HttpResponseMessage[]> ReadMultipartResponseAsync(HttpResponseMessage response)
    {
        var innerResponses = new List<HttpResponseMessage>();
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var parts = content.Split(new[] { "HTTP/" }, StringSplitOptions.RemoveEmptyEntries).Skip(1);

        foreach (var part in parts)
        {
            var requestData = part.Split(new[] { "\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            requestData = requestData.Where(x => !x.StartsWith("--") && !x.StartsWith("\r\n")).ToArray();
            var headers = requestData[0].Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Skip(1);
            var httpResponse = new HttpResponseMessage();
            httpResponse.Version = new Version(part.Substring(0, 3));
            httpResponse.StatusCode = (HttpStatusCode)int.Parse(part.Substring(4, 3));

            if (requestData.Length > 1)
            {
                httpResponse.Content = new StringContent(requestData[1]);
                httpResponse.Content.Headers.Remove("Content-Type");
            }

            foreach (var header in headers)
            {
                // Each header line is split on the first ':' only, as the value itself may contain ':'
                var separatorIndex = header.IndexOf(':');

                if (separatorIndex <= 0)
                {
                    continue;
                }

                var headerName = header.Substring(0, separatorIndex).Trim();
                var headerValue = header.Substring(separatorIndex + 1).Trim();

                if (httpResponse.Content == null || !httpResponse.Content.Headers.TryAddWithoutValidation(headerName, headerValue))
                {
                    httpResponse.Headers.TryAddWithoutValidation(headerName, headerValue);
                }
            }

            innerResponses.Add(httpResponse);
        }

        return innerResponses.ToArray();
    }

    /// <summary>
    ///     Creates an HTTP content from the provided HTTP request message.
    /// </summary>
    /// <param name="request">The HTTP request message.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation, containing the created HTTP content.</returns>
    internal static async Task<HttpContent> CreateHttpContentAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var memoryStream = new MemoryStream();

        // HTTP/MIME requires CRLF line endings regardless of the platform's default
        using var writer = new StreamWriter(memoryStream, new UTF8Encoding(false), 1024, true) { NewLine = "\r\n" };
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
#if NETSTANDARD2_0
            await request.Content.CopyToAsync(memoryStream).ConfigureAwait(false);
#else
            await request.Content.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
#endif
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
    ///     Creates a <see cref="MultipartContent" /> with the given subtype and parts.
    /// </summary>
    internal static MultipartContent CreateMultipartContent(string subtype, IEnumerable<HttpContent> parts)
    {
        var multipartContent = new MultipartContent(subtype, "boundary_" + Guid.NewGuid());

        // Removes double quotes from boundary, otherwise the request fails with error 405 Method Not Allowed
        var boundary = multipartContent.Headers.ContentType.Parameters.First(x => x.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase));
        boundary.Value = boundary.Value.Replace("\"", string.Empty);

        foreach (var part in parts)
        {
            multipartContent.Add(part);
        }

        return multipartContent;
    }

    /// <summary>
    ///     Creates a form-data file part for attachment uploads.
    /// </summary>
    internal static HttpContent CreateFilePart(string fileName, byte[] file)
    {
        var content = new ByteArrayContent(file);
        content.Headers.Add("Content-Disposition", $"form-data; name=\"files\"; filename=\"{fileName}\"");
        content.Headers.Add("Content-Type", "application/octet-stream");
        return content;
    }
}
