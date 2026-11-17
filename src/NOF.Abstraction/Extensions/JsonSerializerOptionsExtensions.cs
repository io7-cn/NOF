using NOF.Abstraction;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace System.Text.Json;

/// <summary>
/// Extension methods for the NOF.Abstraction layer.
/// </summary>
public static partial class NOFAbstractionExtensions
{
    extension(JsonSerializerOptions options)
    {
        /// <summary>Writes long values, including nullable and collection elements, as decimal strings.</summary>
        /// <remarks>Integer tokens remain accepted on input. Other numeric types are unchanged. Configure before first use.</remarks>
        public JsonSerializerOptions UseStringInt64()
        {
            if (!options.Converters.Any(converter => converter is JsonStringInt64Converter))
            {
                options.Converters.Insert(0, new JsonStringInt64Converter());
            }
            return options;
        }

        /// <summary>Writes all enums as strings using native enum names, attributes and optional naming policy.</summary>
        /// <remarks>Numeric enum values are rejected by default. For Native AOT, use the generic overload for each enum.</remarks>
        [RequiresDynamicCode("Global enum conversion requires runtime generic code. Use UseStringEnums<TEnum> for Native AOT.")]
        public JsonSerializerOptions UseStringEnums(JsonNamingPolicy? namingPolicy = null, bool allowIntegerValues = false)
        {
            options.Converters.Insert(0, new JsonStringEnumConverter(namingPolicy, allowIntegerValues));
            return options;
        }

        /// <summary>Enables native string conversion for one enum without requiring runtime code generation.</summary>
        /// <remarks>Numeric enum values are rejected by default. Configure before first use.</remarks>
        public JsonSerializerOptions UseStringEnums<TEnum>(JsonNamingPolicy? namingPolicy = null, bool allowIntegerValues = false)
            where TEnum : struct, Enum
        {
            options.Converters.Insert(0, new JsonStringEnumConverter<TEnum>(namingPolicy, allowIntegerValues));
            return options;
        }

        /// <summary>Creates independent JSON options with framework primitive metadata.</summary>
        public static JsonSerializerOptions CreateNOF()
        {
            var created = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                TypeInfoResolver = NOFJsonSerializerContext.Default
            };
            if (JsonSerializer.IsReflectionEnabledByDefault)
            {
#pragma warning disable IL2026, IL3050
                created.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
#pragma warning restore IL2026, IL3050
            }
            return created;
        }

        /// <summary>
        /// Gets the source-generated <see cref="JsonTypeInfo{T}"/> for <typeparamref name="T"/> from the current
        /// options instance.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no metadata for <typeparamref name="T"/> is available in the current resolver chain.
        /// </exception>
        public JsonTypeInfo<T> GetRequiredTypeInfo<T>()
        {
            return options.GetRequiredTypeInfo(typeof(T)) as JsonTypeInfo<T>
                ?? throw CreateMissingMetadataException(typeof(T));
        }

        /// <summary>
        /// Gets the source-generated <see cref="JsonTypeInfo"/> for the specified runtime type from the current
        /// options instance.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no metadata for <paramref name="runtimeType"/> is available in the current resolver chain.
        /// </exception>
        public JsonTypeInfo GetRequiredTypeInfo(Type runtimeType)
        {
            ArgumentNullException.ThrowIfNull(runtimeType);

            try
            {
                return options.GetTypeInfo(runtimeType);
            }
            catch (NotSupportedException ex)
            {
                throw CreateMissingMetadataException(runtimeType, ex);
            }
        }

    }

    private static InvalidOperationException CreateMissingMetadataException(Type runtimeType, Exception? innerException = null)
    {
        return new InvalidOperationException(
            $"NOF JSON metadata for '{runtimeType.FullName}' was not found. " +
            $"Register a source-generated JsonSerializerContext on this host's JsonSerializerOptions before the type is serialized or deserialized.",
            innerException);
    }
}
