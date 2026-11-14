using NOF.Abstraction;
using System.Net;

namespace NOF.Sample.UI.Services;

/// <summary>A failure classified by the actual transport status, independently of Problem title text.</summary>
public sealed record HttpTransportFailure(HttpStatusCode StatusCode, string Title, string Detail) : IFailure;

/// <summary>The explicit remote integration outcome.</summary>
public union HttpTransportResponse<T>(T, HttpTransportFailure);
