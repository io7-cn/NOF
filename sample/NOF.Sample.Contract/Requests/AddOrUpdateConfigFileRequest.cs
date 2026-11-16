namespace NOF.Sample;

public record AddOrUpdateConfigFileRequest
{
    public long NodeId { get; set; }
    public required string FileName { get; set; }
    public required string Content { get; set; }
}
