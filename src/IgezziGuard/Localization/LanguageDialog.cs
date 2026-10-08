namespace IgezziGuard;

internal sealed class LanguageDialog : Form
{
    internal LanguageDialog()
    {
        Text = Localizer.T("Language");
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 260); MinimumSize = new Size(480, 300);
        Font = new Font("Segoe UI", 11); Padding = new Padding(24);
        ShowInTaskbar = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label { Text = Localizer.T("App language"), AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        var languages = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, AccessibleName = label.Text };
        languages.Items.Add(new Localizer.Language("", Localizer.T("Use Windows language")));
        foreach (var language in Localizer.Languages) languages.Items.Add(language);
        string? preference = Localizer.ReadSavedLanguage(Localizer.PreferencePath);
        languages.SelectedIndex = 0;
        for (int i = 1; i < languages.Items.Count; i++)
            if (languages.Items[i] is Localizer.Language language && language.Code == preference) languages.SelectedIndex = i;
        var hint = new Label { Text = Localizer.T("The language will change the next time you open Hanki Tools."), AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 16) };
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new HankiButton { Text = Localizer.T("Save"), AutoSize = true, Primary = true };
        var cancel = new HankiButton { Text = Localizer.T("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => {
            try {
                string code = ((Localizer.Language)languages.SelectedItem!).Code;
                Localizer.SavePreference(Localizer.PreferencePath, code.Length == 0 ? null : code);
                DialogResult = DialogResult.OK; Close();
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                MessageBox.Show(this, Localizer.T("The language preference could not be saved.") + "\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        actions.Controls.Add(save); actions.Controls.Add(cancel);
        layout.Controls.Add(label); layout.Controls.Add(languages); layout.Controls.Add(hint); layout.Controls.Add(actions);
        Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        HankiTheme.Apply(this);
    }
}
