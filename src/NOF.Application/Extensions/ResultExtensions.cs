using NOF.Domain;

namespace NOF.Contract;

/// <summary>
/// Extension methods for the NOF.Application layer.
/// </summary>
public static partial class NOFApplicationExtensions
{
    extension(Result)
    {
        /// <summary>Creates a failed result from a <see cref="NOF.Domain.Failure"/> descriptor.</summary>
        /// <param name="failure">The failure descriptor.</param>
        /// <returns>A failed result.</returns>
        public static NOF.Contract.Failure Fail(NOF.Domain.Failure failure)
        {
            return Result.Fail(failure.ErrorCode, failure.Message);
        }
    }
}
