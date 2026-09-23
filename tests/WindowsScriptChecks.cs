using System.Text;
using IgezziGuard;
internal static class WindowsScriptChecks
{
    internal static async Task Run()
    {
        // Parse fixed scripts using the Windows PowerShell parser; never invoke any of their commands.
        foreach(var script in WindowsDiagnosticCatalog.Create(includeExternal:true).OfType<WindowsDiagnosticModule>().Select(m=>m.Script).Append(ActivationDiagnostic.Script)){
            var encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(script));
            var parse="$s=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('"+encoded+"'));$tokens=$null;$errors=$null;[void][System.Management.Automation.Language.Parser]::ParseInput($s,[ref]$tokens,[ref]$errors);if($errors.Count -gt 0){throw ($errors.Message -join '; ')};'Parsed'";
            var result=await WindowsCommand.PowerShellCapture(parse,CancellationToken.None,20);
            DiagnosticChecks.Check(result.StandardOutput.Trim()=="Parsed","fixed diagnostic script parses without execution");
        }
    }
}
