using Microsoft.AspNetCore.Mvc;

namespace NOF.Sample.Endpoints.Models;

/// <summary>The Sample HTTP problem preserves the structured business error.</summary>
public sealed class NodeHasChildrenProblem : ProblemDetails
{
    public const string ProblemType = "urn:nof:sample:config-node:has-children";

    public required NodeHasChildrenError Error { get; init; }
}
