namespace NOF.Sample;

public record GetConfigNodeByIdRequest
{
    public long Id { get; set; }
}

public record GetConfigNodeByIdResponse
{
    public required ConfigNodeDto Node { get; set; }
}
