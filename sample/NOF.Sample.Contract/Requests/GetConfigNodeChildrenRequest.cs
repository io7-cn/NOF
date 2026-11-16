namespace NOF.Sample;

public record GetConfigNodeChildrenRequest
{
    public long Id { get; set; }
}

public record GetConfigNodeChildrenResponse
{
    public long NodeId { get; set; }
    public required List<long> ChildrenIds { get; set; }
}
