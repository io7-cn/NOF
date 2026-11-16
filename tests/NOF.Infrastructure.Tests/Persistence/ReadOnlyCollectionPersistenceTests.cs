using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NOF.Domain;
using NOF.Infrastructure.EntityFrameworkCore;
using Xunit;

namespace NOF.Infrastructure.Tests.Persistence;

public sealed class ReadOnlyCollectionPersistenceTests
{
    [Fact]
    public async Task OwnsMany_UsesPrivateFields_ForMaterializationAndTracking()
    {
        var options = new DbContextOptionsBuilder<CollectionDbContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCustomizer, NOFModelCustomizer>().Options;
        await using var db = new CollectionDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var entity = new CollectionOwner { Id = 1 };
        entity.Add("file", "tag");
        db.Add(entity);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Set<CollectionOwner>());
        Assert.Equal("file", Assert.Single(loaded.Files).Value);
        Assert.Equal("tag", Assert.Single(loaded.Tags).Value);
        var view = loaded.Files;
        loaded.Add("second", "next");
        loaded.RemoveFirst();
        Assert.Single(view);
        Assert.Equal("second", Assert.Single(view).Value);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var verified = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Set<CollectionOwner>());
        Assert.Equal("second", Assert.Single(verified.Files).Value);
        Assert.Equal(2, verified.Tags.Count);
        foreach (var navigation in db.Model.FindEntityType(typeof(CollectionOwner))!.GetNavigations())
        {
            Assert.Equal(PropertyAccessMode.Field, navigation.GetPropertyAccessMode());
            Assert.NotNull(navigation.FieldInfo);
        }
    }

    [Fact]
    public void ExplicitPropertyAccessConflict_IsReportedDuringModelConstruction()
    {
        var options = new DbContextOptionsBuilder<ConflictingCollectionDbContext>().UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCustomizer, NOFModelCustomizer>().Options;
        using var db = new ConflictingCollectionDbContext(options);
        var exception = Assert.Throws<InvalidOperationException>(() => db.Model);
        Assert.Contains("conflicts", exception.Message);
    }
}

public partial class CollectionOwner
{
    public int Id { get; set; }
    private readonly List<CollectionFile> _files = [];
    private readonly HashSet<CollectionTag> _tags = new(ReferenceEqualityComparer.Instance);
    [CollectionBackingField(nameof(_files))]
    public IReadOnlyCollection<CollectionFile> Files => new ReadOnlyCollectionView<CollectionFile>(_files);
    [CollectionBackingField(nameof(_tags))]
    public IReadOnlyCollection<CollectionTag> Tags => new ReadOnlyCollectionView<CollectionTag>(_tags);
    public void Add(string file, string tag)
    {
        _files.Add(new() { Value = file });
        _tags.Add(new() { Value = tag });
    }
    public void RemoveFirst() => _files.RemoveAt(0);
}
public sealed class CollectionFile { public int Id { get; set; } public string Value { get; set; } = ""; }
public sealed class CollectionTag { public int Id { get; set; } public string Value { get; set; } = ""; }
public class CollectionDbContext(DbContextOptions options) : NOFDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<CollectionOwner>(entity =>
        {
            entity.HasKey(owner => owner.Id);
            entity.OwnsMany(owner => owner.Files, files => files.HasKey(file => file.Id));
            entity.OwnsMany(owner => owner.Tags, tags => tags.HasKey(tag => tag.Id));
        });
    }
}
public sealed class ConflictingCollectionDbContext(DbContextOptions<ConflictingCollectionDbContext> options) : CollectionDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<CollectionOwner>().Navigation(owner => owner.Files).UsePropertyAccessMode(PropertyAccessMode.Property);
    }
}
