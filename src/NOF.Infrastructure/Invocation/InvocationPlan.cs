using NOF.Application;
using System.Runtime.CompilerServices;

namespace NOF.Infrastructure;

/// <summary>A frozen effective filter sequence and complete permitted result catalog.</summary>
public sealed class InvocationPlan
{
    /// <summary>Creates an immutable host plan.</summary>
    public InvocationPlan(ServiceOperationDescriptor operation, InvocationOrigin origin,
        IEnumerable<InvocationFilterRegistration> filters, IEnumerable<InvocationFailureCase> failures)
    {
        Operation = operation;
        Origin = origin;
        Filters = Array.AsReadOnly(filters.ToArray());
        var entries = failures.ToArray();
        if (entries.GroupBy(item => item.Id, StringComparer.Ordinal).Any(group => group.Select(item => (item.CaseType, item.ErrorType, item.ErrorCode)).Distinct().Count() != 1))
        {
            throw new InvalidOperationException("One invocation failure identity cannot describe different payloads.");
        }
        FailureCases = Array.AsReadOnly(entries.DistinctBy(item => item.CaseType).ToArray());
    }
    /// <summary>Gets the operation described by this plan.</summary>
    public ServiceOperationDescriptor Operation { get; }
    /// <summary>Gets its entry origin.</summary>
    public InvocationOrigin Origin { get; }
    /// <summary>Gets effective filters with their scope provenance.</summary>
    public IReadOnlyList<InvocationFilterRegistration> Filters { get; }
    /// <summary>Gets only failures contributed by effective filters and their Describe selections.</summary>
    public IReadOnlyList<InvocationFailureCase> FailureCases { get; }
    /// <summary>Gets the operation's statically known business results.</summary>
    public IReadOnlyList<ServiceOperationResultCase> BusinessCases => Operation.ResultCases;
    /// <summary>Validates an open result without translating expected failures into exceptions.</summary>
    public object? Validate(object? result)
    {
        InvocationResult.ValidateInitialization(result);
        var branch = InvocationResult.Value(result);
        if (branch is IInvocationFailure)
        {
            return FailureCases.Any(item => item.CaseType.IsInstanceOfType(branch)) ? result
                : throw new InvalidOperationException($"Filter failure '{branch.GetType()}' was not declared by the effective invocation plan.");
        }
        if (result is null ? Operation.AllowsNullResponse
            : Operation.ResponseType.IsInstanceOfType(result) || BusinessCases.Any(item => item.CaseType.IsInstanceOfType(result))
                || FailureCases.Any(item => item.CaseType.IsInstanceOfType(result)))
        {
            return result;
        }
        throw new InvalidOperationException($"The result of '{Operation.Id}' is outside its effective invocation contract.");
    }
}

/// <summary>Shared value validation and union presentation; it is not an outcome wrapper.</summary>
public static class InvocationResult
{
    /// <summary>Rejects uninitialized native unions even when a caller allows null business values.</summary>
    public static void ValidateInitialization(object? value)
    {
        while (value is IUnion union)
        {
            value = union.Value ?? throw new InvalidOperationException("The outcome response contains no case.");
        }
    }
    /// <summary>Reads the active native union branch for adapters and pattern matching.</summary>
    public static object? Value(object? value)
    {
        ValidateInitialization(value);
        while (value is IUnion union)
        {
            value = union.Value;
        }
        return value;
    }
}
