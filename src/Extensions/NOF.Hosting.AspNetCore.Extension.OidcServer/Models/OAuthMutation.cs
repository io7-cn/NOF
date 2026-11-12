using NOF.Abstraction;
using NOF.Contract;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Contains completion or rejection of an OAuth mutation.</summary>
public union OAuthMutation(Empty, Failure);
