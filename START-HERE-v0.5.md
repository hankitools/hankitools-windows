# Hanki Tools v0.5 — Assistant preparation workflow

Build on Windows with .NET SDK 8 or later using BUILD-WINDOWS.cmd. Launch HankiTools.exe in the timestamped dist folder. This is a source preview, not a tested Windows binary release.

## Assistant

There is no embedded AI model or API integration in this version. No credentials, account connection, API charges or automatic uploads are involved. The app builds a local prompt that you may paste into ChatGPT or another assistant yourself.

1. Open Assistant and paste a log; optionally add a question. Or click Prepare for ChatGPT in Shield, Connect or Performance to load the current displayed text. These buttons do not start a new check; inspect its timestamp, completion status and relevance. Introductory/error text may be loaded if no successful check has run.
2. Edit the draft. Select private text and choose Redact selection. Optional Mask common patterns replaces common Windows user-profile names, emails and IPv4-like strings. It is incomplete and can produce false matches; IPv6 addresses, UNC paths, standalone hostnames/usernames, API keys, tokens, passwords and customer data can remain. Masking may remove useful diagnostic context. Inspect the entire text manually.
3. Click Prepare for ChatGPT. The exact prompt appears below, with the report encoded as a JSON string and instructions to treat it as untrusted evidence. This is a prompt convention, not a guarantee against prompt injection in a downstream AI.
4. Review the full preview, check the review box and click Copy reviewed prompt. Editing the source/question invalidates the preview and review approval. Reports over 100,000 characters are rejected rather than shortened during preparation. Questions are limited to 2,000 characters.
5. Open ChatGPT uses only https://chatgpt.com/ with no prompt/query data. Paste manually into your chosen conversation. No clipboard is read automatically, and no commands or AI suggestions can be executed by Hanki.

OS clipboard history, clipboard managers or clipboard sync may retain copied content. Clear draft only clears Hanki's current draft, not clipboard history or module reports. Hanki does not persist Assistant drafts itself; ordinary OS memory behavior still applies. Existing scan history from Shield remains unchanged.

## Windows acceptance checks

- Test source editing, selection redaction, masking and preparing using fake identifiers only.
- Verify copying is disabled before preparation/review, and re-disabled after any source/question edit.
- Paste copied content into a local text editor and compare with the full preview, including Unicode, quotation marks and multiline logs.
- Replace an existing draft via a module button: Cancel preserves it; OK loads the requested report.
- Test blank and oversized reports, a busy clipboard, scrolling, DPI scaling and keyboard focus.
- Open ChatGPT without copying anything: URL must contain no report data and no message should be submitted.

Earlier modules retain their previous functionality/limitations; see START-HERE-v0.4.md and START-HERE-v0.3.md. Live Windows UI/clipboard/browser-launch behavior remains to be tested.
