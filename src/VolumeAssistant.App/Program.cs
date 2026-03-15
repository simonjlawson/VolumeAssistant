namespace VolumeAssistant.App;

/// <summary>
/// Application entry point. Initialises Windows Forms and starts the tray application.
/// </summary>
static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Install a Windows Forms synchronization context so that background threads
        // can marshal work to the UI thread before the message loop starts.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        using var trayApp = new TrayApplication();
        trayApp.Run();
    }
}
