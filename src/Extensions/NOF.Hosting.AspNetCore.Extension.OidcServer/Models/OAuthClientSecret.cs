using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Contains the OAuthClientSecretDescriptor value or an OAuth failure.</summary>
public union OAuthClientSecret(OAuthClientSecretDescriptor, Failure);
