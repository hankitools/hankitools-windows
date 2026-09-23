using System.Diagnostics;
using System.Text;
namespace IgezziGuard;

/// <summary>
/// Technician: labels dialog → local session record → customer HTML report saved where the technician
/// chooses and opened in the browser (Print → Save as PDF). The business name and contact are remembered.
/// </summary>
internal static class CustomerReportFlow
{
    internal sealed record BusinessDefaults(string Name, string Contact);
    private static string Folder => Path.Combine(SecurityPaths.Root, "technician");
    private static string DefaultsPath => Path.Combine(Folder, "business.json");
    internal const string NotAvailable = "Customer reports are part of Hanki Technician. Your scan results, saved history and the Review / share report text remain available in every edition.";

    /// <returns>A status line for the page, or null when the technician cancelled.</returns>
    internal static string? Create(IWin32Window owner, DiagnosticScan scan, RepairReport? repairs)
    {
        var entitlements = EntitlementComposition.Current();
        if (!entitlements.Allows(HankiCapability.CustomerReports)) return NotAvailable;
        BusinessDefaults? saved = null;
        try { saved = LocalJson.Read<BusinessDefaults>(DefaultsPath); } catch (IOException) { }
        var (dialog, fields) = BuildLabels(saved);
        string business, contact, job, device; bool technical;
        using (dialog) {
            if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
            (business, contact, job, device, technical) = (fields.Business.Text.Trim(), fields.Contact.Text.Trim(), fields.Job.Text, fields.Device.Text, fields.Technical.Checked);
        }

        var service = new TechnicianSessions(entitlements);
        TechnicianSession session;
        try { session = service.Begin(job, device, scan, repairs); }
        catch (ArgumentException ex) { return "Could not create the report: " + ex.Message; }
        try {
            Directory.CreateDirectory(Folder);
            service.SaveLocal(Path.Combine(Folder, session.Id.ToString("N") + ".json"), session);
            LocalJson.Write(DefaultsPath, new BusinessDefaults(business, contact));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* The report still works without the local record. */ }

        string html;
        try { html = service.ExportHtml(session, new(business, contact), technical, AppInfo.Version); }
        catch (ArgumentException ex) { return "Could not create the report: " + ex.Message; }
        using var save = new SaveFileDialog { Title = "Save the customer report", Filter = "Web page (*.html)|*.html", AddExtension = true, OverwritePrompt = true,
            FileName = TechnicianReport.FileName(session.CustomerLabel, scan.Ended), InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        if (save.ShowDialog(owner) != DialogResult.OK) return "The report wasn't saved.";
        try { File.WriteAllText(save.FileName, html, new UTF8Encoding(false)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "Could not save the report: " + ex.Message; }
        try { using var process = Process.Start(new ProcessStartInfo(save.FileName) { UseShellExecute = true }); } catch { /* Saved; opening is a convenience. */ }
        return $"Customer report saved to {save.FileName} and opened in your browser. Check it before giving it to the customer; to make a PDF, press Ctrl+P and choose Save as PDF.";
    }

    internal sealed record LabelFields(TextBox Business, TextBox Contact, TextBox Job, TextBox Device, CheckBox Technical);
    /// <summary>The labels dialog; separate from Create so it can be rendered for review.</summary>
    internal static (Form Dialog, LabelFields Fields) BuildLabels(BusinessDefaults? saved)
    {
        var dialog = new Form { Text = "Customer report", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20) };
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        TextBox Field(string label, string value, string placeholder)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 10, 0, 4) });
            var box = new TextBox { Width = 460, Text = value, PlaceholderText = placeholder, AccessibleName = label, MaxLength = 100 };
            layout.Controls.Add(box); return box;
        }
        layout.Controls.Add(new Label { Text = "The report opens in your browser; print it or save it as PDF for the customer.", AutoSize = true, MaximumSize = new Size(460, 0), Tag = "intro", Margin = new Padding(0, 0, 0, 4) });
        var business = Field("Your business name", saved?.Name ?? "", "Shown at the top of the report");
        var contact = Field("Your contact (phone, email or website)", saved?.Contact ?? "", "Optional");
        var job = Field("Job number", "", "For example JOB-1042 (avoid the customer's name)");
        var device = Field("Device", "", "For example Lenovo laptop");
        var technical = new CheckBox { Text = "Add technical details (user names, addresses and paths are masked)", AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
        layout.Controls.Add(technical);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 18, 0, 0) };
        var create = new HankiButton { Text = "Create report", Primary = true, AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new HankiButton { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([create, cancel]); layout.Controls.Add(buttons);
        dialog.Controls.Add(layout); dialog.AcceptButton = create; dialog.CancelButton = cancel;
        HankiTheme.Apply(dialog);
        return (dialog, new(business, contact, job, device, technical));
    }
}
