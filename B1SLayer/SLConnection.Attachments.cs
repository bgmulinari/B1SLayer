using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

public partial class SLConnection
{
    /// <summary>
    ///     Uploads the provided file as an attachment.
    /// </summary>
    /// <remarks>
    ///     An attachment folder must be defined. See section 'Setting up an Attachment Folder' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="path">
    ///     The path to the file to be uploaded.
    /// </param>
    /// <returns>
    ///     A <see cref="SLAttachment" /> object with information about the created attachment entry.
    /// </returns>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<SLAttachment> PostAttachmentAsync(string path, CancellationToken cancellationToken = default) => PostAttachmentAsync(Path.GetFileName(path), File.ReadAllBytes(path), cancellationToken);

    /// <summary>
    ///     Uploads the provided file as an attachment.
    /// </summary>
    /// <remarks>
    ///     An attachment folder must be defined. See section 'Setting up an Attachment Folder' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="fileName">
    ///     The file name of the file to be uploaded including the file extension.
    /// </param>
    /// <param name="file">
    ///     The file to be uploaded.
    /// </param>
    /// <returns>
    ///     A <see cref="SLAttachment" /> object with information about the created attachment entry.
    /// </returns>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<SLAttachment> PostAttachmentAsync(string fileName, byte[] file, CancellationToken cancellationToken = default) => PostAttachmentsCoreAsync([new KeyValuePair<string, byte[]>(fileName, file)], cancellationToken);

    /// <summary>
    ///     Uploads the provided file as an attachment.
    /// </summary>
    /// <remarks>
    ///     An attachment folder must be defined. See section 'Setting up an Attachment Folder' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="fileName">
    ///     The file name of the file to be uploaded including the file extension.
    /// </param>
    /// <param name="file">
    ///     The file to be uploaded.
    /// </param>
    /// <returns>
    ///     A <see cref="SLAttachment" /> object with information about the created attachment entry.
    /// </returns>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<SLAttachment> PostAttachmentAsync(string fileName, Stream file, CancellationToken cancellationToken = default) => PostAttachmentsAsync(new Dictionary<string, Stream> { { fileName, file } }, cancellationToken);

    /// <summary>
    ///     Uploads the provided files as an attachment. All files will be posted as attachment lines in a single attachment entry.
    /// </summary>
    /// <remarks>
    ///     An attachment folder must be defined. See section 'Setting up an Attachment Folder' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="files">
    ///     A Dictionary containing the files to be uploaded, where the file name is the Key and the file is the Value.
    /// </param>
    /// <returns>
    ///     A <see cref="SLAttachment" /> object with information about the created attachment entry.
    /// </returns>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<SLAttachment> PostAttachmentsAsync(IDictionary<string, byte[]> files, CancellationToken cancellationToken = default) => PostAttachmentsCoreAsync(ToBufferedFiles(files), cancellationToken);

