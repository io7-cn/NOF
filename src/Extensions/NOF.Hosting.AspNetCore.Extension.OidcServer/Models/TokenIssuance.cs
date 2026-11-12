using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Contains the IssueTokenResponse value or an OAuth failure.</summary>
public union TokenIssuance(IssueTokenResponse, Failure);
