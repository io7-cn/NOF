using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Contains the CreateOAuthClientRequest value or an OAuth failure.</summary>
public union OAuthClientNormalization(CreateOAuthClientRequest, Failure);
