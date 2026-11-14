using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NOF.Application;
using NOF.Infrastructure;
using System.Security.Cryptography;
using System.Text;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>
/// Implementation of signing key management with key rotation support.
/// Each signing key is persisted as a single row with encrypted private key material.
/// </summary>
public sealed class PersistenceSigningKeyService : ISigningKeyService
{
    private readonly OAuthAuthorizationServerOptions _options;
    private readonly ISigningKeyStore _keys;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PersistenceSigningKeyService> _logger;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly DeploymentOptions _deployment;
    private byte[]? _encryptionKey;

    public PersistenceSigningKeyService(
        IOptions<OAuthAuthorizationServerOptions> options,
        ISigningKeyStore keys,
        IUnitOfWork unitOfWork,
        IHostEnvironment hostEnvironment,
        IOptions<DeploymentOptions> deployment,
        ILogger<PersistenceSigningKeyService> logger)
    {
        _options = options.Value;
        _keys = keys;
        _unitOfWork = unitOfWork;
        _hostEnvironment = hostEnvironment;
        _deployment = deployment.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ManagedSigningKey> GetCurrentSigningKeyAsync(CancellationToken cancellationToken = default)
    {
        await EnsurePrimaryKeysPersistedAsync(cancellationToken).ConfigureAwait(false);
        var keys = await LoadKeysAsync(cancellationToken).ConfigureAwait(false);
        if (keys is not { Count: > 0 })
        {
            throw new InvalidOperationException("No active signing keys were found.");
        }

        return keys[0];
    }

    /// <inheritdoc />
    public async Task<ManagedSigningKey[]> GetAllKeysAsync(CancellationToken cancellationToken = default)
    {
        await EnsurePrimaryKeysPersistedAsync(cancellationToken).ConfigureAwait(false);
        return [.. await LoadKeysAsync(cancellationToken).ConfigureAwait(false) ?? []];
    }

    /// <inheritdoc />
    public async Task RotateKeyAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - _options.RevokedSigningKeyRetention;
        await _keys.DeleteRevokedAsync(cutoff, cancellationToken).ConfigureAwait(false);

        await EnsurePrimaryKeysPersistedAsync(cancellationToken).ConfigureAwait(false);
        var newKey = GenerateNewKey();
        EnsureEncryptionKeyInitialized();

        var now = DateTime.UtcNow;
        var activeAndPublished = await _keys.ListPublishedAsync(cancellationToken).ConfigureAwait(false);

        EnsurePrimaryKeys(activeAndPublished, now);

        var activeKey = activeAndPublished
            .Where(key => key.Status == PersistedSigningKeyStatus.Active)
            .OrderByDescending(key => key.UpdatedAtUtc)
            .ThenByDescending(key => key.CreatedAtUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No active signing key was found after initialization.");
        var nextActiveKey = activeAndPublished
            .Where(key => key.Status == PersistedSigningKeyStatus.NextActive)
            .OrderByDescending(key => key.CreatedAtUtc)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No next signing key was found after initialization.");

        activeKey.Status = PersistedSigningKeyStatus.Retired;
        activeKey.UpdatedAtUtc = now;

        nextActiveKey.Status = PersistedSigningKeyStatus.Active;
        nextActiveKey.UpdatedAtUtc = now;

        _keys.Add(ToPersistedSigningKey(newKey, PersistedSigningKeyStatus.NextActive, now));
        ApplyRetiredRetention(activeAndPublished, now);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            throw;
        }
    }

    private async Task<List<ManagedSigningKey>?> LoadKeysAsync(CancellationToken cancellationToken)
    {
        EnsureEncryptionKeyInitialized();

        var persistedKeys = await _keys.ListPublishedAsync(cancellationToken).ConfigureAwait(false);

        if (persistedKeys.Count == 0)
        {
            return null;
        }

        var keys = persistedKeys.OrderBy(key => key.Status == PersistedSigningKeyStatus.Active ? 0 : key.Status == PersistedSigningKeyStatus.NextActive ? 1 : 2).ThenByDescending(key => key.CreatedAtUtc).Select(ToManagedSigningKey).ToList();
        if (keys.Count == 0)
        {
            throw new InvalidOperationException("No usable signing keys were loaded from persistence.");
        }

        return keys;
    }

