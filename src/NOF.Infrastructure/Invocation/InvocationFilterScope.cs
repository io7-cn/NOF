namespace NOF.Infrastructure;

/// <summary>The scope of one application rule.</summary>
public enum InvocationFilterScope
{
    /// <summary>All operations.</summary>
    Global,
    /// <summary>One service contract.</summary>
    Service,
    /// <summary>One stable operation identity.</summary>
    UseCase
}
