using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Contains the OAuthClientDescriptor value or an OAuth failure.</summary>
public union OAuthClientLookup(OAuthClientDescriptor, Failure);
