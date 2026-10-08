# UI localization

Hanki supports the same language choices as the website articles: English (`en`), Finnish (`fi`), German (`de`), Spanish (`es`), French (`fr`), Italian (`it`), Japanese (`ja`), Korean (`ko`), Dutch (`nl`), Polish (`pl`), Brazilian Portuguese (`pt-BR`), and Simplified Chinese (`zh-Hans`).

Use **Language / Language** at the bottom of the sidebar to select a language. The setting applies the next time Hanki opens, so an active scan or repair is not interrupted. The selector always lists languages by their native names and retains the English word “Language” for discoverability. **Use Windows language** restores automatic selection.

On first launch, Hanki follows the Windows UI language. Regional variants resolve to a supported language, with Portuguese using Brazilian Portuguese. Simplified Chinese Windows variants use `zh-Hans`; unsupported languages, including Traditional Chinese, fall back to English. Windows regional number and date formatting is preserved independently of the app language.

## Coverage and remaining work

The embedded catalogs currently contain 351 messages per language. This change localizes the navigation, page introductions, tool cards, main home and tuning choices, tab labels, main tool actions, language dialog, common report controls, common accessibility labels, and tool search labels. Search retains English terms alongside translated tool names and suggested search terms.

This is **not a complete translation of every screen and diagnostic explanation**. Detailed diagnostic reports, repair explanations, guided troubleshooting steps, some secondary forms and status messages still use the existing English text. Missing translations intentionally display their English source. Windows commands, evidence, application names, paths, route IDs, persisted records, and native Windows dialogs are not rewritten. The catalogs should receive native-speaker review before a localized release, especially descriptions of actions that change settings.

## Maintaining translations

Catalogs are UTF-8 JSON embedded from `src/IgezziGuard/Localization/Catalogs`. English source text is the lookup key. Add each message to every catalog, including `en.json`, and call `Localizer.T` at the presentation boundary. Use `Localizer.Format` for messages with arguments; retain the same numbered placeholders in every translation. Translate complete sentences instead of concatenating grammatical fragments.

Keep internal identifiers separate from display text. In particular, `NavigationItem.Page`, `TabPage.Text` route names, `ToolLauncher.Route.Name`, enum values, command arguments, and saved evidence must remain stable. `Localizer.Route` supplies translated route labels without changing the target. Do not translate whole Windows command output or replace words inside collected reports.

The preference is saved atomically to `%LOCALAPPDATA%\IgezziGuard\language.json`. Missing, invalid or unsupported preferences revert to the Windows language. Catalog loading is offline and adds no service dependency.

## Validation

```powershell
dotnet build src/IgezziGuard/IgezziGuard.csproj
dotnet run --project tests/HankiTools.Checks.csproj -- --localization-checks
dotnet run --project tests/HankiTools.Checks.csproj
```

The focused checks cover all language catalogs and placeholders, regional matching, missing/invalid preferences, persistence, fallback, localized search terms, stable navigation IDs, and preservation of regional formatting. They also run as part of the full suite.

For each language, run the opt-in UI check with a report path and optional screenshot folder:

```powershell
HankiTools.exe --ui-smoke-test-localized fi ui-fi.json screenshots-fi
```

This override does not save or change the user's language preference. It visits the existing routes at two window sizes, validates the 13-option language selector, and can capture localized home, search, tool, and language-dialog screenshots. Pixel layout and translation quality still need visual review.
