namespace NOF.Sample.Identity;

public sealed record IdentityStatusResponse(bool Authenticated, string? Name, string? Subject, IReadOnlyList<string> Permissions, string Authority);
