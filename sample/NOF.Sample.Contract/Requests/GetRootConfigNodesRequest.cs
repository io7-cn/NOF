namespace NOF.Sample;

public record GetRootConfigNodesRequest { }

public record GetRootConfigNodesResponse
{
    public required List<ConfigNodeDto> Nodes { get; set; }
}