    private async Task EnsurePrimaryKeysPersistedAsync(CancellationToken cancellationToken)
    {
        EnsureEncryptionKeyInitialized();

        var now = DateTime.UtcNow;
        var keys = await _keys.ListPublishedAsync(cancellationToken).ConfigureAwait(false);
        var hasChanges = EnsurePrimaryKeys(keys, now);
        if (!hasChanges)
        {
            return;
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            var initialized = await _keys.HasStatusAsync(PersistedSigningKeyStatus.Active, cancellationToken).ConfigureAwait(false);
            var prepared = await _keys.HasStatusAsync(PersistedSigningKeyStatus.NextActive, cancellationToken).ConfigureAwait(false);
            if (initialized && prepared)
            {
                return;
            }

            throw;
        }
    }

    private bool EnsurePrimaryKeys(List<PersistedSigningKey> keys, DateTime now)
    {
        var hasChanges = false;
        var activeKeys = keys
            .Where(key => key.Status == PersistedSigningKeyStatus.Active)
            .OrderByDescending(key => key.UpdatedAtUtc)
            .ThenByDescending(key => key.CreatedAtUtc)
            .ToList();
        var currentActive = activeKeys.FirstOrDefault();
        foreach (var extraActive in activeKeys.Skip(1))
        {
            MoveToRetired(extraActive, now);
            hasChanges = true;
        }

        var nextActiveKeys = keys
            .Where(key => key.Status == PersistedSigningKeyStatus.NextActive)
            .OrderByDescending(key => key.CreatedAtUtc)
            .ToList();
        var nextActive = nextActiveKeys.FirstOrDefault();
        foreach (var extraNextActive in nextActiveKeys.Skip(1))
        {
            MoveToRevoked(extraNextActive, now);
            hasChanges = true;
        }

        if (currentActive is null)
        {
            if (nextActive is not null)
            {
                nextActive.Status = PersistedSigningKeyStatus.Active;
                nextActive.UpdatedAtUtc = now;
                nextActive.InvalidatedAtUtc = null;
                currentActive = nextActive;
                nextActive = null;
                hasChanges = true;
            }
            else
            {
                var generatedActive = ToPersistedSigningKey(GenerateNewKey(), PersistedSigningKeyStatus.Active, now);
                _keys.Add(generatedActive);
                keys.Add(generatedActive);
                currentActive = generatedActive;
                hasChanges = true;
            }
        }

        if (nextActive is null)
        {
            var generatedNextActive = ToPersistedSigningKey(GenerateNewKey(), PersistedSigningKeyStatus.NextActive, now);
            _keys.Add(generatedNextActive);
            keys.Add(generatedNextActive);
            hasChanges = true;
        }

        return hasChanges;
    }

    private void ApplyRetiredRetention(List<PersistedSigningKey> keys, DateTime now)
    {
        var retiredKeys = keys
            .Where(key => key.Status == PersistedSigningKeyStatus.Retired)
            .OrderByDescending(key => key.CreatedAtUtc)
            .ToList();

        for (var index = _options.RetiredKeyRetentionCount; index < retiredKeys.Count; index++)
        {
            MoveToRevoked(retiredKeys[index], now);
        }
    }

    private ManagedSigningKey GenerateNewKey()
    {
        var rsa = RSA.Create(_options.KeySize);
        var kid = ComputeKeyId(rsa);
        var key = new RsaSecurityKey(rsa) { KeyId = kid };

        return new ManagedSigningKey
        {
            Kid = kid,
            Key = key,
            CreatedAtUtc = DateTime.UtcNow,
            ActivatedAtUtc = DateTime.UtcNow
        };
    }

    private PersistedSigningKey ToPersistedSigningKey(
        ManagedSigningKey key,
        PersistedSigningKeyStatus status,
        DateTime now)
    {
        return new PersistedSigningKey
        {
            Kid = key.Kid,
            EncryptedPrivateKey = Encrypt(Convert.ToBase64String(key.Key.Rsa.ExportPkcs8PrivateKey())),
            PublicKey = Convert.ToBase64String(key.Key.Rsa.ExportSubjectPublicKeyInfo()),
            Status = status,
            CreatedAtUtc = key.CreatedAtUtc,
            UpdatedAtUtc = now,
            InvalidatedAtUtc = status == PersistedSigningKeyStatus.Revoked ? now : null
        };
    }

