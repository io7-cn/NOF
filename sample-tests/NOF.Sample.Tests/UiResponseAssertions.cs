using NOF.Contract;
using NOF.Sample.UI.Services;
using Xunit;

namespace NOF.Sample.Tests;

public static class UiResponseAssertions
{
    public static T ExpectSuccess<T>(this UiResponse<T> outcome) => Assert.IsAssignableFrom<T>(outcome.Value);
    public static Empty ExpectSuccess(this UiResponse outcome) => Assert.IsType<Empty>(outcome.Value);
}
