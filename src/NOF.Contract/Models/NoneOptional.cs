using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>
/// A marker type representing the absence of a value in an <see cref="Optional{T}"/>.
/// This type has no members and exists solely to enable implicit conversion to <see cref="Optional{T}"/>.
/// </summary>
public readonly struct NoneOptional;
