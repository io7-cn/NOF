using System.Runtime.CompilerServices;
using Xunit;

namespace NOF.Contract;

// Internal because this source is linked into multiple test assemblies; exporting it would create ambiguous types.
internal static class ResultAssertions
{
    public static Success<T> ExpectSuccess<T>(this Result<T> result) => Assert.IsType<Success<T>>(result.Value);
    public static Success ExpectSuccess(this Result result) => Assert.IsType<Success>(result.Value);
    public static Failure ExpectFailure(this IUnion result) => Assert.IsType<Failure>(result.Value);
}
