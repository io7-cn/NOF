namespace NOF.Hosting;

/// <summary>Service identity and instance settings owned by the application's composition root.</summary>
public sealed class DeploymentOptions
{
    /// <summary>Gets or sets the stable service identity used for message routes and diagnostics.</summary>
    public string ServiceName { get; set; } = AppDomain.CurrentDomain.FriendlyName;
    /// <summary>Gets or sets the service's numeric Snowflake identifier.</summary>
    public uint ServiceId { get; set; }
    /// <summary>Gets or sets the instance's numeric Snowflake identifier.</summary>
    public uint InstanceId { get; set; } = 1;
    /// <summary>Gets or sets an explicit cleanup leader selection; null uses instance one.</summary>
    public bool? PrimaryNode { get; set; }
    /// <summary>Gets whether this instance performs primary-node maintenance.</summary>
    public bool IsPrimaryNode => PrimaryNode ?? InstanceId == 1;
}
