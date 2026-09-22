# Hanki v0.11 • Action hierarchy

- Main navigation: Home and the five PC workspaces. Left-aligned rows with a selection marker instead of large filled buttons.
- Support & history: Assistant, Recovery and Scan history in a separate section, using smaller text and shorter rows.
- Quick access: collapsed by default; expand to reach PowerShell, CMD and Explorer.
- Primary actions: mint-filled with a 44px minimum height and more padding. Collection/start actions receive emphasis.
- Secondary actions: outlined, 38px minimum height.
- Report, export, Assistant and Windows-settings links: borderless with a 30px minimum height. Keyboard focus remains visible.
- Home: five main tool cards with quiet navigation links. Assistant, Recovery and history move to a compact utility row below.

This is a presentation change. Action confirmations and cancellation behavior remain as before. Dimensions scale with display DPI; AutoSize can grow controls to fit their labels.

Validation: Release cross-build passes with zero warnings/errors. Windows Forms cannot be run in this Linux workspace. On Windows, verify primary/secondary contrast, keyboard focus, high-contrast mode, quick-access expansion and 100/150/200% scaling. Check the title shows v0.11 after running BUILD-WINDOWS.cmd and launching the newly built executable.
