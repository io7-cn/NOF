using NHibernate.Mapping.ByCode;

namespace NOF.Infrastructure.NHibernate;

/// <summary>Maps framework message entities using NHibernate's native mapping API.</summary>
public static class MessageModelConfigurations
{
    /// <summary>Configures the Inbox receipt model.</summary>
    public static void Inbox(ModelMapper mapper)
    {
        mapper.Class<NOFInboxMessage>(map =>
        {
            map.Table(nameof(NOFInboxMessage));
            map.Lazy(false);
            map.DynamicUpdate(true);
            map.OptimisticLock(OptimisticLockMode.Dirty);
            map.ComposedId(id =>
            {
                id.Property(value => value.Id, property => { });
                id.Property(value => value.Route, property => { property.Length(512); });
            });
            map.Property(value => value.CreatedAtUtc, property => { property.Column(column => column.Index("IX_NOFInboxMessage_Status_CreatedAtUtc")); });
            map.Property(value => value.RetryCount, property => { });
            map.Property(value => value.Payload, property => { property.Type(global::NHibernate.NHibernateUtil.BinaryBlob); });
            map.Property(value => value.Headers, property => { property.Type(global::NHibernate.NHibernateUtil.StringClob); property.NotNullable(true); });
            map.Property(value => value.ProcessedAtUtc, property => { });
            map.Property(value => value.FailedAtUtc, property => { });
            map.Property(value => value.ErrorMessage, property => { property.Length(2048); });
            map.Property(value => value.ClaimedBy, property => { property.Length(256); property.Column(column => column.Index("IX_NOFInboxMessage_ClaimedBy")); });
            map.Property(value => value.ClaimExpiresAtUtc, property => { property.Column(column => column.Index("IX_NOFInboxMessage_Status_ClaimExpiresAtUtc")); });
            map.Property(value => value.Status, property => { property.Column(column => column.Index("IX_NOFInboxMessage_Status_CreatedAtUtc,IX_NOFInboxMessage_Status_ClaimExpiresAtUtc")); });
        });
    }
    /// <summary>Configures the Outbox intent model.</summary>
    public static void Outbox(ModelMapper mapper)
    {
        mapper.Class<NOFOutboxMessage>(map =>
        {
            map.Table(nameof(NOFOutboxMessage));
            map.Lazy(false);
            map.Id(value => value.Id, id => { id.Generator(Generators.Assigned); });
            map.Property(value => value.CreatedAtUtc, property => { property.Column(column => column.Index("IX_NOFOutboxMessage_Status_CreatedAtUtc")); });
            map.Property(value => value.RetryCount, property => { });
            map.Property(value => value.MessageType, property => { property.Length(512); property.NotNullable(true); });
            map.Property(value => value.SchemaVersion, property => { });
            map.Property(value => value.NextAttemptAtUtc, property => { property.Column(column => column.Index("IX_NOFOutboxMessage_Status_NextAttemptAtUtc")); });
            map.Property(value => value.Payload, property => { property.Type(global::NHibernate.NHibernateUtil.BinaryBlob); });
            map.Property(value => value.Headers, property => { property.Type(global::NHibernate.NHibernateUtil.StringClob); property.NotNullable(true); });
            map.Property(value => value.SentAtUtc, property => { });
            map.Property(value => value.FailedAtUtc, property => { });
            map.Property(value => value.ErrorMessage, property => { property.Length(2048); });
            map.Property(value => value.ClaimedBy, property => { property.Length(256); property.Column(column => column.Index("IX_NOFOutboxMessage_ClaimedBy")); });
            map.Property(value => value.ClaimExpiresAtUtc, property => { property.Column(column => column.Index("IX_NOFOutboxMessage_Status_ClaimExpiresAtUtc")); });
            map.Property(value => value.Status, property => { property.Column(column => column.Index("IX_NOFOutboxMessage_Status_CreatedAtUtc,IX_NOFOutboxMessage_Status_ClaimExpiresAtUtc,IX_NOFOutboxMessage_Status_NextAttemptAtUtc")); });
            map.Property(value => value.TraceParent, property => { property.Length(128); property.Column(column => column.Index("IX_NOFOutboxMessage_TraceParent")); });
        });
    }
}
