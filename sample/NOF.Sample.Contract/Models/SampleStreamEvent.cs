namespace NOF.Sample;

public record SampleStreamEvent
{
    public required string Topic { get; set; }

    public required string Message { get; set; }

    public int Index { get; set; }

    public DateTimeOffset Timestamp { get; set; }
}
