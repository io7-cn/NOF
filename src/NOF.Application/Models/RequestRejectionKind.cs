namespace NOF.Application;

/// <summary>Identifies an application policy rejection independently of transport status codes.</summary>
public enum RequestRejectionKind
{
    /// <summary>A verified caller identity is required.</summary>
    AuthenticationRequired,
    /// <summary>The caller is not allowed to execute this use case.</summary>
    Forbidden,
    /// <summary>The application input does not meet the execution policy.</summary>
    ValidationFailed
}
