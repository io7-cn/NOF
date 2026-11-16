namespace NOF.Sample.UI.Services;

/// <summary>Stores the current sample notification within a UI session.</summary>
public sealed class SampleMessageService
{
    public event Action? Changed;

    public string? Text { get; private set; }
    public string Level { get; private set; } = "info";

    public void Success(string text) => Show(text, "success");
    public void Warning(string text) => Show(text, "warning");
    public void Error(string text) => Show(text, "error");

    public void Dismiss()
    {
        Text = null;
        Changed?.Invoke();
    }

    private void Show(string text, string level)
    {
        Text = text;
        Level = level;
        Changed?.Invoke();
    }
}
