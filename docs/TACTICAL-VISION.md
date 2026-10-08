# Tactical Vision

Open **Gaming → Games**, select or add the actual game executable, then choose
**Tactical Vision…** (under **More** on narrow windows). Enable it and save a
Digital Vibrance percentage. The default is 70%; 50% represents neutral color
and 100% is maximum. Existing game entries stay off until explicitly enabled.

Keep Hanki open while playing. Hanki matches the full executable path of the
foreground process, including games started through another launcher. It boosts
the display containing that window and restores its previous setting within
approximately 750 ms of switching away, changing games, or exiting the game.
Closing Hanki also restores the display. A higher existing vibrance setting is
preserved, and manual changes in NVIDIA Control Panel take precedence on restore.

This requires a display driven directly by NVIDIA with HDR off. The setting
affects the whole display while the game is focused, including other visible
windows. It is a color adjustment, with no claimed FPS improvement. It uses
driver display controls, without injecting code into the game. A launcher or
protected process whose executable path cannot be read does not activate it.

The Games page reports activation and driver errors. Digital Vibrance uses
private NVAPI interfaces, which may be unavailable on some drivers. Their ABI is
documented in the [NVIDIA_NvAPI implementation](https://github.com/jNizM/NVIDIA_NvAPI/blob/master/src/Class_NvAPI.ahk).
Unsupported calls pause the feature until Hanki is reopened.

Before changing colors, Hanki saves the original and requested driver values to
`%LOCALAPPDATA%\IgezziGuard\tactical-vision-restore.json`. After an interrupted
session, reopen Hanki with the same display connected to restore the previous
value. Restoration failures retain that file for a later retry; NVIDIA Control
Panel → Adjust desktop color settings → Digital Vibrance is the manual fallback.
Per-game preferences are saved in the existing local game library.

Validation covers preference compatibility, exact-path matching, strength
conversion, restoring prior colors, manual overrides, interrupted sessions and
driver failures using a fake driver. Actual color output, fullscreen games and
driver compatibility still require verification on an NVIDIA gaming PC.
