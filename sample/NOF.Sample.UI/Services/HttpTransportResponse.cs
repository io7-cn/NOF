using NOF.Abstraction;
using System.Net;

namespace NOF.Sample.UI.Services;

/// <summary>The explicit remote integration outcome.</summary>
public union HttpTransportResponse<T>(T, HttpTransportFailure);
