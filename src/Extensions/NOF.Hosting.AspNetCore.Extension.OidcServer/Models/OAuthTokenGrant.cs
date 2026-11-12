using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Contains the OAuthTokenEndpointResponse value or an OAuth failure.</summary>
public union OAuthTokenGrant(OAuthTokenEndpointResponse, Failure);
