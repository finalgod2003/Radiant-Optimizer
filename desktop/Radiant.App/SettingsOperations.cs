namespace Radiant.App;

public sealed partial class MainWindow
{
    private async Task<T> ChangeSettingsAsync<T>(Func<Task<T>> operation)
    {
        if (changingSettings) throw new InvalidOperationException("A settings operation is already running.");
        changingSettings = true;
        shell.IsEnabled = false;
        Notice("Saving recovery data and checking the Windows result…");
        try { return await Task.Run(operation); }
        finally { changingSettings = false; shell.IsEnabled = true; }
    }
}
