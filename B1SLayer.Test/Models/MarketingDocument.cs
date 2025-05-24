using System.Text.Json.Serialization;

namespace B1SLayer.Test.Models;

internal class MarketingDocument
{
    public int DocEntry { get; set; }
    public string CardCode { get; set; }
    public string CardName { get; set; }
    public DateTime DocDate { get; set; }
    public decimal DocTotal { get; set; }
    public string DocCurrency { get; set; }
    public string DocStatus { get; set; }
    public string Comments { get; set; }
    public bool Cancelled { get; set; }
    public char DocumentStatus { get; set; } // O=Open, C=Closed
    
    [JsonPropertyName("BPL_IDAssignedToInvoice")]
    public int BranchId { get; set; }
    
    [JsonPropertyName("U_CustomField")]
    public string UserDefinedField { get; set; }
    
    public DateTimeOffset? UpdateDateTime { get; set; }
}
