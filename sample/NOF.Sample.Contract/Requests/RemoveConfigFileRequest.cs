namespace NOF.Sample;

public record RemoveConfigFileRequest
{
    public long NodeId { get; set; }
    public required string FileName { get; set; }
}
