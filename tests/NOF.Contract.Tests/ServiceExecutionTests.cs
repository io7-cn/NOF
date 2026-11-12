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
        Assert.Same(failure, Assert.Throws<ServiceRejectedException>(() => rejected.Require<Result<Reply>>()).Failure);
        Assert.Same(failure, rejected.Require<Result<Reply>>(Result<Reply>.CreateFailure).Value);
        Assert.Throws<InvalidOperationException>(() => ((ServiceExecution)new ServiceCompleted(default(Result<Reply>)))
            .Require<Result<Reply>>(Result<Reply>.CreateFailure));
        Assert.Throws<InvalidOperationException>(() => ((ServiceExecution)new ServiceCompleted(default(Result<Reply>)))
            .Require<Result<Reply>>());
    }

    [Fact]
    public async Task HttpValueReader_ReadsPlainDto_AndDistinguishesNullFromMissingValue()
    {
        using var dto = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"Value\":\"read\"}") };
        var typeInfo = JsonSerializerOptions.Default.GetRequiredTypeInfo<Reply>();
        Assert.Equal(new Reply("read"), await HttpRpcTransportResultReader.ReadValueAsync(dto, typeInfo, default));
        using var missing = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null") };
        Assert.Null(await HttpRpcTransportResultReader.ReadValueAsync(missing, typeInfo, default, allowNull: true));
        using var invalid = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => HttpRpcTransportResultReader.ReadValueAsync(invalid, typeInfo, default));
    }

    [Fact]
    public async Task HttpValueReader_PreservesPolicyFailureDetails()
    {
        using var denied = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"$kind\":\"failure\",\"code\":\"tenant.denied\",\"message\":\"Forbidden\"}")
        };
        var error = await Assert.ThrowsAsync<ServiceRejectedException>(() => HttpRpcTransportResultReader.ReadValueAsync(
            denied, JsonSerializerOptions.Default.GetRequiredTypeInfo<Reply>(), default));
        Assert.Equal("tenant.denied", error.Failure.Code);
        Assert.Equal("Forbidden", error.Failure.Message);
    }

    [Fact]
    public async Task HttpValueReader_DoesNotInventDefaultValuesForEmptyBodies()
    {
        using var empty = new HttpResponseMessage(HttpStatusCode.NoContent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => HttpRpcTransportResultReader.ReadValueAsync(
            empty, JsonSerializerOptions.Default.GetRequiredTypeInfo<int>(), default));
    }

    public sealed record Reply(string Value);
    public union PlainDecision(Reply, Failure);
}
