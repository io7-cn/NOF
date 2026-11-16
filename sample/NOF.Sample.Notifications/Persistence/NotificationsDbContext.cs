using Microsoft.EntityFrameworkCore;
using NOF.Infrastructure.EntityFrameworkCore;

namespace NOF.Sample.Notifications.Persistence;

public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : NOFDbContext(options)
{
    public DbSet<MessageReceiptRow> Receipts => Set<MessageReceiptRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<MessageReceiptRow>(entity =>
        {
            entity.HasKey(value => value.Id);
            entity.HasIndex(value => value.OrderId);
            entity.Property(value => value.IdempotencyKey).HasMaxLength(256).IsRequired();
            entity.HasIndex(value => new { value.IdempotencyKey, value.Kind }).IsUnique();
            entity.Property(value => value.Summary).HasMaxLength(1024);
        });
    }
}
