using NOF.Abstraction;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;

namespace NOF.Application;

/// <summary>Identifies an execution boundary without assigning a message programming model.</summary>
public enum InvocationOrigin
{
    /// <summary>A trusted in-process caller.</summary>
    Local,
    /// <summary>An HTTP request.</summary>
    Http,
    /// <summary>A message consumer whose identity is established by its host.</summary>
    Message
}
