namespace NOF.Application;

/// <summary>Optional ordering within a service-qualified, tenant-local durable stream.</summary>
public sealed record OutboxEnqueueOptions
{
    /// <summary>Gets the stream key, or null for independent delivery.</summary>
    public string? OrderKey { get; init; }
    /// <summary>Gets whether this is the last message in the stream.</summary>
    public bool CompletesOrderKey { get; init; }
}
