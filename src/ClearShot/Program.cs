namespace ClearShot;

internal static class Program
{
    private const string ShowSignalName = @"Local\ClearShot.ShowWindow";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\ClearShot.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            // Already running in the tray: ask that copy to open its window, then quietly exit.
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ShowSignalName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }
            return;
        }
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        bool startedByWindows = args.Contains(StartupRegistration.StartupArgument, StringComparer.OrdinalIgnoreCase);

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Write($"Unhandled: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write($"Fatal: {e.ExceptionObject}");
        Application.Run(new TrayApp(showSignal, openWindow: !startedByWindows));
    }
}
