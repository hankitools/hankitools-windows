namespace IgezziGuard;

public sealed class AiChatPanel : UserControl
{
    private readonly TextBox key = new() { Width = 240, UseSystemPasswordChar = true, PlaceholderText = "OpenAI API key (session only)" };
    private readonly TextBox model = new() { Width = 190, PlaceholderText = "Your API model ID" };
    private readonly TextBox conversation = Area(true);
    private readonly TextBox message = Area(false);
    private readonly Label status = new() { AutoSize = true, Text = "Optional OpenAI API connection. API usage has separate billing; a ChatGPT subscription is not an API credential.\nNothing sends automatically. Key and chat stay in this app's memory until cleared/exit. Live integration is not yet validated." };
    private readonly HankiButton send = new() { Text = "Review / send to OpenAI…", Primary = true, AutoSize = true };
    private readonly HankiButton cancel = new() { Text = "Cancel request", AutoSize = true, Enabled = false };
    private readonly HankiButton clear = new() { Text = "Clear chat and key", AutoSize = true };
    private readonly List<AiMessage> history = [];
    private CancellationTokenSource? pending;
    public bool IsBusy => pending is not null;
    public void Cancel() => pending?.Cancel();
    public AiChatPanel()
    {
        Dock = DockStyle.Fill;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize));
        layout.RowStyles.Add(new(SizeType.Percent, 65)); layout.RowStyles.Add(new(SizeType.Percent, 35)); layout.RowStyles.Add(new(SizeType.AutoSize));
        var config = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true }; config.Controls.AddRange([key, model]);
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true }; bar.Controls.AddRange([send, cancel, clear]);
        layout.Controls.Add(status, 0, 0); layout.Controls.Add(config, 0, 1); layout.Controls.Add(conversation, 0, 2); layout.Controls.Add(message, 0, 3); layout.Controls.Add(bar, 0, 4); Controls.Add(layout);
        message.PlaceholderText = "Paste reviewed evidence or type a follow-up question…";
        send.Click += async (_, _) => await Send(); cancel.Click += (_, _) => Cancel();
        clear.Click += (_, _) => { if (MessageBox.Show(this, "Clear this local chat, draft and API key? This does not delete any provider-side records.", "Clear AI session", MessageBoxButtons.OKCancel) == DialogResult.OK) { history.Clear(); conversation.Clear(); message.Clear(); key.Clear(); } };
    }
    public bool LoadDraft(string text)
    {
        if (IsBusy) { MessageBox.Show(this, "Wait for or cancel the current AI request first."); return false; }
        if (message.TextLength > 0 && MessageBox.Show(this, "Replace the unsent AI draft? Existing chat history will still be included in the next review.", "Load reviewed report", MessageBoxButtons.OKCancel) != DialogResult.OK) return false;
        message.Text = text; return true;
    }
    private async Task Send()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(message.Text)) return;
        try {
            var next = history.Append(new AiMessage("user", message.Text)).ToList();
            var payload = AiClient.Payload(model.Text.Trim(), next);
            if (string.IsNullOrWhiteSpace(key.Text)) throw new ArgumentException("Enter your own API key in the masked field. Do not paste it into the report.");
            if (payload.Contains(key.Text.Trim(), StringComparison.Ordinal)) throw new ArgumentException("Your API key appears in the request text. Remove it before sending.");
            using var payloadDocument = System.Text.Json.JsonDocument.Parse(payload);
            using var review = new Form { Text = "Review exactly what will be sent", Size = new Size(900, 650), StartPosition = FormStartPosition.CenterParent };
            var text = Area(true); text.Text = "DESTINATION: https://api.openai.com/v1/responses\r\nThe JSON below includes ALL prior turns, the new message and instructions. The API key is sent separately in the Authorization header.\r\nstore=false is requested; this is not a guarantee of zero provider retention. API charges may apply, including after cancellation.\r\n\r\n" + System.Text.Json.JsonSerializer.Serialize(payloadDocument.RootElement, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var consent = new CheckBox { Text = "I reviewed this entire request and agree to send it to OpenAI", Dock = DockStyle.Bottom, Height = 35 };
            var approve = new HankiButton { Text = "Send reviewed request", Dock = DockStyle.Bottom, Height = 40, Enabled = false, DialogResult = DialogResult.OK };
            consent.CheckedChanged += (_, _) => approve.Enabled = consent.Checked;
            review.Controls.Add(text); review.Controls.Add(consent); review.Controls.Add(approve); HankiTheme.Apply(review);
            if (review.ShowDialog(this) != DialogResult.OK || !consent.Checked) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)); pending = cts;
            send.Enabled = clear.Enabled = key.Enabled = model.Enabled = message.Enabled = false; cancel.Enabled = true;
            status.Text = "Sending reviewed request to OpenAI… AI answers may be wrong; no commands will run.";
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var answer = await AiClient.Send(client, key.Text.Trim(), payload, cts.Token);
            history.Add(next[^1]); history.Add(new("assistant", answer));
            conversation.Text = string.Join("\r\n\r\n", history.Select(m => m.Role.ToUpperInvariant() + "\r\n" + m.Content)); message.Clear();
            status.Text = "Answer received. Verify suggestions before acting; Hanki executes no AI commands. Next send will include this conversation for review.";
        } catch (OperationCanceledException) { status.Text = "Request cancelled or timed out. Draft retained; the provider may still process/bill the request. No retry made."; }
        catch (Exception ex) { status.Text = ex is IOException or ArgumentException ? ex.Message : "AI request failed. Draft retained; check connectivity and model compatibility. No automatic retry."; }
        finally { pending = null; send.Enabled = clear.Enabled = key.Enabled = model.Enabled = message.Enabled = true; cancel.Enabled = false; }
    }
    private static TextBox Area(bool readOnly) => new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = readOnly, ScrollBars = ScrollBars.Both, MaxLength = 100000 };
}
