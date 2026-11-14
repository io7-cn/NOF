using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Sample.Application.Entities;
using OpenIddict.EntityFrameworkCore.Models;

namespace NOF.Sample;

public class ConfigurationDbContext : NOFDbContext
{
    public ConfigurationDbContext(DbContextOptions<ConfigurationDbContext> options) : base(options)
    {
    }

    public DbSet<ConfigNode> ConfigNodes { get; set; }
    public DbSet<ConfigNodeChildren> ConfigNodeChildren { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseOpenIddict();
        // OpenIddict owns physical token pruning and revocation; business soft-delete conventions do not apply.
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreApplication>().HasSoftDelete(false);
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreAuthorization>().HasSoftDelete(false);
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreScope>().HasSoftDelete(false);
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreToken>().HasSoftDelete(false);

        modelBuilder.Entity<ConfigNode>(entity =>
        {
            entity.ToTable("ConfigNode");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();

            entity.OwnsMany(e => e.ConfigFiles, file =>
            {
                file.ToTable("ConfigFile");
                file.WithOwner().HasForeignKey("NodeId");
                file.HasKey("Id");
                file.Property("Id").ValueGeneratedOnAdd();
                // Content can be large
            });
        });

        // 配置读模型：ConfigNodeChildren
        modelBuilder.Entity<ConfigNodeChildren>(entity =>
        {
            entity.ToTable("ConfigNodeChildren");
            entity.HasKey(e => e.NodeId);

            // PostgreSQL 数组类型
            entity.Property(e => e.ChildrenIds)
                .HasColumnType("bigint[]")
                .IsRequired();
        });
    }
}
