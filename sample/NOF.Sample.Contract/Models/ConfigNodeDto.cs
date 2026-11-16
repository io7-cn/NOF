namespace NOF.Sample;

public record ConfigNodeDto
{
    public long Id { get; set; }
    public long? ParentId { get; set; }
    public required string Name { get; set; }
    public string? ActiveFileName { get; set; }
    public required List<ConfigFileDto> ConfigFiles { get; set; }
}
