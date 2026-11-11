using NOF.Contract;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Xunit;

namespace NOF.Hosting.Tests;

public sealed class HttpRpcTransportResultReaderTests
{
    [Fact]
    public async Task ReadFailureAsync_TaggedFailure_PreservesBusinessCodeAndMessage()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"kind":"failure","error":{"code":"permission.denied","message":"Access denied"}}""", Encoding.UTF8, "application/json")
        };
        var result = await HttpRpcTransportResultReader.ReadFailureAsync<Result<TokenResponse>>(response, CancellationToken.None);
        Assert.Equal("permission.denied", result.ExpectFailure().Code);
        Assert.Equal("Access denied", result.ExpectFailure().Message);
    }

    [Fact]
    public async Task ReadAsync_EmptySuccess_RequiresDataForTypedResult()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => HttpRpcTransportResultReader.ReadAsync(
            response, CreateTypeInfo<Result<TokenResponse>>(), CancellationToken.None));
        Assert.NotNull((await HttpRpcTransportResultReader.ReadAsync(response, CreateTypeInfo<Result>(), CancellationToken.None)).ExpectSuccess());
    }

    [Fact]
    public async Task ReadAsync_WhenHttp200ContainsBusinessFailure_PreservesFailure()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"kind":"failure","error":{"code":"conflict","message":"Already approved"}}""",
                Encoding.UTF8,
                "application/json")
        };

        var result = await HttpRpcTransportResultReader.ReadAsync(
            response,
            CreateTypeInfo<Result<TokenResponse>>(),
            CancellationToken.None);

        Assert.NotNull(result.ExpectFailure());
        Assert.Equal("conflict", result.ExpectFailure().Code);

    }

    [Fact]
    public async Task ReadAsync_WhenResponseSucceeds_ReturnsBusinessResult()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"kind":"success","data":{"token":"abc"},"message":""}""",
                Encoding.UTF8,
                "application/json")
        };

        var result = await HttpRpcTransportResultReader.ReadAsync(
            response,
            CreateTypeInfo<Result<TokenResponse>>(),
            CancellationToken.None);

        Assert.NotNull(result.ExpectSuccess());
        Assert.Equal("abc", result.ExpectSuccess().Data!.Token);
    }

    [Fact]
    public async Task ReadFailureAsync_WhenResponseIsOAuthError_ReturnsTransportFailure()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"error":"invalid_token","error_description":"access token expired."}""",
                Encoding.UTF8,
                "application/json")
        };

        var result = await HttpRpcTransportResultReader.ReadFailureAsync<Result<TokenResponse>>(response, CancellationToken.None);

        Assert.NotNull(result.ExpectFailure());
        Assert.Equal("401", result.ExpectFailure().Code);
        Assert.Contains("invalid_token", result.ExpectFailure().Message);
    }

    [Fact]
    public async Task ReadAsync_WhenResponseIsRedirect_ReturnsTransportFailure()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://example.com/callback");

        var result = await HttpRpcTransportResultReader.ReadAsync(
            response,
            CreateTypeInfo<Result>(),
            CancellationToken.None);

        Assert.NotNull(result.ExpectFailure());
        Assert.Equal("302", result.ExpectFailure().Code);
    }

    [Fact]
    public async Task ReadAsync_WhenResponseIsSuccess_ReadsContractFailureBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"kind":"failure","error":{"code":"invalid_token","message":"access token expired."}}""",
                Encoding.UTF8,
                "application/json")
        };

        var result = await HttpRpcTransportResultReader.ReadAsync(
            response,
            CreateTypeInfo<Result<TokenResponse>>(),
            CancellationToken.None);

        Assert.NotNull(result.ExpectFailure());

        Assert.Equal("invalid_token", result.ExpectFailure().Code);
    }

    [Fact]
    public async Task ReadStreamingFallbackAsync_WhenStreamingEndpointReturnsJsonFailure_ReturnsFailedStreamingResult()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"kind":"failure","error":{"code":"stream_failed","message":"stream negotiation failed."}}""",
                Encoding.UTF8,
                "application/json")
        };

        var result = await HttpRpcTransportResultReader.ReadStreamingFallbackAsync<TokenResponse>(
            response,
            CreateTypeInfo<Result>(),
            CancellationToken.None);

        Assert.NotNull(result.ExpectFailure());
        Assert.Equal("stream_failed", result.ExpectFailure().Code);
        Assert.Equal("stream negotiation failed.", result.ExpectFailure().Message);

    }

    public sealed record TokenResponse(string Token);

    private static JsonTypeInfo<T> CreateTypeInfo<T>()
        => (JsonTypeInfo<T>)new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }.GetTypeInfo(typeof(T));
}
