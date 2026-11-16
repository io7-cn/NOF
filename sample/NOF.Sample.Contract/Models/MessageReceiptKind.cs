namespace NOF.Sample;

/// <summary>Numeric business-projection identities; values are stable on the wire and in storage.</summary>
public enum MessageReceiptKind
{
    Mail = 0,
    OrderAudit = 1,
    OrderMetrics = 2
}
