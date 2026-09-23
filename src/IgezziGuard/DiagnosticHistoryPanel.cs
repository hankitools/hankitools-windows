namespace IgezziGuard;
public sealed class DiagnosticHistoryPanel : ToolPage
{
    private readonly ComboBox first = new() { Width=270, DropDownStyle=ComboBoxStyle.DropDownList, AccessibleName="Earlier scan" };
    private readonly ComboBox second = new() { Width=270, DropDownStyle=ComboBoxStyle.DropDownList, AccessibleName="Later scan" };
    private readonly ComboBox frequency = new() { Width=120, DropDownStyle=ComboBoxStyle.DropDownList, AccessibleName="Scheduled check frequency" };
    private IReadOnlyList<DiagnosticScan> scans=[];
    private readonly DiagnosticHistory history=new(Path.Combine(SecurityPaths.Root,"diagnostic-history.json"));
    private readonly RepairAudit audit=new(Path.Combine(SecurityPaths.Root,"repair-audit.json"));
    public DiagnosticHistoryPanel():base("Local scan history keeps up to 30 scans for 90 days, without raw diagnostic evidence. Repair audit is retained separately, including incomplete attempts. No cloud account or upload. Existing scanner history and undo journals remain separate.")
    {
        frequency.Items.AddRange(["Daily", "Weekly"]); frequency.SelectedIndex=0; Bar.Controls.Add(frequency);
        Button("Schedule check", async ()=>{
            var entitlements=EntitlementComposition.Current();
            if(!entitlements.Allows(HankiCapability.ScheduledChecks)){Output.Text="Scheduled checks are an optional Pro convenience. Manual Full System Scan and saved history remain available. Production licensing is not connected in this candidate.";return;}
            var selectedFrequency = frequency.SelectedIndex==1?HealthCheckFrequency.Weekly:HealthCheckFrequency.Daily;
            if(!Review($"Create a current-user Windows scheduled task for a {selectedFrequency.ToString().ToLowerInvariant()} local check at 19:00 (weekly: Sunday)? Runs only while signed in, at standard privilege, without external probes or repairs. Keep this application at its current path. Remove schedule here or in Task Scheduler."))return;
            await Run(async t=>{await ScheduledHealthChecks.InstallAsync(selectedFrequency,entitlements,t);return "Schedule registered. Manage or remove it in Windows Task Scheduler; results appear in local diagnostic history.";});
        });
        Button("Remove schedule",async()=>{if(!Review("Remove the Hanki local health-check task? Existing history is kept."))return;await Run(async t=>{await ScheduledHealthChecks.RemoveAsync(t);return "Schedule removed.";});});
        Button("Refresh saved scans",RefreshHistory);Bar.Controls.Add(first);Bar.Controls.Add(second);
        Button("Open selected scan",()=>{if(first.SelectedIndex<0)return;var scan=scans[first.SelectedIndex];try{Output.Text=FullScanPanel.Summary(scan)+"\r\n\r\n"+RepairReportText.Format(new(scan.Id,audit.Read().Where(a=>a.ScanId==scan.Id).Select(a=>a.Attempt).ToArray()));}catch{Output.Text="Repair audit unavailable. Existing files were preserved.";}});
        Button("Compare selected scans",()=>{if(first.SelectedIndex<0||second.SelectedIndex<0)return;var a=scans[first.SelectedIndex];var b=scans[second.SelectedIndex];if(a.Ended>b.Ended)(a,b)=(b,a);Output.Text=$"{a.Ended:g} → {b.Ended:g}\r\nChanges are observations, not proof of repair causation.\r\n\r\n"+string.Join("\r\n",DiagnosticHistory.Compare(a,b));});
        Button("Customer report",()=>{
            if(first.SelectedIndex<0){Output.Text="Choose a saved scan first.";return;}
            var entitlements=EntitlementComposition.Current();
            if(!entitlements.Allows(HankiCapability.CustomerReports)){Output.Text="Customer reports are an additive Technician capability. Your local scan summary and reviewed text export remain available in Community.";return;}
            using var dialog=new Form {Text="Customer report labels",Size=new Size(620,360),StartPosition=FormStartPosition.CenterParent};
            var fields=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,Padding=new Padding(16)};
            var job=new TextBox {Width=550,PlaceholderText="Job number (avoid customer personal data)",AccessibleName="Job label"};
            var device=new TextBox {Width=550,PlaceholderText="Device label",AccessibleName="Device label"};
            var business=new TextBox {Width=550,PlaceholderText="Business / technician display name",AccessibleName="Business name"};
            var contact=new TextBox {Width=550,PlaceholderText="Business contact",AccessibleName="Business contact"};
            var technical=new CheckBox {Text="Include available minimized technical details",AutoSize=true};
            var create=new HankiButton {Text="Prepare report for review",AutoSize=true,DialogResult=DialogResult.OK};
            fields.Controls.AddRange([job,device,business,contact,technical,create]);dialog.Controls.Add(fields);HankiTheme.Apply(dialog);
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            try {var scan=scans[first.SelectedIndex];var service=new TechnicianSessions(entitlements);var session=service.Begin(job.Text,device.Text,scan,new(scan.Id,audit.Read().Where(a=>a.ScanId==scan.Id).Select(a=>a.Attempt).ToArray()));
                service.SaveLocal(Path.Combine(SecurityPaths.Root,"technician",session.Id.ToString("N")+".json"),session);
                Output.Text=service.Export(session,new(business.Text,contact.Text),technical.Checked)+"\r\n\r\nUse Review / share report to edit and save the customer copy. Saved scan history omits original raw evidence.";
            }catch{Output.Text="Could not prepare the report. Check labels, local storage and audit availability.";}
        });
        Button("Clear scan history",()=>{if(!Review("Remove saved Full System Scan summaries? Repair audit, recovery backups and legacy scanner history will remain available."))return;try{history.Clear();RefreshHistory();}catch{Output.Text="History could not be cleared.";}});
    }
    private void RefreshHistory(){try{scans=history.Read();first.Items.Clear();second.Items.Clear();foreach(var s in scans){var text=$"{s.Ended:g} · {s.Results.Count} findings";first.Items.Add(text);second.Items.Add(text);}Output.Text=$"{scans.Count} saved scans. Select one to inspect or two to compare.";}catch{Output.Text="Saved history is unavailable or damaged. Original files were preserved; current scans can still run.";}}
}
