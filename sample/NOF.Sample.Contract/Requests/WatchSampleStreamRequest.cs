namespace NOF.Sample;

public record WatchSampleStreamRequest
{
    public string Topic { get; set; } = "NOF Streaming Demo";

    public int Count { get; set; } = 5;

    public int IntervalMilliseconds { get; set; } = 1000;

    public bool ForceFailure { get; set; }
}
