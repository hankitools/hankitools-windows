namespace IgezziGuard;
public sealed class ActivationPanel : ToolPage
{
    protected override bool ReadOnlyTool => true;
    private readonly CheckBox network = new() { Text="Allow organization KMS network checks", AutoSize=true };
    public ActivationPanel():base("Understand legitimate Windows activation problems using read-only licensing data. Hanki does not collect full product keys, change activation settings or activate Windows. Organization KMS probes are optional and run only when Windows reports a KMS client.")
    {
        Button("Review Windows activation",async()=>{
            bool approved=network.Checked;
            if(approved&&!Review("Allow bounded DNS SRV discovery and TCP reachability checks to the KMS host configured in Windows or published by your organization's DNS? Only installed KMS clients are probed. No public KMS server is suggested and no activation request is sent."))return;
            var context=FullScanPanel.Context(approved);
            await Run(async token=>{
                var results=await DiagnosticExecution.RunAsync(new ActivationDiagnostic(),context,null,token);
                var cards=results.Select(ResultPresentation.FromFinding).ToList();
                var worst=Diagnosis.Worst(cards.Select(c=>c.Status));
                cards.Add(new("What to do next",worst is CardStatus.Problem or CardStatus.Review
                    ?"Open Windows Activation settings (button above) and use Troubleshoot, or enter a genuine product key. Organization PCs may need your IT department."
                    :"Nothing to do. If Windows still shows an activation message, open Windows Activation settings for Microsoft's own troubleshooter."));
                string headline=worst switch{CardStatus.Problem=>"Windows activation needs attention",CardStatus.Review=>"Windows activation has something worth checking",
                    CardStatus.Unknown=>"Activation status could not be fully read",_=>"Windows reports no activation problem"};
                return Diagnosis.From(string.Join("\r\n\r\n",results.Select(FindingAnalysis.Describe)),cards,headline);
            });
        });
        Bar.Controls.Add(network);
        Button("Open Windows Activation settings",()=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:activation"){UseShellExecute=true});}catch{Output.Text="Could not open Activation settings. Open Settings → System → Activation manually.";}});
    }
}
