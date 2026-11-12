using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Contains the OAuthClientRegistrationSecretDescriptor value or an OAuth failure.</summary>
public union OAuthClientRegistration(OAuthClientRegistrationSecretDescriptor, Failure);
