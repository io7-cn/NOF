using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure;

/// <summary>Accepts a delivery durably before transport acknowledgement, isolating its tenant and scope.</summary>
public sealed class MessageInboxReceiver(IServiceProvider services, IObjectSerializer serializer)
{
    /// <summary>Stores one consumer receipt or confirms that the same receipt already exists.</summary>
    public async Task ReceiveAsync(Guid messageId, ReadOnlyMemory<byte> payload,
        string consumerId, IEnumerable<KeyValuePair<string, string?>>? headers, CancellationToken cancellationToken)
    {
        var metadata = headers?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        metadata.TryGetValue(NOFAbstractionConstants.Transport.Headers.TenantId, out var tenantId);
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId(tenantId));
        await using var scope = services.CreateAsyncScope();
        using var ambientBinding = scope.ServiceProvider.BindNOF(Context.Current);
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var message = NOFInboxMessage.Create(messageId, consumerId, payload.ToArray(),
            serializer.SerializeToText(metadata, typeof(Dictionary<string, string?>)));
        try
        {
            await store.ReceiveAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            await using var verification = services.CreateAsyncScope();
            using var verificationBinding = verification.ServiceProvider.BindNOF(Context.Current);
            if (!await verification.ServiceProvider.GetRequiredService<IInboxStore>()
                .ExistsAsync(messageId, consumerId, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }
        }
    }

    /// <summary>Reads the transport message ID using case-insensitive header semantics.</summary>
    public static Guid GetMessageId(IEnumerable<KeyValuePair<string, string?>>? headers)
    {
        var value = headers?.FirstOrDefault(pair => string.Equals(pair.Key, NOFAbstractionConstants.Transport.Headers.MessageId, StringComparison.OrdinalIgnoreCase)).Value;
        return Guid.TryParse(value, out var id) ? id : Guid.NewGuid();
    }
}