    /// <summary>
    ///     Uploads the provided files as an attachment. All files will be posted as attachment lines in a single attachment entry.
    /// </summary>
    /// <remarks>
    ///     An attachment folder must be defined. See section 'Setting up an Attachment Folder' in the Service Layer User Manual for more details.
    /// </remarks>
    /// <param name="files">
    ///     A Dictionary containing the files to be uploaded, where the file name is the Key and the file is the Value.
    /// </param>
    /// <returns>
    ///     A <see cref="SLAttachment" /> object with information about the created attachment entry.
    /// </returns>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public async Task<SLAttachment> PostAttachmentsAsync(IDictionary<string, Stream> files, CancellationToken cancellationToken = default)
    {
        var bufferedFiles = await BufferAttachmentFilesAsync(files, cancellationToken).ConfigureAwait(false);
        return await PostAttachmentsCoreAsync(bufferedFiles, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Performs the attachment upload with the given pre-buffered files.
    /// </summary>
    private async Task<SLAttachment> PostAttachmentsCoreAsync(IReadOnlyCollection<KeyValuePair<string, byte[]>> bufferedFiles, CancellationToken cancellationToken)
    {
        return await Request("Attachments2")
            .SendFilesAsync(HttpMethod.Post,
                bufferedFiles,
                async response =>
                {
                    var responseContent = await response.ReadStringAsync(cancellationToken).ConfigureAwait(false);
                    return JsonSerializer.Deserialize<SLAttachment>(responseContent, ProtocolSerializerOptions);
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Updates an existing attachment entry with the provided file. If the file already exists
    ///     in the attachment entry, it will be replaced. Otherwise, a new attachment line is added.
    /// </summary>
    /// <param name="attachmentEntry">
    ///     The attachment entry ID to be updated.
    /// </param>
    /// <param name="path">
    ///     The file path for the file to be updated including the file extension.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PatchAttachmentAsync(int attachmentEntry, string path, CancellationToken cancellationToken = default) => PatchAttachmentAsync(attachmentEntry, Path.GetFileName(path), File.ReadAllBytes(path), cancellationToken);

    /// <summary>
    ///     Updates an existing attachment entry with the provided file. If the file already exists
    ///     in the attachment entry, it will be replaced. Otherwise, a new attachment line is added.
    /// </summary>
    /// <param name="attachmentEntry">
    ///     The attachment entry ID to be updated.
    /// </param>
    /// <param name="fileName">
    ///     The file name of the file to be updated including the file extension.
    /// </param>
    /// <param name="file">
    ///     The file to be updated.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PatchAttachmentAsync(int attachmentEntry, string fileName, byte[] file, CancellationToken cancellationToken = default) => PatchAttachmentsCoreAsync(attachmentEntry, [new KeyValuePair<string, byte[]>(fileName, file)], cancellationToken);

    /// <summary>
    ///     Updates an existing attachment entry with the provided file. If the file already exists
    ///     in the attachment entry, it will be replaced. Otherwise, a new attachment line is added.
    /// </summary>
    /// <param name="attachmentEntry">
    ///     The attachment entry ID to be updated.
    /// </param>
    /// <param name="fileName">
    ///     The file name of the file to be updated including the file extension.
    /// </param>
    /// <param name="file">
    ///     The file to be updated.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PatchAttachmentAsync(int attachmentEntry, string fileName, Stream file, CancellationToken cancellationToken = default) => PatchAttachmentsAsync(attachmentEntry, new Dictionary<string, Stream> { { fileName, file } }, cancellationToken);

    /// <summary>
    ///     Updates an existing attachment entry with the provided files. If the file already exists
    ///     in the attachment entry, it will be replaced. Otherwise, a new attachment line is added.
    /// </summary>
    /// <param name="attachmentEntry">
    ///     The attachment entry ID to be updated.
    /// </param>
    /// <param name="files">
    ///     A Dictionary containing the files to be updated, where the file name is the Key and the file is the Value.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task PatchAttachmentsAsync(int attachmentEntry, IDictionary<string, byte[]> files, CancellationToken cancellationToken = default) => PatchAttachmentsCoreAsync(attachmentEntry, ToBufferedFiles(files), cancellationToken);

    /// <summary>
    ///     Updates an existing attachment entry with the provided files. If the file already exists
    ///     in the attachment entry, it will be replaced. Otherwise, a new attachment line is added.
    /// </summary>
    /// <param name="attachmentEntry">
    ///     The attachment entry ID to be updated.
    /// </param>
    /// <param name="files">
    ///     A Dictionary containing the files to be updated, where the file name is the Key and the file is the Value.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public async Task PatchAttachmentsAsync(int attachmentEntry, IDictionary<string, Stream> files, CancellationToken cancellationToken = default)
    {
        var bufferedFiles = await BufferAttachmentFilesAsync(files, cancellationToken).ConfigureAwait(false);
        await PatchAttachmentsCoreAsync(attachmentEntry, bufferedFiles, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Performs the attachment update with the given pre-buffered files.
    /// </summary>
    private async Task PatchAttachmentsCoreAsync(int attachmentEntry, IReadOnlyCollection<KeyValuePair<string, byte[]>> bufferedFiles, CancellationToken cancellationToken)
    {
        await Request($"Attachments2({attachmentEntry})")
            .SendFilesAsync(SLRequest.PatchMethod, bufferedFiles, _ => Task.FromResult(0), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Validates and buffers the given files upfront so the multipart content can be rebuilt in case the request is retried.
    /// </summary>
    private static async Task<List<KeyValuePair<string, byte[]>>> BufferAttachmentFilesAsync(IDictionary<string, Stream> files, CancellationToken cancellationToken)
    {
        if (files == null || files.Count == 0)
        {
            throw new ArgumentException("No files to be sent.");
        }

        var bufferedFiles = new List<KeyValuePair<string, byte[]>>(files.Count);

        foreach (var file in files)
        {
            bufferedFiles.Add(new KeyValuePair<string, byte[]>(file.Key, await file.Value.ToByteArrayAsync(cancellationToken).ConfigureAwait(false)));
        }

        return bufferedFiles;
    }

    /// <summary>
    ///     Validates the given files, which are already retry-safe byte arrays needing no additional buffering.
    /// </summary>
    private static List<KeyValuePair<string, byte[]>> ToBufferedFiles(IDictionary<string, byte[]> files)
    {
        if (files == null || files.Count == 0)
        {
            throw new ArgumentException("No files to be sent.");
        }

        return files.ToList();
    }

    /// <summary>
    ///     Downloads the specified attachment file as a <see cref="Stream" />. By default, the first attachment
    ///     line is downloaded if there are multiple attachment lines in one attachment.
    /// </summary>
    /// <param name="attachmentEntry">
    ///     The attachment entry ID to be downloaded.
    /// </param>
    /// <param name="fileName">
    ///     The file name of the attachment to be downloaded  (including the file extension). Only required if
    ///     you want to download an attachment line other than the first attachment line.
    /// </param>
    /// <returns>
    ///     The downloaded attachment file as a <see cref="Stream" />.
    /// </returns>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public async Task<Stream> GetAttachmentAsStreamAsync(int attachmentEntry, string fileName = null, CancellationToken cancellationToken = default) => new MemoryStream(await GetAttachmentAsBytesAsync(attachmentEntry, fileName, cancellationToken).ConfigureAwait(false));

    /// <summary>
    ///     Downloads the specified attachment file as a <see cref="byte" /> array. By default, the first attachment
    ///     line is downloaded if there are multiple attachment lines in one attachment.
    /// </summary>
    /// <param name="attachmentEntry">
    ///     The attachment entry ID to be downloaded.
    /// </param>
    /// <param name="fileName">
    ///     The file name of the attachment to be downloaded  (including the file extension). Only required if
    ///     you want to download an attachment line other than the first attachment line.
    /// </param>
    /// <returns>
    ///     The downloaded attachment file as a <see cref="byte" /> array.
    /// </returns>
    /// <param name="cancellationToken">
    ///     A token to cancel the asynchronous operation.
    /// </param>
    public Task<byte[]> GetAttachmentAsBytesAsync(int attachmentEntry, string fileName = null, CancellationToken cancellationToken = default) =>
        Request($"Attachments2({attachmentEntry})/$value")
            .SetQueryParam("filename", !string.IsNullOrEmpty(fileName) ? $"'{fileName}'" : null)
            .GetBytesAsync(cancellationToken);
}
