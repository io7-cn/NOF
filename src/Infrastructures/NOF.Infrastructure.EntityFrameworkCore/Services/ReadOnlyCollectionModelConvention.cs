using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NOF.Domain;
using System.Reflection;

namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>Uses aggregate-owned fields for explicitly mapped OwnsMany collection views.</summary>
public static class ReadOnlyCollectionModelConvention
{
    /// <summary>Runs after application relationship configuration and validates conflicting navigation mappings.</summary>
    public static void Apply(IMutableModel model)
    {
        foreach (var navigation in model.GetEntityTypes().SelectMany(entity => entity.GetNavigations()))
        {
            if (!navigation.IsCollection || !navigation.ForeignKey.IsOwnership || navigation.IsOnDependent
                || navigation.PropertyInfo?.GetCustomAttribute<CollectionBackingFieldAttribute>() is not { } association)
            {
                continue;
            }
            var field = navigation.PropertyInfo.DeclaringType!.GetField(association.Name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"Missing collection field '{association.Name}' for '{navigation.Name}'.");
            if (!field.IsInitOnly || !field.IsPrivate || !field.FieldType.IsGenericType
                || field.FieldType.GetGenericTypeDefinition() is var definition && definition != typeof(List<>) && definition != typeof(HashSet<>)
                || field.FieldType.GetGenericArguments()[0] != navigation.TargetEntityType.ClrType
                || navigation.FieldInfo is { } configured && configured != field
                || navigation is IConventionNavigation convention && convention.GetPropertyAccessModeConfigurationSource() == ConfigurationSource.Explicit
                    && navigation.GetPropertyAccessMode() != PropertyAccessMode.Field)
            {
                throw new InvalidOperationException($"OwnsMany navigation '{navigation.Name}' conflicts with its read-only collection field '{association.Name}'.");
            }
            navigation.FieldInfo = field;
            navigation.SetPropertyAccessMode(PropertyAccessMode.Field);
        }
    }
}
