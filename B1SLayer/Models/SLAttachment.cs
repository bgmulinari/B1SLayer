namespace B1SLayer;

/// <summary>
/// Represents the details of an attachment entry.
/// </summary>
public class SLAttachment
{
    /// <summary>
    /// Gets or sets the ID of the attachment entry.
    /// </summary>
    public int AbsoluteEntry { get; set; }

    /// <summary>
    /// Gets or sets the lines of the attachment entry.
    /// </summary>
    public SLAttachmentLines[] Lines { get; set; }
}

/// <summary>
/// Represents the details of an attachment line.
/// </summary>
public class SLAttachmentLines
{
    /// <summary>
    /// Gets or sets the source path of the file.
    /// </summary>
    public string SourcePath { get; set; }

    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    public string FileName { get; set; }
    
    /// <summary>
    /// Gets or sets the file extension.
    /// </summary>
    public string FileExtension { get; set; }

    /// <summary>
    /// Gets or sets the attachment date.
    /// </summary>
    public string AttachmentDate { get; set; }

    /// <summary>
    /// Gets or sets the user ID.
    /// </summary>
    public int UserID { get; set; }

    /// <summary>
    /// Gets or sets the override indication.
    /// </summary>
    public string Override { get; set; }
}