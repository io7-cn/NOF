using NOF.Abstraction;
using NOF.Contract;
using System.Net;
using System.Text.Json;
using Xunit;

namespace NOF.Contract.Tests;

public class ServiceExecutionTests
{
    [Fact]
    public void Completion_PreservesPlainBusinessValues_AndFailures()
    {
        var dto = new Reply("read");
        ServiceExecution completed = new ServiceCompleted(dto);
        Assert.Same(dto, completed.Require<Reply>());
        completed = new ServiceCompleted(NOF.Testing.TestOutcome.Success());
        Assert.Same(Empty.Instance, completed.Require<NOF.Testing.TestOutcome>().Value);
        Assert.Throws<InvalidOperationException>(() => ((ServiceExecution)new ServiceCompleted(default(NOF.Testing.TestOutcome))).Require<NOF.Testing.TestOutcome>());
        var failure = new Failure("business.closed", "Closed");
        completed = new ServiceCompleted(failure);
        Assert.Same(failure, completed.Require<Failure>());
        completed = new ServiceCompleted((PlainDecision)failure);
        Assert.Same(failure, completed.Require<PlainDecision>().Value);
    }

    [Fact]
    public void NullCompletion_IsValidOnlyForNullableResponses()
    {
        ServiceExecution completed = new ServiceCompleted(null);
        Assert.Null(completed.Require<Reply?>(allowNull: true));
        Assert.Throws<InvalidOperationException>(() => completed.Require<Reply>());
        Assert.Throws<InvalidOperationException>(() => completed.Require<int>(allowNull: true));
        Assert.Throws<InvalidOperationException>(() => default(ServiceExecution).Require<Reply?>(allowNull: true));
    }

    [Fact]
    public void Rejection_ThrowsForPlainResponses_AndSupportsStaticOutcomeFactories()
    {
        var failure = new Failure("403", "Forbidden");
        ServiceExecution rejected = new ServiceRejected(failure);
        Assert.Same(failure, Assert.Throws<ServiceRejectedException>(() => rejected.Require<Reply>()).Failure);
        Assert.Same(failure, Assert.Throws<ServiceRejectedException>(() => rejected.Require<NOF.Testing.TestOutcome<Reply>>()).Failure);
        Assert.Same(failure, rejected.Require<NOF.Testing.TestOutcome<Reply>>(NOF.Testing.TestOutcome<Reply>.CreateFailure).Value);
        Assert.Throws<InvalidOperationException>(() => ((ServiceExecution)new ServiceCompleted(default(NOF.Testing.TestOutcome<Reply>)))
            .Require<NOF.Testing.TestOutcome<Reply>>(NOF.Testing.TestOutcome<Reply>.CreateFailure));
        Assert.Throws<InvalidOperationException>(() => ((ServiceExecution)new ServiceCompleted(default(NOF.Testing.TestOutcome<Reply>)))
            .Require<NOF.Testing.TestOutcome<Reply>>());
    }

    public sealed record Reply(string Value);
    public union PlainDecision(Reply, Failure);
}
