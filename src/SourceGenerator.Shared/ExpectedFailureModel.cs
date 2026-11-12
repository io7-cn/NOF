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

    public static string OperationTypeName(IMethodSymbol method)
        => method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name + "Operation";

    public static string ResultTypeName(IMethodSymbol method) => OperationTypeName(method) + ".Result";

    public static string CaseTypeName(IMethodSymbol method, AttributeData declaration)
        => OperationTypeName(method) + "." + CaseName(declaration);

    public static string ResponseTypeName(IMethodSymbol method)
        => Declarations(method).Length > 0 ? ResultTypeName(method)
            : method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));

    public static bool AllowsNull(IMethodSymbol method)
        => Declarations(method).Length == 0 && (method.ReturnType.NullableAnnotation == NullableAnnotation.Annotated
            || method.ReturnType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T });

    public static string CaseName(AttributeData attribute)
        => attribute.ConstructorArguments.FirstOrDefault().Value as string ?? "";

    public static bool HasCustomError(AttributeData attribute)
        => attribute.AttributeConstructor?.Parameters.Length == 2
            && attribute.AttributeConstructor.Parameters[1].Type.ToDisplayString() == "System.Type";

    public static ITypeSymbol? ErrorType(AttributeData attribute)
        => HasCustomError(attribute) && attribute.ConstructorArguments.Length == 2
            ? attribute.ConstructorArguments[1].Value as ITypeSymbol : null;

    public static string ErrorTypeName(AttributeData attribute)
        => ErrorType(attribute)?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";

    public static string Code(AttributeData attribute)
        => attribute.ConstructorArguments.Length == 1 ? CaseName(attribute)
            : attribute.ConstructorArguments[1].Value as string ?? "";

    public static bool ValidName(string name) => SyntaxFacts.IsValidIdentifier(name)
        && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
        && SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.None;
}
