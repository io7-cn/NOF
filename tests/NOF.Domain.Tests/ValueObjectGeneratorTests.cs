using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NOF.Abstraction;
using NOF.Domain;
using NOF.Domain.SourceGenerator;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public class ValueObjectGeneratorTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static readonly Type[] _extraRefs =
    [
        typeof(IValueObject<>),
        typeof(Failure),
        typeof(NewableValueObjectAttribute),
        typeof(ValueObjectLengthAttribute),
        typeof(IdGenerator),
        typeof(JsonConverterAttribute),
    ];

    private static GeneratorDriverRunResult RunGenerator(string source)
        => new ValueObjectGenerator().GetResultPostGen(source, _extraRefs);

    private static string GetVoCode(GeneratorDriverRunResult result)
        => result.GeneratedTrees
            .Select(t => t.GetText().ToString())
            .Single();

    private static Assembly CompileGeneratedAssembly(string source)
    {
        var compilation = CSharpCompilation.CreateCompilation(
            $"ValueObjectRuntimeTests_{Guid.NewGuid():N}",
            source,
            true,
            _extraRefs);
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ValueObjectGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        using var stream = new MemoryStream();
        var emitResult = outputCompilation.Emit(stream);
        Assert.True(
            emitResult.Success,
            $"Compilation failed: {string.Join(Environment.NewLine, emitResult.Diagnostics)}");
        return Assembly.Load(stream.ToArray());
    }

    private static void AssertUninitialized(Action action)
    {
        var exception = Assert.ThrowsAny<Exception>(action);
        while (exception is TargetInvocationException { InnerException: not null } invocationException)
        {
            exception = invocationException.InnerException;
        }

        var invalidOperationException = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("is uninitialized", invalidOperationException.Message, StringComparison.Ordinal);
        Assert.Contains(".From(...)", invalidOperationException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromAndTryFrom_NormalizeOnceAndRejectExpectedInvalidInputWithoutExceptions()
    {
        var assembly = CompileGeneratedAssembly("""
            using NOF.Domain;
            public readonly partial struct Name : IValueObject<string>
            {
                public static int NormalizeCalls;
                public static int ValidityCalls;
                public static string Normalize(string value)
                {
                    NormalizeCalls++;
                    return value == "null!" ? null! : value.Trim().ToLowerInvariant();
                }
                public static bool IsValid(string value)
                {
                    ValidityCalls++;
                    return value.Length > 0 && value != "invalid";
                }
            }
            public static class Probe
            {
                public static bool Run()
                {
                    var value = Name.From("  HELLO  ");
                    if ((string)value != "hello" || Name.NormalizeCalls != 1 || Name.ValidityCalls != 1) { return false; }
                    if (!Name.TryFrom(" WORLD ", out var result) || (string)result != "world"
                        || Name.NormalizeCalls != 2 || Name.ValidityCalls != 2) { return false; }
                    if (Name.TryFrom(null, out result) || Name.NormalizeCalls != 2) { return false; }
                    if (Name.TryFrom("null!", out result) || Name.ValidityCalls != 2) { return false; }
                    if (Name.TryFrom("invalid", out result)) { return false; }
                    try { _ = (string)result; return false; }
                    catch (System.InvalidOperationException) { }
                    try { Name.From("invalid"); return false; }
                    catch (System.ArgumentException error) { if (error.ParamName != "value") { return false; } }
                    try { Name.From(null!); return false; }
                    catch (System.ArgumentNullException) { }
                    return typeof(Name).GetMethod("Of") == null;
                }
            }
            """);
        Assert.True((bool)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!);
    }

    [Fact]
    public void LengthConstraints_AreAppliedToNormalizedValues_BeforeCustomValidity()
    {
        var assembly = CompileGeneratedAssembly("""
            using NOF.Domain;
            [ValueObjectLength(4, MinimumLength = 2)]
            public readonly partial struct Name : IValueObject<string>
            {
                public static int ValidityCalls;
                public static string Normalize(string value) => value.Trim();
                public static bool IsValid(string value) { ValidityCalls++; return true; }
            }
            public static class Probe
            {
                public static bool Run()
                {
                    if (Name.TryFrom(" a ", out _) || Name.TryFrom("12345", out _) || Name.ValidityCalls != 0) { return false; }
                    if (!Name.TryFrom(" abcd ", out var name) || (string)name != "abcd" || Name.ValidityCalls != 1) { return false; }
                    try { Name.From("abcde"); return false; }
                    catch (System.ArgumentException) { return Name.ValidityCalls == 1; }
                }
            }
            """);
        Assert.True((bool)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!);
    }

    [Theory]
    [InlineData("Normalize", false)]
    [InlineData("Normalize", true)]
    [InlineData("IsValid", false)]
    [InlineData("IsValid", true)]
    public void UserHookExceptions_PropagateUnchanged(string hook, bool useTry)
    {
        var assembly = CompileGeneratedAssembly($$"""
            using NOF.Domain;
            public readonly partial struct Name : IValueObject<string>
            {
                public static readonly System.Exception HookError = new System.InvalidOperationException("callback failed");
                public static string Normalize(string value) => {{(hook == "Normalize" ? "throw HookError" : "value")}};
                public static bool IsValid(string value) => {{(hook == "IsValid" ? "throw HookError" : "true")}};
            }
            public static class Probe
            {
                public static bool Run()
                {
                    try { {{(useTry ? "Name.TryFrom(nameof(Name), out _);" : "Name.From(nameof(Name));")}} return false; }
                    catch (System.Exception error) { return object.ReferenceEquals(Name.HookError, error); }
                }
            }
            """);
        Assert.True((bool)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!);
    }

    [Fact]
    public void NullablePrimitiveFactories_PreserveAbsenceAndResetFailedOutput()
    {
        var assembly = CompileGeneratedAssembly("""
            using NOF.Domain;
            public readonly partial struct Positive : IValueObject<int>
            {
                public static bool IsValid(int value) => value > 0;
            }
            public static class Probe
            {
                public static bool Run()
                {
                    if (Positive.From((int?)null) != null || Positive.TryFrom((int?)null, out _)) { return false; }
                    if (!Positive.TryFrom((int?)42, out var value) || (int)value != 42) { return false; }
                    if (Positive.TryFrom(-1, out value)) { return false; }
                    try { _ = (int)value; return false; }
                    catch (System.InvalidOperationException) { return (int)Positive.From(1) == 1; }
                }
            }
            """);
        Assert.True((bool)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!);
    }

    [Fact]
    public void ValueObjectLengthAttribute_ValidatesMinimumLength()
    {
        var attribute = new ValueObjectLengthAttribute(10)
        {
            MinimumLength = 2,
        };

        Assert.Equal(2, attribute.MinimumLength);
        Assert.Throws<ArgumentOutOfRangeException>(() => attribute.MinimumLength = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => attribute.MinimumLength = 11);
    }

    private static (GeneratorDriverRunResult Result, IReadOnlyList<Diagnostic> Diagnostics)
        RunGeneratorWithDiagnostics(string source)
    {
        var compilation = CSharpCompilation.CreateCompilation("TestAssembly", source, true, _extraRefs);

        var driver = CSharpGeneratorDriver.Create([new ValueObjectGenerator().AsSourceGenerator()], parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        var result = driver.GetRunResult();
        var diagnostics = result.Results.SelectMany(r => r.Diagnostics).ToList();
        return (result, diagnostics);
    }

    // -----------------------------------------------------------------------
    // NOF010 鈥?must be partial
    // -----------------------------------------------------------------------

    [Fact]
    public void NonPartialStruct_EmitsNOF010_AndNoSource()
    {
        const string source = """
            using NOF.Domain;
            namespace Test
            {
                public readonly struct NotPartial : IValueObject<string> { }
            }
            """;

        var (result, diagnostics) = RunGeneratorWithDiagnostics(source);

        Assert.Single(diagnostics, d => d.Id == "NOF010");
        Assert.Empty(
        result.GeneratedTrees);
    }

    // -----------------------------------------------------------------------
    // NOF012 鈥?[NewableValueObject] only on ValueObject<long>
    // -----------------------------------------------------------------------

    [Fact]
    public void NewableOnStringVo_EmitsNOF012_AndNoSource()
    {
        const string source = """
            using NOF.Domain;
            namespace Test
            {
                [NewableValueObject]
                public readonly partial struct MyVo : IValueObject<string> { }
            }
            """;

        var (result, diagnostics) = RunGeneratorWithDiagnostics(source);

        Assert.Single(diagnostics, d => d.Id == "NOF012");
        Assert.Empty(
        result.GeneratedTrees);
    }

    [Fact]
    public void NewableOnIntVo_EmitsNOF012_AndNoSource()
    {
        const string source = """
            using NOF.Domain;
            namespace Test
            {
                [NewableValueObject]
                public readonly partial struct MyVo : IValueObject<int> { }
            }
            """;

        var (result, diagnostics) = RunGeneratorWithDiagnostics(source);

        Assert.Single(diagnostics, d => d.Id == "NOF012");
        Assert.Empty(
        result.GeneratedTrees);
    }

    // -----------------------------------------------------------------------
    // Happy path 鈥?basic generation
    // -----------------------------------------------------------------------

    [Fact]
    public void StringVo_GeneratesExpectedMembers()
    {
        const string source = """
            #nullable enable
            using NOF.Domain;
            namespace Test
            {
                public readonly partial struct Name : IValueObject<string> { }
            }
            """;

        var result = RunGenerator(source);

        Assert.Single(result.GeneratedTrees);
        var code = GetVoCode(result);

        Assert.Contains("public static Name From(string value)", code);
        Assert.Contains("private readonly bool _isInitialized;", code);
        Assert.Contains("public Name()", code);
        Assert.Contains("private void __EnsureInitialized()", code);
        Assert.Contains("global::System.ArgumentNullException.ThrowIfNull(value)", code);
        Assert.Contains("public static explicit operator string(Name vo)", code);
        Assert.Contains("public static bool operator ==(Name left, Name right)", code);
        Assert.Contains("public static bool operator !=(Name left, Name right)", code);
        Assert.Contains("public sealed class __JsonConverter", code);
        Assert.Contains("private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<string> __GetPrimitiveJsonTypeInfo", code);
        Assert.Contains("JsonSerializer.Deserialize(ref reader, __GetPrimitiveJsonTypeInfo(options))", code);
        Assert.Contains("JsonSerializer.Serialize(writer, value._value, __GetPrimitiveJsonTypeInfo(options))", code);
        Assert.Contains("public override string ToString()", code);
        Assert.DoesNotContain("From(string? value)", code); // no nullable overload for ref types
        Assert.DoesNotContain("public static Name New()", code);
        Assert.DoesNotContain("public override string? ToString()", code);
    }

    [Fact]
    public void DefaultValueObject_CannotMasqueradeAsValidatedValue_OrExposePrimitive()
    {
        const string source = """
            using NOF.Domain;
            namespace Test;

            public readonly partial struct Score : IValueObject<int>;
            """;

        var assembly = CompileGeneratedAssembly(source);
        var valueObjectType = Assert.IsAssignableFrom<Type>(assembly.GetType("Test.Score"));
        var factory = Assert.IsAssignableFrom<MethodInfo>(
            valueObjectType.GetMethod("From", BindingFlags.Public | BindingFlags.Static, [typeof(int)]));
        var explicitCast = Assert.Single(
            valueObjectType.GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.Name == "op_Explicit" && method.ReturnType == typeof(int));

        // Array allocation is an implicit zero-initialization path that source analysis cannot
        // diagnose without creating noise. It exercises the runtime guard directly.
        var uninitialized = Assert.IsAssignableFrom<object>(
            Array.CreateInstance(valueObjectType, 1).GetValue(0));
        var validPrimitiveDefault = Assert.IsAssignableFrom<object>(factory.Invoke(null, [0]));

        Assert.Equal(0, explicitCast.Invoke(null, [validPrimitiveDefault]));
        Assert.Equal("0", validPrimitiveDefault.ToString());
        Assert.True(validPrimitiveDefault.Equals(validPrimitiveDefault));
        Assert.Equal("0", JsonSerializer.Serialize(validPrimitiveDefault, valueObjectType));

        AssertUninitialized(() => explicitCast.Invoke(null, [uninitialized]));
        Assert.True(uninitialized.Equals(uninitialized));
        Assert.False(uninitialized.Equals(validPrimitiveDefault));
        Assert.False(validPrimitiveDefault.Equals(uninitialized));
        AssertUninitialized(() => uninitialized.GetHashCode());
        AssertUninitialized(() => uninitialized.ToString());
        AssertUninitialized(() => JsonSerializer.Serialize(uninitialized, valueObjectType));
        AssertUninitialized(() => Activator.CreateInstance(valueObjectType));
    }

    [Fact]
    public void ValueTypeVo_DoesNotEmitNullGuard()
    {
        // Value types (int, long, etc.) never need a null guard
        const string source = """
            #nullable enable
            using NOF.Domain;
            namespace Test
            {
                public readonly partial struct Score : IValueObject<int> { }
            }
            """;

        var result = RunGenerator(source);

        var code = GetVoCode(result);
        Assert.DoesNotContain("ArgumentNullException.ThrowIfNull", code);
    }

    [Fact]
    public void LongVo_GeneratesNullableOverload_AndNoNullGuard()
    {
        const string source = """
            using NOF.Domain;
            namespace Test
            {
                public readonly partial struct OrderId : IValueObject<long> { }
            }
            """;

        var result = RunGenerator(source);

        var code = GetVoCode(result);
        Assert.Contains("public static OrderId? From(long? value)", code);
        Assert.Contains("value.HasValue ? From(value.Value) : null", code);
        Assert.DoesNotContain("ArgumentNullException.ThrowIfNull", code);
        Assert.Contains("_value.GetHashCode()", code);   // value type 鈥?no ?.
        Assert.Contains("public override string ToString()", code);
        Assert.Contains("_value.ToString() ?? string.Empty", code);
    }

    [Fact]
    public void NullableReferenceUnderlyingType_EmitsNOF011_ButStillGeneratesSource()
    {
        const string source = """
            #nullable enable
            using NOF.Domain;
            namespace Test
            {
                public readonly partial struct Name : IValueObject<string?> { }
            }
            """;

        var (result, diagnostics) = RunGeneratorWithDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id == "NOF011" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Single(result.GeneratedTrees);
    }

    [Fact]
    public void NullableValueUnderlyingType_EmitsNOF011_ButStillGeneratesSource()
    {
        const string source = """
            #nullable enable
            using NOF.Domain;
            namespace Test
            {
                public readonly partial struct Score : IValueObject<int?> { }
            }
            """;

        var (result, diagnostics) = RunGeneratorWithDiagnostics(source);

        Assert.Contains(diagnostics, d => d.Id == "NOF011" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Single(result.GeneratedTrees);
    }

    [Fact]
    public void AlwaysCallsIsValidInTryFrom()
    {
        // IsValid is a static virtual hook called once after normalization.
        const string source = """
            using NOF.Domain;
            namespace Test
            {
                public readonly partial struct OrderId : IValueObject<long> { }
            }
            """;

        var result = RunGenerator(source);

        var code = GetVoCode(result);
        Assert.Contains("value = __CallNormalize<OrderId>(value);", code);
        Assert.Contains("__CallIsValid<OrderId>(value)", code);
    }

    [Fact]
    public void UserOverriddenIsValid_IsCalledInTryFrom()
    {
        const string source = """
            using NOF.Domain;
            namespace Test
            {
                public readonly partial struct OrderId : IValueObject<long>
                {
                    public static bool IsValid(long value) => value > 0;
                }
            }
            """;

        var result = RunGenerator(source);

        var code = GetVoCode(result);
        Assert.Contains("value = __CallNormalize<OrderId>(value);", code);
        Assert.Contains("__CallIsValid<OrderId>(value)", code);
    }

    [Fact]
    public void Normalize_IsCalledBeforeIsValid()
    {
        const string source = """
            #nullable enable
            using NOF.Domain;
            namespace Test
            {
                public readonly partial struct Name : IValueObject<string>
                {
                    public static string Normalize(string value) => value.Trim();
                }
            }
            """;

        var result = RunGenerator(source);
        var code = GetVoCode(result);

        var normalizeIdx = code.IndexOf("__CallNormalize<Name>(value);", StringComparison.Ordinal);
        var validateIdx = code.IndexOf("__CallIsValid<Name>(value)", StringComparison.Ordinal);
        Assert.True(normalizeIdx >= 0);
        Assert.True(validateIdx > normalizeIdx);
    }

    [Fact]
    public void NewableOnLongVo_GeneratesNewMethod()
    {
        const string source = """
            using NOF.Domain;
            namespace Test
            {
                [NewableValueObject]
                public readonly partial struct EntityId : IValueObject<long> { }
            }
            """;

        var result = RunGenerator(source);

        var code = GetVoCode(result);
        Assert.Contains("public static EntityId New(global::NOF.Domain.IIdGenerator generator)", code);
        Assert.Contains("global::System.ArgumentNullException.ThrowIfNull(generator);", code);
        Assert.Contains("return From(generator.NextId());", code);
        Assert.Contains("public static EntityId New()", code);
        Assert.Contains("=> New(global::NOF.Domain.IdGenerator.Current);", code);
    }

    [Fact]
    public void StringVo_NullGuardBeforeNormalizationAndIsValid()
    {
        const string source = """
            #nullable enable
            using NOF.Domain;
            namespace Test
            {
                public readonly partial struct Tag : IValueObject<string> { }
            }
            """;

        var result = RunGenerator(source);
        var code = GetVoCode(result);

        // null guard must come before Normalize/IsValid
        var nullGuardIdx = code.IndexOf("ArgumentNullException.ThrowIfNull", StringComparison.Ordinal);
        var normalizeIdx = code.IndexOf("__CallNormalize<Tag>(value);", StringComparison.Ordinal);
        var validateIdx = code.IndexOf("__CallIsValid<Tag>(value)", StringComparison.Ordinal);
        Assert.True(nullGuardIdx < normalizeIdx);
        Assert.True(normalizeIdx < validateIdx);
    }

    [Fact]
    public void StringVoWithLength_GeneratesLengthValidationAfterNormalize()
    {
        const string source = """
            using NOF.Domain;
            namespace Test
            {
                [ValueObjectLength(100)]
                public readonly partial struct Name : IValueObject<string> { }
            }
            """;

        var result = RunGenerator(source);
        var code = GetVoCode(result);

        var normalizeIdx = code.IndexOf("__CallNormalize<Name>(value);", StringComparison.Ordinal);
        var lengthIdx = code.IndexOf("if (value.Length > 100)", StringComparison.Ordinal);
        var validateIdx = code.IndexOf("__CallIsValid<Name>(value)", StringComparison.Ordinal);

        Assert.True(normalizeIdx >= 0);
        Assert.True(lengthIdx > normalizeIdx);
        Assert.True(validateIdx > lengthIdx);
        Assert.Contains("if (value.Length > 100) { return false; }", code);
    }

    [Fact]
    public void StringVoWithMinimumLength_GeneratesRangeValidationAfterNormalize()
    {
        const string source = """
            using NOF.Domain;
            namespace Test
            {
                [ValueObjectLength(100, MinimumLength = 3)]
                public readonly partial struct Name : IValueObject<string> { }
            }
            """;

        var result = RunGenerator(source);
        var code = GetVoCode(result);

        var normalizeIdx = code.IndexOf("__CallNormalize<Name>(value);", StringComparison.Ordinal);
        var minimumIdx = code.IndexOf("if (value.Length < 3)", StringComparison.Ordinal);
        var maximumIdx = code.IndexOf("if (value.Length > 100)", StringComparison.Ordinal);
        var validateIdx = code.IndexOf("__CallIsValid<Name>(value)", StringComparison.Ordinal);

        Assert.True(normalizeIdx >= 0);
        Assert.True(minimumIdx > normalizeIdx);
        Assert.True(maximumIdx > minimumIdx);
        Assert.True(validateIdx > maximumIdx);
        Assert.Contains("if (value.Length < 3) { return false; }", code);
    }

    [Fact]
    public void LengthOnNonStringValueObject_EmitsNOF016_AndNoSource()
    {
        const string source = """
            using NOF.Domain;
            namespace Test
            {
                [ValueObjectLength(10)]
                public readonly partial struct Score : IValueObject<int> { }
            }
            """;

        var (result, diagnostics) = RunGeneratorWithDiagnostics(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "NOF016" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void LengthOnNullableStringValueObject_EmitsNOF016_AndNoSource()
    {
        const string source = """
            #nullable enable
            using NOF.Domain;
            namespace Test
            {
                [ValueObjectLength(10)]
                public readonly partial struct Name : IValueObject<string?> { }
            }
            """;

        var (result, diagnostics) = RunGeneratorWithDiagnostics(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "NOF016" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void LengthOnNonValueObject_EmitsNOF016_AndNoSource()
    {
        const string source = """
            using NOF.Domain;
            namespace Test
            {
                [ValueObjectLength(10)]
                public readonly struct Name { }
            }
            """;

        var (result, diagnostics) = RunGeneratorWithDiagnostics(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "NOF016" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.GeneratedTrees);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveLength_EmitsNOF017_AndNoSource(int maximumLength)
    {
        var source = $$"""
            using NOF.Domain;
            namespace Test
            {
                [ValueObjectLength({{maximumLength}})]
                public readonly partial struct Name : IValueObject<string> { }
            }
            """;

        var (result, diagnostics) = RunGeneratorWithDiagnostics(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "NOF017" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.GeneratedTrees);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(11, 10)]
    public void InvalidMinimumLength_EmitsNOF017_AndNoSource(int minimumLength, int maximumLength)
    {
        var source = $$"""
            using NOF.Domain;
            namespace Test
            {
                [ValueObjectLength({{maximumLength}}, MinimumLength = {{minimumLength}})]
                public readonly partial struct Name : IValueObject<string> { }
            }
            """;

        var (result, diagnostics) = RunGeneratorWithDiagnostics(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "NOF017" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.GeneratedTrees);
    }
}
