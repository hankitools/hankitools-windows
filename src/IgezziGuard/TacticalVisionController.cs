using System.Diagnostics;
using System.Runtime.InteropServices;
namespace IgezziGuard;

/// <summary>Runs on the UI thread for the lifetime of Hanki, even when the Games page is hidden.</summary>
internal sealed class TacticalVisionController : IDisposable
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 750 };
    private readonly TacticalVisionSession session = new(Path.Combine(SecurityPaths.Root, "tactical-vision-restore.json"), Nvidia.ReadVibrance, Nvidia.WriteVibrance);
    private (Guid Game, string Display, int Strength)? current;
    private bool failed;
    private volatile bool displayChanged;
    internal static string Status { get; private set; } = "Tactical Vision: waiting for an enabled game. Keep Hanki open while playing.";
    internal static event Action? StatusChanged;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    internal TacticalVisionController()
    {
        try { session.Recover(); }
        catch (Exception ex) when (Expected(ex)) { Fail(ex); }
        timer.Tick += (_, _) => Tick();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        timer.Start();
    }
    private void DisplayChanged(object? sender, EventArgs e) => displayChanged = true;
    private static bool Expected(Exception ex) => ex is IOException or UnauthorizedAccessException or NvidiaException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException;
    private static void SetStatus(string text) { if (Status == text) return; Status = text; StatusChanged?.Invoke(); }
    private void Fail(Exception ex)
    {
        failed = true;
        SetStatus("Tactical Vision paused: " + ex.Message + " Reopen Hanki to retry. Displays must be connected directly to NVIDIA.");
    }
    private void Tick()
    {
        try {
            if (failed) { session.Restore(); return; }
            var window = GetForegroundWindow();
            GetWindowThreadProcessId(window, out var pid);
            string? executable = null;
            // Protected or vanished processes simply do not qualify as a game.
            try { using var process = Process.GetProcessById((int)pid); executable = process.MainModule?.FileName; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException) { }
            var game = executable is null ? null : TacticalVisionSession.Match(GameLibrary.Read(GameLibrary.StorePath), executable);
            var display = Screen.FromHandle(window).DeviceName;
            (Guid Game, string Display, int Strength)? next = game is null ? null : (game.Id, display, game.TacticalVision);
            if (current == next && !displayChanged) return;
            displayChanged = false;
            session.Restore();
            current = null;
            if (game is null) { SetStatus("Tactical Vision: original colors restored. Keep Hanki open while playing."); return; }
            var screen = GraphicsProbe.Displays(includeModes: false).FirstOrDefault(d => d.Device == display);
            if (screen?.HdrEnabled != false) {
                current = next;
                SetStatus("Tactical Vision unavailable: use an SDR display with HDR off.");
                return;
            }
            session.Apply(display, game.TacticalVision);
            current = next;
            SetStatus($"Tactical Vision active: {game.Name}, {game.TacticalVision}% Digital Vibrance.");
        } catch (Exception ex) when (Expected(ex)) {
            Fail(ex);
            try { session.Restore(); } catch (Exception restore) when (Expected(restore)) { SetStatus(Status + " Color restoration pending: " + restore.Message); }
        }
    }
    public void Dispose()
    {
        timer.Stop(); timer.Dispose();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        try { session.Restore(); }
        catch (Exception ex) when (Expected(ex)) {
            MessageBox.Show("Tactical Vision could not restore the previous colors. Reopen Hanki with the display connected to retry, or adjust Digital Vibrance in NVIDIA Control Panel.\n\n" + ex.Message, "Tactical Vision");
        }
    }
}
