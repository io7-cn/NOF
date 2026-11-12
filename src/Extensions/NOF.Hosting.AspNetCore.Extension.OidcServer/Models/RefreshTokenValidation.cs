using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Contains the ValidateRefreshTokenResponse value or an OAuth failure.</summary>
public union RefreshTokenValidation(ValidateRefreshTokenResponse, Failure);
