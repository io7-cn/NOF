using NOF.Abstraction;
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
            if (!string.IsNullOrWhiteSpace(failure.Detail))
            {
                return failure.Detail;
            }

            return failure.Title;
        }
    }
}
