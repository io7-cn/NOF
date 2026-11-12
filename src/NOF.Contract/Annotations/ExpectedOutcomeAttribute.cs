using System.ComponentModel;

namespace NOF.Contract;

/// <summary>Identifies generated closed operation outcomes for checked-result analysis.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class ExpectedOutcomeAttribute : Attribute;
