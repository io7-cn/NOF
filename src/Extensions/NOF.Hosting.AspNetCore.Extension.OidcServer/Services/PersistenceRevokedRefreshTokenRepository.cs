using NOF.Application;
using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

public sealed class PersistenceRevokedRefreshTokenRepository : IRevokedRefreshTokenRepository
{
    private readonly IRevokedRefreshTokenStore _tokens;
    private readonly IUnitOfWork _unitOfWork;

    public PersistenceRevokedRefreshTokenRepository(IRevokedRefreshTokenStore tokens, IUnitOfWork unitOfWork)
    {
        _tokens = tokens;
        _unitOfWork = unitOfWork;
    }

    public async Task RevokeAsync(string tokenId, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        var expiresAtUtc = DateTime.UtcNow.Add(expiration);
        var revokedToken = await _tokens.FindAsync(tokenId, cancellationToken)
            .ConfigureAwait(false);

        if (revokedToken is null)
        {
            revokedToken = new RevokedRefreshToken
            {
                TokenId = tokenId,
                ExpiresAtUtc = expiresAtUtc
            };
            _tokens.Add(revokedToken);
        }
        else
        {
            revokedToken.ExpiresAtUtc = expiresAtUtc;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsRevokedAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        var revokedToken = await _tokens.FindAsync(tokenId, cancellationToken)
            .ConfigureAwait(false);

        return revokedToken is not null && revokedToken.ExpiresAtUtc > DateTime.UtcNow;
    }
}
