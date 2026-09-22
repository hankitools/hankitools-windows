# Hanki v0.14 • Charcoal workspace

Every module now uses a neutral charcoal workspace (#202225), slightly lighter report/input/card surfaces (#292B2E), and neutral raised controls (#36393D). Text is warm white (#F2F1ED); secondary text is neutral gray (#B8BBBE). Primary actions and active indicators retain mint (#97E1C9). The sidebar keeps its pine background (#101B1C).

The shared ToolPage report area no longer has a bright native textbox border. This includes dump analysis and the other ToolPage-based modules. Native scrollbars remain Windows controls. Windows high-contrast colors still take precedence.

Release cross-build: zero warnings/errors. This color-only/layout patch does not change collection or action logic. Native appearance requires Windows verification. After rebuilding, check report areas, summary cards, input fields, dialogs and sidebar contrast, including disabled/selected controls.

Close the old instance, run BUILD-WINDOWS.cmd, launch from the newly generated dist directory and confirm v0.14 in the title.
