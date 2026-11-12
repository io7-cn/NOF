using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;

namespace NOF.SourceGeneration;

// Internal because this file is linked into multiple analyzer assemblies; public copies would expose conflicting type identities.
internal static class ExpectedFailureModel
{
    public static bool IsDeclaration(AttributeData attribute)
        => attribute.AttributeClass is { } type && type.ContainingNamespace.ToDisplayString() == "NOF.Contract"
            && type.Name == "FailureCaseAttribute";

    public static AttributeData[] Declarations(IMethodSymbol method) => method.GetAttributes().Where(IsDeclaration).ToArray();

    public static string ResultTypeName(IMethodSymbol method)
        => method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name + "Result";

    public static string ResponseTypeName(IMethodSymbol method)
        => Declarations(method).Length > 0 ? ResultTypeName(method)
            : method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));

    public static bool AllowsNull(IMethodSymbol method)
        => Declarations(method).Length == 0 && (method.ReturnType.NullableAnnotation == NullableAnnotation.Annotated
            || method.ReturnType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T });

    public static string Code(AttributeData attribute) => attribute.ConstructorArguments.FirstOrDefault().Value as string ?? "";

    public static string? ExplicitCaseName(AttributeData attribute)
        => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "Name").Value.Value as string;

    public static string CaseName(AttributeData attribute) => ExplicitCaseName(attribute) ?? "Failed";

    public static string ErrorTypeName(AttributeData attribute)
        => attribute.AttributeClass is { Arity: 1 } type
            ? type.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : "global::NOF.Contract.Error";

    public static bool ValidName(string name) => SyntaxFacts.IsValidIdentifier(name)
        && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
        && SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.None;
}
