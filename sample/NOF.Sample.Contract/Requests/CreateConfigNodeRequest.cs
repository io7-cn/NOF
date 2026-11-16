namespace NOF.Sample;

public record CreateConfigNodeRequest
{
    public required string Name { get; set; }
    public long? ParentId { get; set; }
}

public sealed record CreatedConfigNode(long Id);
