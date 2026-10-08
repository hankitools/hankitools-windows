using System.Windows;
using System.Windows.Controls;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBox = System.Windows.Controls.TextBox;

namespace IgezziGuard.Shell;

/// <summary>Assistant → Prepare / redact: turn a report into a reviewed prompt you copy yourself. Nothing is sent from here.</summary>
internal sealed class PrepareView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly TextBox question = new() { Style = (Style)Application.Current.FindResource("FieldBox"), MaxLength = 2000, Tag = "What happened? What would you like explained?", Margin = new Thickness(0, 6, 0, 8) };
    private readonly TextBox source = Editor(false, 260), preview = Editor(true, 260);
    private readonly CheckBox reviewed = new() { Content = "I reviewed the full preview for private data", Margin = new Thickness(0, 8, 14, 8), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button copy = Buttons.Secondary("Copy reviewed prompt");
    private readonly TextBlock status = UiKit.Text("Local preparation only — no AI model is running inside Hanki.", 13.5, UiKit.Res("TextMuted"), wrap: true);
    internal event Action<string>? AiRequested;
    internal string SourceText => source.Text;
    internal string PreviewText => preview.Text;
    internal string StatusText => status.Text;

    private static TextBox Editor(bool readOnly, double height)
    {
        var box = new TextBox { Style = (Style)Application.Current.FindResource("FieldBox"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsReadOnly = readOnly,
            MaxLength = int.MaxValue, Height = height, FontSize = 13.5, Margin = new Thickness(0, 6, 0, 8), VerticalContentAlignment = VerticalAlignment.Top };
        System.Windows.Automation.AutomationProperties.SetName(box, readOnly ? "Prompt preview" : "Report to prepare");
        return box;
    }

    internal PrepareView(IShellServices shell)
    {
        this.shell = shell; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        System.Windows.Automation.AutomationProperties.SetName(question, "Your question");
        stack.Children.Add(UiKit.Text("1. Paste or edit a log below. Review usernames, paths, hostnames, IPv4/IPv6, emails, tokens and customer data.", 14, UiKit.Res("TextMuted"), wrap: true));
        stack.Children.Add(question); stack.Children.Add(source);
        var bar = new WrapPanel();
        Button Add(string text, Action action, bool primary = false) { var b = primary ? Buttons.Primary(text) : Buttons.Secondary(text); b.Click += (_, _) => action(); bar.Children.Add(b); return b; }
        Add("Redact selection", () => { if (source.SelectionLength > 0) source.SelectedText = "[REDACTED]"; });
        Add("Mask common patterns", Mask); Add("Prepare for ChatGPT", Prepare, true); Add("Clear draft", Clear);
        stack.Children.Add(bar);
        var step = UiKit.Text("2. Review the exact prompt below. Pattern masking is incomplete and may alter useful evidence. Edit the source and prepare again.", 14, UiKit.Res("TextMuted"), wrap: true); step.Margin = new Thickness(0, 12, 0, 0);
        stack.Children.Add(step); stack.Children.Add(preview);
        var actions = new WrapPanel(); actions.Children.Add(reviewed); actions.Children.Add(copy);
        var open = Buttons.Secondary("Open ChatGPT"); open.Click += (_, _) => DesktopShortcuts.Open(shell.DialogOwner, "chatgpt"); actions.Children.Add(open);
        var ai = Buttons.Secondary("Use reviewed prompt in in-app AI…");
        ai.Click += (_, _) => { if (reviewed.IsChecked == true && preview.Text.Length > 0) AiRequested?.Invoke(preview.Text); else Blocks.Notice("Hanki Assistant", "Prepare and review the prompt first, then check the review box."); };
        actions.Children.Add(ai);
        stack.Children.Add(actions); status.Margin = new Thickness(0, 6, 0, 0); stack.Children.Add(status); Content = stack;
        copy.IsEnabled = false;
        source.TextChanged += (_, _) => Invalidate(); question.TextChanged += (_, _) => Invalidate();
        reviewed.Click += (_, _) => copy.IsEnabled = reviewed.IsChecked == true && preview.Text.Length > 0;
        copy.Click += (_, _) => {
            if (reviewed.IsChecked != true || preview.Text.Length == 0) return;
            try { System.Windows.Clipboard.SetText(preview.Text); status.Text = "Copied. Paste into your chosen ChatGPT conversation yourself. Nothing uploaded by Hanki."; }
            catch (System.Runtime.InteropServices.ExternalException) { status.Text = "Clipboard busy. Try copying again."; }
        };
    }

    private void Invalidate() { preview.Clear(); reviewed.IsChecked = false; copy.IsEnabled = false; }
    private void Mask()
    {
        try { source.Text = AssistantPrompt.MaskCommon(source.Text); status.Text = "Masked common user-profile paths, emails and IPv4-like strings. IPv6, secrets and other identifiers may remain. Review manually."; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { status.Text = "Masking failed; source retained: " + ex.Message; }
    }
    internal void Prepare()
    {
        try { preview.Text = AssistantPrompt.Build(question.Text, source.Text); reviewed.IsChecked = false; copy.IsEnabled = false; status.Text = "Preview ready. Nothing sent. Clipboard history/sync may retain what you copy; review before copying."; }
        catch (ArgumentException ex) { status.Text = ex.Message; }
    }
    private void Clear()
    {
        if (!Blocks.Confirm("Clear draft", "Discard this local draft? This does not erase clipboard history or reports in other modules.", "Discard draft", true)) return;
        source.Clear(); question.Clear(); status.Text = "Draft cleared. Clipboard unchanged.";
    }

    /// <summary>Loads a report from another page into the draft; false when it is too long or the person keeps their current draft.</summary>
    internal bool LoadReport(string report)
    {
        if (report.Length > AssistantPrompt.MaximumLength) { Blocks.Notice("Hanki Assistant", "Report too long. Paste relevant excerpts (up to 100,000 characters)."); return false; }
        if (source.Text.Length > 0 && !Blocks.Confirm("Hanki Assistant", "Replace your current draft with this report?", "Replace")) return false;
        source.Text = report; status.Text = "Report loaded locally. Review/redact it, then prepare the prompt. No data uploaded.";
        return true;
    }
}

/// <summary>Assistant → In-app AI (optional): send a reviewed prompt to OpenAI with your own API key, after a full review of the request.</summary>
internal sealed class AiChatView : ScrollViewer
{
    private readonly IShellServices shell;
    private readonly PasswordBox key = new() { Width = 260, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(8, 7, 8, 7), Background = UiKit.Res("Canvas"), Foreground = UiKit.Res("TextPrimary"), BorderBrush = UiKit.Res("Border"), CaretBrush = UiKit.Res("TextPrimary") };
    private readonly TextBox model = new() { Style = (Style)Application.Current.FindResource("FieldBox"), Width = 210, Tag = "Your API model ID", Margin = new Thickness(0, 0, 8, 8) };
    private readonly TextBox conversation = new() { Style = (Style)Application.Current.FindResource("FieldBox"), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 280, Margin = new Thickness(0, 6, 0, 8), VerticalContentAlignment = VerticalAlignment.Top };
    private readonly TextBox message = new() { Style = (Style)Application.Current.FindResource("FieldBox"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 150, MaxLength = 100000, Tag = "Paste reviewed evidence or type a follow-up question…", Margin = new Thickness(0, 0, 0, 8), VerticalContentAlignment = VerticalAlignment.Top };
    private readonly TextBlock status = UiKit.Text("Optional OpenAI API connection. API usage has separate billing; a ChatGPT subscription is not an API credential.\nNothing sends automatically. Key and chat stay in this app's memory until cleared/exit. Live integration is not yet validated.", 13.5, UiKit.Res("TextMuted"), wrap: true);
    private readonly Button send = Buttons.Primary("Review / send to OpenAI…"), cancel = Buttons.Secondary("Cancel request"), clear = Buttons.Secondary("Clear chat and key");
    private readonly List<AiMessage> history = [];
    private CancellationTokenSource? pending;
    internal bool IsBusy => pending is not null;
    internal string Draft => message.Text;
    internal string StatusText => status.Text;

    internal AiChatView(IShellServices shell)
    {
        this.shell = shell; VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        System.Windows.Automation.AutomationProperties.SetName(key, "OpenAI API key (session only)"); System.Windows.Automation.AutomationProperties.SetName(model, "Your API model ID");
        System.Windows.Automation.AutomationProperties.SetName(conversation, "Conversation"); System.Windows.Automation.AutomationProperties.SetName(message, "Message to send");
        var stack = new StackPanel { Margin = new Thickness(24, 0, 24, 28) };
        stack.Children.Add(status);
        var config = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; config.Children.Add(key); config.Children.Add(model);
        stack.Children.Add(config); stack.Children.Add(conversation); stack.Children.Add(message);
        var bar = new WrapPanel(); cancel.IsEnabled = false; bar.Children.Add(send); bar.Children.Add(cancel); bar.Children.Add(clear); stack.Children.Add(bar); Content = stack;
        send.Click += async (_, _) => await Send(); cancel.Click += (_, _) => Cancel();
        clear.Click += (_, _) => {
            if (!Blocks.Confirm("Clear AI session", "Clear this local chat, draft and API key? This does not delete any provider-side records.", "Clear", true)) return;
            history.Clear(); conversation.Clear(); message.Clear(); key.Clear();
        };
    }

    internal void Cancel() => pending?.Cancel();

    internal bool LoadDraft(string text)
    {
        if (IsBusy) { Blocks.Notice("Hanki Assistant", "Wait for or cancel the current AI request first."); return false; }
        if (message.Text.Length > 0 && !Blocks.Confirm("Load reviewed report", "Replace the unsent AI draft? Existing chat history will still be included in the next review.", "Replace")) return false;
        message.Text = text; return true;
    }

    private async Task Send()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(message.Text)) return;
        string apiKey = key.Password.Trim(), modelId = model.Text.Trim(), draft = message.Text;
        try {
            var next = history.Append(new AiMessage("user", draft)).ToList();
            var payload = AiClient.Payload(modelId, next);
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("Enter your own API key in the masked field. Do not paste it into the report.");
            if (payload.Contains(apiKey, StringComparison.Ordinal)) throw new ArgumentException("Your API key appears in the request text. Remove it before sending.");
            using var payloadDocument = System.Text.Json.JsonDocument.Parse(payload);
            var review = "DESTINATION: https://api.openai.com/v1/responses\r\nThe JSON below includes ALL prior turns, the new message and instructions. The API key is sent separately in the Authorization header.\r\nstore=false is requested; this is not a guarantee of zero provider retention. API charges may apply, including after cancellation.\r\n\r\n" +
                System.Text.Json.JsonSerializer.Serialize(payloadDocument.RootElement, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            if (!Dialogs.ReviewSend("Review exactly what will be sent", review, "I reviewed this entire request and agree to send it to OpenAI", "Send reviewed request")) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120)); pending = cts;
            send.IsEnabled = clear.IsEnabled = key.IsEnabled = model.IsEnabled = message.IsEnabled = false; cancel.IsEnabled = true;
            using var ticket = shell.Tasks.Begin("AI request", cts.Cancel);
            status.Text = "Sending reviewed request to OpenAI… AI answers may be wrong; no commands will run.";
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var answer = await AiClient.Send(client, apiKey, payload, cts.Token);
            history.Add(next[^1]); history.Add(new("assistant", answer));
            conversation.Text = string.Join("\r\n\r\n", history.Select(m => m.Role.ToUpperInvariant() + "\r\n" + m.Content)); message.Clear();
            status.Text = "Answer received. Verify suggestions before acting; Hanki executes no AI commands. Next send will include this conversation for review.";
        }
        catch (OperationCanceledException) { status.Text = "Request cancelled or timed out. Draft retained; the provider may still process/bill the request. No retry made."; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { status.Text = ex is IOException or ArgumentException ? ex.Message : "AI request failed. Draft retained; check connectivity and model compatibility. No automatic retry."; }
        finally { pending = null; send.IsEnabled = clear.IsEnabled = key.IsEnabled = model.IsEnabled = message.IsEnabled = true; cancel.IsEnabled = false; }
    }
}

/// <summary>Assistant: prepare a reviewed prompt, and the optional in-app AI.</summary>
internal sealed class AssistantPage : NativePage
{
    private readonly SubTabs tabs = new();
    internal PrepareView? Prepare => tabs.ContentOf("Prepare / redact") as PrepareView;
    internal AiChatView? Ai => tabs.ContentOf("In-app AI (optional)") as AiChatView;
    internal bool IsBusy => Ai?.IsBusy == true;
    internal AssistantPage(IShellServices shell)
    {
        tabs.Add("Prepare / redact", "Prepare / redact", () => {
            var view = new PrepareView(shell);
            view.AiRequested += text => { tabs.Select("In-app AI (optional)"); Ai?.LoadDraft(text); };
            return view;
        });
        tabs.Add("In-app AI (optional)", "In-app AI (optional)", () => new AiChatView(shell));
        Content = tabs;
    }
    internal void LoadReport(string text) { tabs.Select("Prepare / redact"); Prepare?.LoadReport(text); }
    internal override void OnShown() { if (tabs.Current is null) tabs.Select("Prepare / redact"); }
    internal override void OnRoute(IReadOnlyList<string> path) { if (path.Count > 1 && tabs.Keys.Contains(path[1])) tabs.Select(path[1]); }
}
