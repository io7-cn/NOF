using System.Text;

namespace NOF.Contract;

/// <summary>Preserves successful result messages when the response body contains SSE items.</summary>
public static class StreamingResponseMetadata
{
    /// <summary>The response header containing the Base64-encoded UTF-8 message.</summary>
    public const string MessageHeader = "X-NOF-Result-Message";

    /// <summary>Encodes a message for an HTTP header.</summary>
    public static string EncodeMessage(string message) => Convert.ToBase64String(Encoding.UTF8.GetBytes(message));

    /// <summary>Reads the successful stream's message, defaulting to an empty message.</summary>
    public static string ReadMessage(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.TryGetValues(MessageHeader, out var values)
            ? Encoding.UTF8.GetString(Convert.FromBase64String(values.Single()))
            : string.Empty;
    }
}
