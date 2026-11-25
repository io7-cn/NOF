using System;

namespace System.Diagnostics.CodeAnalysis;

// Keep the .NET Standard 2.0 shim assembly-local so it does not conflict with the platform attribute.
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
internal sealed class NotNullWhenAttribute(bool returnValue) : Attribute
{
    public bool ReturnValue { get; } = returnValue;
}
