namespace NOF.Sample;

public record SetActiveFileRequest
{
    public long NodeId { get; set; }
    public string? FileName { get; set; }
}
