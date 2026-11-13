using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;
/// <summary>The named OAuth client does not exist.</summary>
public sealed record OAuthClientNotFound(string Detail = "OAuth client was not found.") : IFailure
{
    /// <inheritdoc/>
    public string Title => "OAuth client not found";
}

/// <summary>A client identifier is already allocated.</summary>
public sealed record OAuthClientConflict(string Detail = "OAuth client already exists.") : IFailure
{
    /// <inheritdoc/>
    public string Title => "OAuth client conflict";
}

/// <summary>The registration initial access token could not be authenticated.</summary>
public sealed record OAuthInitialAccessTokenInvalid(string Detail) : IFailure
{
    /// <inheritdoc/>
    public string Title => "Initial access token invalid";
}

/// <summary>An authenticated initial access token lacks the required registration scope.</summary>
public sealed record OAuthInitialAccessScopeMissing(string Detail) : IFailure
{
    /// <inheritdoc/>
    public string Title => "Initial access scope missing";
}

/// <summary>The complete admission outcomes for dynamic OAuth client registration.</summary>
public union OAuthInitialAccessAuthorization(NOF.Contract.Empty, OAuthInitialAccessTokenInvalid, OAuthInitialAccessScopeMissing);
/// <summary>Invalid client request.</summary>
public sealed record OAuthClientInvalidRequest(string Detail) : IFailure
{
    /// <inheritdoc/>
    public string Title => "Invalid client request";
}

/// <summary>Invalid client operation.</summary>
public sealed record OAuthClientInvalidOperation(string Detail) : IFailure
{
    /// <inheritdoc/>
    public string Title => "Invalid client operation";
}

/// <summary>Invalid registration token.</summary>
public sealed record OAuthClientRegistrationTokenInvalid(string Detail) : IFailure
{
    /// <inheritdoc/>
    public string Title => "Invalid registration token";
}

/// <summary>Invalid client metadata.</summary>
public sealed record OAuthClientMetadataInvalid(string Detail) : IFailure
{
    /// <inheritdoc/>
    public string Title => "Invalid client metadata";
}

/// <summary>Invalid redirect URI.</summary>
public sealed record OAuthClientRedirectUriInvalid(string Detail) : IFailure
{
    /// <inheritdoc/>
    public string Title => "Invalid redirect URI";
}

/// <summary>Client storage failed.</summary>
public sealed record OAuthClientStorageFailure(string Detail) : IFailure
{
    /// <inheritdoc/>
    public string Title => "Client storage failed";
}

/// <summary>The complete client administration mutation outcomes.</summary>
public union OAuthClientMutation(NOF.Contract.Empty, OAuthClientNotFound, OAuthClientConflict, OAuthClientInvalidRequest, OAuthClientInvalidOperation, OAuthClientRegistrationTokenInvalid, OAuthClientMetadataInvalid, OAuthClientRedirectUriInvalid, OAuthClientStorageFailure);
