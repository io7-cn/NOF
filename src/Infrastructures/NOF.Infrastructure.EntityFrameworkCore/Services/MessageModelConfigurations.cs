using Microsoft.EntityFrameworkCore;
namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>Configures framework-owned entities with native EF Core APIs.</summary>
public sealed class NOFInboxMessageModelCreatingContributor : IEFCoreModelConfiguration
{
    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NOFInboxMessage>(entity =>
        {
            entity.ToTable(nameof(NOFInboxMessage));
            entity.HasKey(nameof(NOFInboxMessage.Id), nameof(NOFInboxMessage.Route));
            entity.HasIndex(nameof(NOFInboxMessage.Status), nameof(NOFInboxMessage.CreatedAtUtc));
            entity.HasIndex(nameof(NOFInboxMessage.Status), nameof(NOFInboxMessage.ClaimExpiresAtUtc));
            entity.HasIndex(e => e.ClaimedBy);
            entity.Property(e => e.Route).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.Headers).IsRequired();
            entity.Property(e => e.ErrorMessage).HasMaxLength(2048);
            entity.Property(e => e.ClaimedBy).HasMaxLength(256).IsConcurrencyToken();
        });
    }
}

/// <summary>Configures framework-owned entities with native EF Core APIs.</summary>
public sealed class NOFOutboxMessageModelCreatingContributor : IEFCoreModelConfiguration
{
    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NOFOutboxMessage>(entity =>
        {
            entity.ToTable(nameof(NOFOutboxMessage));
            entity.HasKey(e => e.Id);
            entity.HasIndex(nameof(NOFOutboxMessage.Status), nameof(NOFOutboxMessage.CreatedAtUtc));
            entity.HasIndex(nameof(NOFOutboxMessage.Status), nameof(NOFOutboxMessage.ClaimExpiresAtUtc));
            entity.HasIndex(e => e.ClaimedBy);
            entity.Property(e => e.MessageType).HasMaxLength(512).IsRequired();
            entity.HasIndex(nameof(NOFOutboxMessage.Status), nameof(NOFOutboxMessage.NextAttemptAtUtc));
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.Headers).IsRequired();
            entity.Property(e => e.ErrorMessage).HasMaxLength(2048);
            entity.Property(e => e.ClaimedBy).HasMaxLength(256);
            entity.Property(e => e.TraceParent).HasMaxLength(128);
            entity.HasIndex(e => e.TraceParent);
        });
    }
}
