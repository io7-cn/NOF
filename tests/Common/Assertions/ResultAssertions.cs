using NOF.Abstraction;
using System.Runtime.CompilerServices;
using Xunit;

namespace NOF.Contract;

// Internal because this source is linked into multiple test assemblies; exporting it would create ambiguous types.
internal static class ResultAssertions
{
    public static T ExpectSuccess<T>(this NOF.Testing.TestOutcome<T> result) => Assert.IsAssignableFrom<T>(result.Value);
    public static Empty ExpectSuccess(this NOF.Testing.TestOutcome result) => Assert.IsType<Empty>(result.Value);
    public static IFailure ExpectFailure(this IUnion result) => Assert.IsAssignableFrom<IFailure>(result.Value);
}
