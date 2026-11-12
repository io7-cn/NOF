using NOF.Abstraction;
using System.Runtime.CompilerServices;
using Xunit;

namespace NOF.Contract;

// Internal because this source is linked into multiple test assemblies; exporting it would create ambiguous types.
internal static class ResultAssertions
{
    public static T ExpectSuccess<T>(this Result<T> result) => Assert.IsAssignableFrom<T>(result.Value);
    public static Empty ExpectSuccess(this Result result) => Assert.IsType<Empty>(result.Value);
    public static Failure ExpectFailure(this IUnion result) => Assert.IsType<Failure>(result.Value);
}
