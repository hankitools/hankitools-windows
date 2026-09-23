namespace IgezziGuard;
public sealed class ActivationPanel : ToolPage
{
    private readonly CheckBox network = new() { Text="Allow organization KMS network checks", AutoSize=true };
    public ActivationPanel():base("Understand legitimate Windows activation problems using read-only licensing data. Hanki does not collect full product keys, change activation settings or activate Windows. Organization KMS probes are optional and run only when Windows reports a KMS client.")
    {
        Button("Review Windows activation",async()=>{
            bool approved=network.Checked;
            if(approved&&!Review("Allow bounded DNS SRV discovery and TCP reachability checks to the KMS host configured in Windows or published by your organization's DNS? Only installed KMS clients are probed. No public KMS server is suggested and no activation request is sent."))return;
            var context=FullScanPanel.Context(approved);
            await Run(async token=>string.Join("\r\n\r\n",(await DiagnosticExecution.RunAsync(new ActivationDiagnostic(),context,null,token)).Select(r=>FindingAnalysis.Describe(r))));
        });
        Bar.Controls.Add(network);
        Button("Open Windows Activation settings",()=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:activation"){UseShellExecute=true});}catch{Output.Text="Could not open Activation settings. Open Settings → System → Activation manually.";}});
    }
}
