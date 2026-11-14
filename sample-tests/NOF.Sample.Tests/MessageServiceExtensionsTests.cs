using NOF.Sample.UI.Services;
using Xunit;

namespace NOF.Sample.Tests;

public class MessageServiceExtensionsTests
{
    [Fact]
    public void UnwrapWithMessageReturnsReferencePayload()
    {
        var messages = new SampleMessageService();
        UiResponse<string> response = "sample";

        Assert.Equal("sample", messages.UnwrapWithMessage(response));
    }

    [Fact]
    public void UnwrapWithMessageReturnsValuePayload()
    {
        var messages = new SampleMessageService();
        UiResponse<int> response = 42;

        Assert.Equal(42, messages.UnwrapWithMessage(response));
    }

    [Fact]
    public void UnwrapWithMessagePassesPayloadToMessageFactory()
    {
        var messages = new SampleMessageService();
        UiResponse<string> response = "sample";
        string? observed = null;

        var value = messages.UnwrapWithMessage(response, payload =>
        {
            observed = payload;
            return payload;
        });

        Assert.Equal("sample", value);
        Assert.Equal("sample", observed);
    }

    [Fact]
    public void UnwrapWithMessageDisplaysFailureWithoutCallingSuccessFactory()
    {
        var messages = new SampleMessageService();
        UiResponse<string> response = new Abstraction.Failure("Rejected", "Invalid input");

        var value = messages.UnwrapWithMessage(response, _ => throw new InvalidOperationException());

        Assert.Null(value);
        Assert.Equal("Invalid input", messages.Text);
        Assert.Equal("error", messages.Level);
    }

    [Fact]
    public void ShowMessageDisplaysCommandSuccess()
    {
        var messages = new SampleMessageService();
        UiResponse response = NOF.Contract.Empty.Instance;

        messages.ShowMessage(response, "Saved", showSuccess: true);

        Assert.Equal("Saved", messages.Text);
        Assert.Equal("success", messages.Level);
    }

    [Fact]
    public void DismissNotifiesTheUiAndClearsTheMessage()
    {
        var messages = new SampleMessageService();
        messages.Warning("Canceled");
        var changed = false;
        messages.Changed += () => changed = true;

        messages.Dismiss();

        Assert.Null(messages.Text);
        Assert.True(changed);
    }
}
