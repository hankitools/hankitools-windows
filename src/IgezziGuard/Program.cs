namespace IgezziGuard;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--scheduled-health-check") {
            Environment.ExitCode = ScheduledHealthChecks.RunAsync().GetAwaiter().GetResult();
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ShowFatal(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ShowFatal(args.ExceptionObject as Exception ?? new InvalidOperationException("Unknown fatal error."));

        if (args.Length is 2 or 3 && args[0] == "--ui-smoke-test") {
            UiSmokeTest.Run(args[1], args.Length == 3 ? args[2] : null); return;
        }
        try { SecurityPaths.EnsureCreated(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show("Hanki cannot open its local data folder.\n\n" + ex.Message, "Startup unavailable"); return; }
        // Keep per-user observation and action-history writers in one app instance.
        FileStream instance;
        try { instance = new FileStream(Path.Combine(SecurityPaths.Root, "app-instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { MessageBox.Show("Hanki is already open, or its local data folder is unavailable.", "Hanki Tools"); return; }
        using var instanceLock = instance;
        using var licenceHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        licenceHttp.DefaultRequestHeaders.UserAgent.TryParseAdd("HankiTools/" + AppInfo.Version);
        AppLicensing.Start(new PolarLicenseProvider(new WindowsLicenseStore(), LicenseStoreConfig.Current(), licenceHttp, () => DateTimeOffset.UtcNow));
        using var tacticalVision = new TacticalVisionController();
        Application.Run(new HankiForm());
    }

    private static void ShowFatal(Exception exception)
    {
        // Nobody can close a dialog during the automated UI check: record the error and fail it at once.
        if (UiSmokeTest.Active) { UiSmokeTest.Note("FATAL " + exception); Environment.Exit(3); }
        bool logged = false;
        try
        {
            File.AppendAllText(SecurityPaths.ErrorLog,
                $"[{DateTimeOffset.Now:O}] {exception}\n\n");
            logged = true;
        }
        catch
        {
            // Never hide the original failure because logging failed.
        }

        MessageBox.Show(
            $"Hanki Tools encountered an unexpected error.\n\n{exception.Message}\n\n" +
            (logged ? $"Details were written to:\n{SecurityPaths.ErrorLog}" : "The error log could not be saved."),
            "Hanki Tools",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