    private ManagedSigningKey ToManagedSigningKey(PersistedSigningKey persistedKey)
    {
        var privateKeyBase64 = Decrypt(persistedKey.EncryptedPrivateKey);
        var privateKeyBytes = Convert.FromBase64String(privateKeyBase64);
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);

        var computedKid = ComputeKeyId(rsa);
        if (!string.Equals(computedKid, persistedKey.Kid, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Persisted signing key '{persistedKey.Kid}' failed integrity validation.");
        }

        return new ManagedSigningKey
        {
            Kid = persistedKey.Kid,
            Key = new RsaSecurityKey(rsa) { KeyId = persistedKey.Kid },
            CreatedAtUtc = persistedKey.CreatedAtUtc,
            ActivatedAtUtc = persistedKey.Status == PersistedSigningKeyStatus.Active
                ? persistedKey.UpdatedAtUtc
                : persistedKey.CreatedAtUtc
        };
    }

    private static void MoveToRetired(PersistedSigningKey key, DateTime now)
    {
        key.Status = PersistedSigningKeyStatus.Retired;
        key.UpdatedAtUtc = now;
        key.InvalidatedAtUtc = null;
    }

    private static void MoveToRevoked(PersistedSigningKey key, DateTime now)
    {
        key.Status = PersistedSigningKeyStatus.Revoked;
        key.InvalidatedAtUtc = now;
        key.UpdatedAtUtc = now;
    }

    private void EnsureEncryptionKeyInitialized()
    {
        if (_encryptionKey is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.SigningKeyEncryptionKey))
        {
            var localKeyPath = ResolveLocalEncryptionKeyPath();
            _options.SigningKeyEncryptionKey = LoadOrCreateLocalEncryptionKey(localKeyPath);
            _logger.LogWarning(
                "SigningKeyEncryptionKey is not configured. Using a locally persisted fallback secret from '{KeyPath}'. This is suitable for single-node and local development only. Configure a shared SigningKeyEncryptionKey for multi-node deployments.",
                localKeyPath);
        }

        _encryptionKey = SHA256.HashData(Encoding.UTF8.GetBytes(_options.SigningKeyEncryptionKey));
    }

    private string LoadOrCreateLocalEncryptionKey(string keyPath)
    {
        if (File.Exists(keyPath))
        {
            var persisted = File.ReadAllText(keyPath, Encoding.UTF8).Trim();
            if (!string.IsNullOrWhiteSpace(persisted))
            {
                return persisted;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);

        var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(keyPath, generated, Encoding.UTF8);
        return generated;
    }

    private string ResolveLocalEncryptionKeyPath()
    {
        var appName = SanitizePathSegment(
            !string.IsNullOrWhiteSpace(_deployment.ServiceName)
                ? _deployment.ServiceName
                : "nof-app");
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = !string.IsNullOrWhiteSpace(_hostEnvironment.ContentRootPath)
                ? _hostEnvironment.ContentRootPath
                : AppContext.BaseDirectory;
        }

        return Path.Combine(basePath, "NOF", appName, "oidc-signing-key-encryption.key");
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        return builder.Length == 0 ? "nof-app" : builder.ToString();
    }

    private string Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherText = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_encryptionKey!, 16);
        aes.Encrypt(nonce, plaintextBytes, cipherText, tag);

        var protectedBytes = new byte[nonce.Length + tag.Length + cipherText.Length];
        Buffer.BlockCopy(nonce, 0, protectedBytes, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, protectedBytes, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherText, 0, protectedBytes, nonce.Length + tag.Length, cipherText.Length);

        return Convert.ToBase64String(protectedBytes);
    }

    private string Decrypt(string protectedText)
    {
        var protectedBytes = Convert.FromBase64String(protectedText);
        if (protectedBytes.Length < 28)
        {
            throw new InvalidOperationException("Persisted signing key payload is invalid.");
        }

        var nonce = protectedBytes[..12];
        var tag = protectedBytes[12..28];
        var cipherText = protectedBytes[28..];
        var plaintextBytes = new byte[cipherText.Length];

        using var aes = new AesGcm(_encryptionKey!, 16);
        aes.Decrypt(nonce, cipherText, tag, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    private static string ComputeKeyId(RSA rsa)
    {
        var publicKeyDer = rsa.ExportSubjectPublicKeyInfo();
        var hash = SHA256.HashData(publicKeyDer);
        return Base64UrlEncoder.Encode(hash)[..16];
    }
}
