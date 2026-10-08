using System.Diagnostics;
using System.Text;

namespace IgezziGuard;

internal static class WindowsCommand
{
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    internal sealed record CommandOutput(string StandardOutput, string StandardError)
    {
        public string DisplayText => StandardOutput + (string.IsNullOrWhiteSpace(StandardError) ? "" : "\r\nTool notes: " + StandardError);
    }
    public static async Task<string> PowerShell(string script, CancellationToken token, int seconds = 60) =>
        (await PowerShellCapture(script, token, seconds)).DisplayText;
    // Windows PowerShell serializes progress records (e.g. "Preparing modules for first use.") to redirected stderr as CLIXML;
    // suppress them so stderr carries only genuine errors.
    public static Task<CommandOutput> PowerShellCapture(string script, CancellationToken token, int seconds = 60) => RunCaptured(
        Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
        ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(
            "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new(); $ProgressPreference='SilentlyContinue'; $ErrorActionPreference='Stop'; try { " + script + " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }"))], token, seconds, true);
    public static async Task<string> Run(string exe, string[] args, CancellationToken token, int seconds = 60, bool utf8 = false, string? workingDirectory = null, bool isolateDebugger = false)
        => (await RunCaptured(exe, args, token, seconds, utf8, workingDirectory, isolateDebugger)).DisplayText;
    internal static async Task<CommandOutput> RunCaptured(string exe, string[] args, CancellationToken token, int seconds = 60, bool utf8 = false, string? workingDirectory = null, bool isolateDebugger = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? Environment.SystemDirectory };
        if (utf8) { start.StandardOutputEncoding = Encoding.UTF8; start.StandardErrorEncoding = Encoding.UTF8; }
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (isolateDebugger) foreach (var key in new[] { "_NT_SYMBOL_PATH", "_NT_ALT_SYMBOL_PATH", "_NT_SOURCE_PATH", "_NT_DEBUGGER_EXTENSION_PATH", "_NT_DEBUGGER_INIT" }) start.Environment.Remove(key);
        using var process = Process.Start(start) ?? throw new IOException("Could not start Windows tool.");
        async Task<string> Read(StreamReader stream) {
            var text = new StringBuilder(); var chars = new char[4096]; int count;
            while ((count = await stream.ReadAsync(chars.AsMemory(), timeout.Token)) > 0) {
                if (text.Length < 2_000_000) text.Append(chars, 0, Math.Min(count, 2_000_000 - text.Length));
            }
            return text.ToString() + (text.Length >= 2_000_000 ? "\r\n[Output capped at 2 million characters]" : "");
        }
        var stdout = Read(process.StandardOutput); var stderr = Read(process.StandardError);
        try {
            await process.WaitForExitAsync(timeout.Token); var output = await stdout; var error = await stderr;
            if (process.ExitCode != 0) throw new IOException($"Windows tool exit {process.ExitCode}: {error}\r\n{output}");
            return new CommandOutput(output, error);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) {
            throw new IOException($"Windows tool timed out after {seconds} seconds. Results may be incomplete; verify any requested setting change in Recovery or Windows before retrying.");
        }
        finally {
            if (!process.HasExited) { try { process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
        }
    }
}
