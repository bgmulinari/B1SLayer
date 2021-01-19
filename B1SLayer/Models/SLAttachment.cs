namespace B1SLayer
{
    public class SLAttachment
    {
        public string odatametadata { get; set; }
        public int AbsoluteEntry { get; set; }
        public SLAttachmentLines[] Attachments2_Lines { get; set; }
    }

    public class SLAttachmentLines
    {
        public string SourcePath { get; set; }
        public string FileName { get; set; }
        public string FileExtension { get; set; }
        public string AttachmentDate { get; set; }
        public int UserID { get; set; }
        public string Override { get; set; }
    }
}