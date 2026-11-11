namespace NOF.Contract;

public static class NOFSampleResultExtensions
{
    extension(System.Runtime.CompilerServices.IUnion result)
    {
        public string GetRpcFailureMessage()
        {
            ArgumentNullException.ThrowIfNull(result);

            var failure = result.Value as Failure
                ?? throw new InvalidOperationException("Expected a failure outcome.");
            if (!string.IsNullOrWhiteSpace(failure.Message))
            {
                return failure.Message;
            }

            return int.TryParse(failure.Code, out var statusCode)
                ? $"请求失败，状态码: {statusCode}"
                : "请求失败";
        }
    }
}
