namespace IgezziGuard;
public sealed class DiagnosticHistoryPanel : ToolPage
{
    private readonly ComboBox first = new() { Width=230, DropDownStyle=ComboBoxStyle.DropDownList, AccessibleName="Scan to open" };
    private readonly ComboBox second = new() { Width=230, DropDownStyle=ComboBoxStyle.DropDownList, AccessibleName="Scan to compare with" };
    private readonly ComboBox frequency = new() { Width=120, DropDownStyle=ComboBoxStyle.DropDownList, AccessibleName="Scheduled check frequency" };
    private IReadOnlyList<DiagnosticScan> scans=[];
    private readonly DiagnosticHistory history=new(Path.Combine(SecurityPaths.Root,"diagnostic-history.json"));
    private readonly RepairAudit audit=new(Path.Combine(SecurityPaths.Root,"repair-audit.json"));
    public DiagnosticHistoryPanel():base("Local scan history keeps up to 30 scans for 90 days, without raw diagnostic evidence. Repair audit is retained separately, including incomplete attempts. No cloud account or upload. Existing scanner history and undo journals remain separate.")
    {
        frequency.Items.AddRange(["Daily", "Weekly"]); frequency.SelectedIndex=0;
        // Row 1: open or compare saved scans. Row 2: scheduling and less frequent actions.
        first.Margin=second.Margin=new Padding(0,6,8,0);
        Bar.Controls.Add(first);
        Button("Open selected scan",()=>{if(first.SelectedIndex<0)return;var scan=scans[first.SelectedIndex];try{Output.Text=FullScanPanel.Summary(scan)+"\r\n\r\n"+RepairReportText.Format(new(scan.Id,audit.Read().Where(a=>a.ScanId==scan.Id).Select(a=>a.Attempt).ToArray()));}catch{Output.Text="Repair audit unavailable. Existing files were preserved.";}});
        Bar.Controls.Add(new Label{Text="compare with",AutoSize=true,Tag="intro",Margin=new Padding(10,10,8,0)});
        Bar.Controls.Add(second);
        Button("Compare selected scans",()=>{if(first.SelectedIndex<0||second.SelectedIndex<0)return;var a=scans[first.SelectedIndex];var b=scans[second.SelectedIndex];if(a.Ended>b.Ended)(a,b)=(b,a);Output.Text=$"{a.Ended:g} → {b.Ended:g}\r\nChanges are observations, not proof of repair causation.\r\n\r\n"+string.Join("\r\n",DiagnosticHistory.Compare(a,b));});
        var more=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Padding=new Padding(0,0,0,14)};
        HankiButton More(string text,Action action){var b=new HankiButton{Text=text,AutoSize=true,Appearance=HankiButtonStyle.Quiet,Margin=new Padding(0,3,6,0)};b.Click+=(_,_)=>{if(!IsBusy)action();};more.Controls.Add(b);return b;}
        frequency.Margin=new Padding(0,6,4,0);
        more.Controls.Add(new Label{Text="Scheduled check",AutoSize=true,Tag="intro",Margin=new Padding(0,9,10,0)});
        more.Controls.Add(frequency);
        More("Schedule check", async ()=>{
            var entitlements=EntitlementComposition.Current();
            if(!entitlements.Allows(HankiCapability.ScheduledChecks)){Output.Text="Scheduled checks are an optional Pro convenience. Manual Full System Scan and saved history remain available. Production licensing is not connected in this candidate.";return;}
            var selectedFrequency = frequency.SelectedIndex==1?HealthCheckFrequency.Weekly:HealthCheckFrequency.Daily;
            if(!Review($"Create a current-user Windows scheduled task for a {selectedFrequency.ToString().ToLowerInvariant()} local check at 19:00 (weekly: Sunday)? Runs only while signed in, at standard privilege, without external probes or repairs. Keep this application at its current path. Remove schedule here or in Task Scheduler."))return;
            await Run(async t=>{await ScheduledHealthChecks.InstallAsync(selectedFrequency,entitlements,t);return "Schedule registered. Manage or remove it in Windows Task Scheduler; results appear in local diagnostic history.";});
        });
        More("Remove schedule",async()=>{if(!Review("Remove the Hanki local health-check task? Existing history is kept."))return;await Run(async t=>{await ScheduledHealthChecks.RemoveAsync(t);return "Schedule removed.";});});
        More("Refresh saved scans",RefreshHistory);
        More("Customer report",()=>{
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
        More("Clear scan history",()=>{if(!Review("Remove saved Full System Scan summaries? Repair audit, recovery backups and legacy scanner history will remain available."))return;try{history.Clear();RefreshHistory();}catch{Output.Text="History could not be cleared.";}});
        Controls.Add(more);Controls.SetChildIndex(more,1);
        // Saved scans load when the page opens: newest first, compared with the one before it.
        VisibleChanged+=(_,_)=>{if(!Visible||IsBusy)return;try{LoadScans();}catch(Exception ex) when (ex is IOException or UnauthorizedAccessException){Output.Text="Saved history is unavailable or damaged. Original files were preserved; current scans can still run.";}};
    }
    private void LoadScans()
    {
        scans=history.Read();first.Items.Clear();second.Items.Clear();
        foreach(var s in scans){var text=$"{s.Ended.ToLocalTime():g} · {s.Results.Count} findings";first.Items.Add(text);second.Items.Add(text);}
        if(scans.Count>0)first.SelectedIndex=0;
        if(scans.Count>1)second.SelectedIndex=1;
    }
    private void RefreshHistory(){try{LoadScans();Output.Text=$"{scans.Count} saved scans. The newest is selected; choose another to open or compare.";}catch{Output.Text="Saved history is unavailable or damaged. Original files were preserved; current scans can still run.";}}
}
