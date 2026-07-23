using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

/// <summary>
///     Provides HTTP content helpers that paper over the API differences between the target frameworks.
/// </summary>
internal static class HttpExtensions
{
    /// <summary>
    ///     Creates a JSON <see cref="HttpContent" /> from the given UTF-8 bytes.
    /// </summary>
    public static HttpContent CreateJsonContent(byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=utf-8");
        return content;
    }

    extension(HttpResponseMessage response)
    {
        /// <summary>
        ///     Reads the response body as a string, or an empty string when there is no content.
        /// </summary>
        public Task<string> ReadStringAsync(CancellationToken cancellationToken)
        {
            if (response?.Content == null)
            {
                return Task.FromResult(string.Empty);
            }

#if NETSTANDARD2_0
            return response.Content.ReadAsStringAsync().WithCancellation(cancellationToken);
#else
            return response.Content.ReadAsStringAsync(cancellationToken);
#endif
        }

        /// <summary>
        ///     Reads the response body as a byte array, or an empty array when there is no content.
        /// </summary>
        public Task<byte[]> ReadBytesAsync(CancellationToken cancellationToken)
        {
            if (response?.Content == null)
            {
                return Task.FromResult(Array.Empty<byte>());
            }

#if NETSTANDARD2_0
            return response.Content.ReadAsByteArrayAsync().WithCancellation(cancellationToken);
#else
            return response.Content.ReadAsByteArrayAsync(cancellationToken);
#endif
        }

        /// <summary>
        ///     Reads the response body as a <see cref="Stream" />, or <see cref="Stream.Null" /> when there is no content.
        /// </summary>
        public Task<Stream> ReadStreamAsync(CancellationToken cancellationToken)
        {
            if (response?.Content == null)
            {
                return Task.FromResult(Stream.Null);
            }

#if NETSTANDARD2_0
            return response.Content.ReadAsStreamAsync().WithCancellation(cancellationToken);
#else
            return response.Content.ReadAsStreamAsync(cancellationToken);
#endif
        }

        /// <summary>
        ///     Reads the response body as a stream for JSON parsing, or <see cref="Stream.Null" /> when there is no content.
        ///     JSON parsing requires UTF-8, so when the response declares a different charset the body is
        ///     decoded through it and transcoded to UTF-8; otherwise the stream is handed over as-is.
        /// </summary>
        public async Task<Stream> ReadJsonStreamAsync(CancellationToken cancellationToken)
        {
            var charSet = response?.Content?.Headers.ContentType?.CharSet?.Trim('"');

            if (string.IsNullOrEmpty(charSet)
                || charSet.Equals("utf-8", StringComparison.OrdinalIgnoreCase)
                || charSet.Equals("us-ascii", StringComparison.OrdinalIgnoreCase)
                || charSet.Equals("ascii", StringComparison.OrdinalIgnoreCase))
            {
                return await response.ReadStreamAsync(cancellationToken).ConfigureAwait(false);
            }

            var responseText = await response.ReadStringAsync(cancellationToken).ConfigureAwait(false);
            return new MemoryStream(Encoding.UTF8.GetBytes(responseText));
        }
    }

    /// <summary>
    ///     Copies the given stream, from its current position, to a new byte array.
    /// </summary>
    public static async Task<byte[]> ToByteArrayAsync(this Stream stream, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
#if NETSTANDARD2_0
        await stream.CopyToAsync(memoryStream, 81920, cancellationToken).ConfigureAwait(false);
#else
        await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
#endif
        return memoryStream.ToArray();
    }

#if NETSTANDARD2_0
    /// <summary>
    ///     Applies best-effort cancellation to a body read that lacks a token-taking overload on this target:
    ///     the caller is released as soon as the token fires, while the underlying read is left to run out.
    ///     Without this, a stalled body read could not be bounded by the request timeout or the caller's token.
    /// </summary>
    private static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            return await task.ConfigureAwait(false);
        }

        var cancellationTaskSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (cancellationToken.Register(state => ((TaskCompletionSource<bool>)state).TrySetResult(true), cancellationTaskSource))
        {
            if (await Task.WhenAny(task, cancellationTaskSource.Task).ConfigureAwait(false) != task)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        return await task.ConfigureAwait(false);
    }
#endif
}
