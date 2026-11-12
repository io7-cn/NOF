using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Contains the OAuthDeviceAuthorizationResponse value or an OAuth failure.</summary>
public union OAuthDeviceAuthorization(OAuthDeviceAuthorizationResponse, Failure);
