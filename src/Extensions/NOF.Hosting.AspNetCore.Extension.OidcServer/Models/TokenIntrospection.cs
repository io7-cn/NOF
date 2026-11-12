using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Contains the IntrospectTokenResponse value or an OAuth failure.</summary>
public union TokenIntrospection(IntrospectTokenResponse, Failure);
