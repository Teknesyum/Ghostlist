namespace Ghostlist.App;

public sealed class DialogRequest : ObservableObject
{
    private readonly TaskCompletionSource<bool> completion = new();

    public DialogRequest(string title, string body, IReadOnlyList<string>? lines, bool askConfirmation)
    {
        Title = title;
        Body = body;
        Lines = lines ?? [];
        AskConfirmation = askConfirmation;
    }

    public string Title { get; }

    public string Body { get; }

    public IReadOnlyList<string> Lines { get; }

    public bool AskConfirmation { get; }

    public bool HasLines => Lines.Count > 0;

    public string ConfirmText => Strings.Current.Get(AskConfirmation ? "dialog.confirm" : "dialog.ok");

    public string CancelText => Strings.Current.Get("dialog.cancel");

    public Task<bool> Completion => completion.Task;

    public void Answer(bool accepted) => completion.TrySetResult(accepted);
}
