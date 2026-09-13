namespace Picklebook.Services;

public record ToastMessage(string Title, string Message, string Type);

public class ToastService
{
    public event Action<ToastMessage>? OnShow;
    public void Show(ToastMessage m) => OnShow?.Invoke(m);
    public void Success(string message) => Show(new ToastMessage("Done", message, "success"));
    public void Error(string message) => Show(new ToastMessage("Something went wrong", message, "danger"));
    public void Info(string message) => Show(new ToastMessage("Heads up", message, "info"));
}