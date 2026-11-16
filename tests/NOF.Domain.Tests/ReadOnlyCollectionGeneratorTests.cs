using Microsoft.CodeAnalysis;
using NOF.Domain;
using NOF.Domain.SourceGenerator;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public sealed class ReadOnlyCollectionGeneratorTests
{
    [Fact]
    public void Views_AreCachedLiveAndExposeNoMutableInterface()
    {
        var aggregate = new CollectionViewAggregate();
        var items = aggregate.Items;
        var tags = aggregate.Tags;
        aggregate.Add(1);
        aggregate.Add(2);
        Assert.Same(items, aggregate.Items);
        Assert.Same(tags, aggregate.Tags);
        Assert.Equal([1, 2], items);
        Assert.Equal(2, tags.Count);
        Assert.False(items is ICollection<int>);
        Assert.False(tags is ICollection<int>);
        Assert.Equal("_items", typeof(CollectionViewAggregate).GetProperty(nameof(CollectionViewAggregate.Items))!
            .GetCustomAttributes(typeof(CollectionBackingFieldAttribute), false).Cast<CollectionBackingFieldAttribute>().Single().Name);
    }

    [Theory]
    [InlineData("private readonly List<int> _items = new();", "public class Aggregate", "NOF021")]
    [InlineData("private readonly int[] _items = new int[0];", "public partial class Aggregate", "NOF020")]
    [InlineData("public readonly List<int> _items = new();", "public partial class Aggregate", "NOF020")]
    [InlineData("private List<int> _items = new();", "public partial class Aggregate", "NOF020")]
    [InlineData("private static readonly List<int> _items = new();", "public partial class Aggregate", "NOF020")]
    public void InvalidDeclarations_ReportCompileTimeDiagnostics(string field, string declaration, string diagnostic)
    {
        var source = "using NOF.Domain; using System.Collections.Generic; " + declaration + " { [ReadOnlyCollection] " + field + " }";
        var result = new ReadOnlyCollectionGenerator().GetResultPostGen(source, typeof(ReadOnlyCollectionAttribute));
        Assert.Contains(result.Diagnostics, item => item.Id == diagnostic && item.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("invalid-name")]
    [InlineData("123")]
    [InlineData("Existing")]
    public void InvalidOrConflictingNames_ReportDiagnostics(string name)
    {
        var source = "using NOF.Domain; using System.Collections.Generic; public partial class Aggregate { public int Existing { get; } [ReadOnlyCollection(\"" + name + "\")] private readonly List<int> _items = new(); }";
        var result = new ReadOnlyCollectionGenerator().GetResultPostGen(source, typeof(ReadOnlyCollectionAttribute));
        Assert.Contains(result.Diagnostics, item => item.Id == "NOF020");
    }
}

public partial class CollectionViewAggregate
{
    [ReadOnlyCollection]
    private readonly List<int> _items = [];
    [ReadOnlyCollection("Tags")]
    private readonly HashSet<int> _tagValues = [];
    public void Add(int value)
    {
        _items.Add(value);
        _tagValues.Add(value);
    }
}
