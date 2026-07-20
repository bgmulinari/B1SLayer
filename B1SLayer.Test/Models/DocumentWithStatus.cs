namespace B1SLayer.Test.Models;

internal class DocumentWithStatus
{
    public int DocEntry { get; set; }
    public BoStatus DocumentStatus { get; set; }
}

internal enum BoStatus
{
    bost_Open,
    bost_Close,
    bost_Paid,
    bost_Delivered
}
