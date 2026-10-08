# v0.19 laptop test plan (0.19.0)

Hands-on checks for the Tune my PC additions in 0.19.0 that the desktop couldn't cover: they need a laptop
(battery, hybrid graphics, a built-in screen, usually 125–200% scaling). Every change below goes through the review
dialog and is saved in Recovery first; each test ends by undoing it. Record results in the table at the end.

## Setup

- **To try the app only:** download `HankiTools-0.19.0…zip` from the
  [release page](https://github.com/hankitools/hankitools-windows/releases/tag/v0.19.0), compare its SHA-256
  with the `.sha256` file, unzip and run `HankiTools.exe`. It's self-contained, so no .NET install is needed.
  The exe is code-signed; if SmartScreen still warns (a new publisher can take time to build reputation), choose More info → Run anyway.
- **To run the checks or build from source:** install Git, the .NET 10 SDK (10.0.4xx) and the GitHub CLI, then
  `git clone https://github.com/hankitools/hankitools-windows`. `dotnet run --project tests\HankiTools.Checks.csproj`
  should print "All non-destructive checks passed" (737 checks at 0.19.0).
- Note before starting: Windows version, laptop model, processor, graphics (integrated and dedicated), screen
  resolution and scaling, whether Settings shows separate power modes for "Plugged in" and "On battery", and the
  Energy saver level (Settings → System → Power & battery → Energy saver).
- Test on a standard (non-administrator) run unless a step says otherwise. Don't change drivers or the BIOS for these
  tests.

## Tests

| ID | How | Expected |
| --- | --- | --- |
| L1 Graphics mode (plugged in) | Tune my PC → Gaming + Performance → Scan. | On a laptop whose screen runs through the integrated GPU (most Optimus laptops): a "Laptop graphics mode" step under **Hardware and BIOS**, optional, naming the dedicated GPU. With the screen already on the dedicated GPU (MUX/Advanced Optimus): no step. Wording makes sense for this laptop's maker. |
| L2 Graphics mode (battery) | Unplug. Tune my PC → Low power → Scan. | If the screen is on the dedicated GPU: a "Hybrid" step. Otherwise none. |
| L3 Power mode on battery | On battery, Low power → Scan → Review and apply the **Power mode** change only (if the plan is Balanced). | Settings → Power & battery shows "Best power efficiency" **on battery**; the plugged-in mode is unchanged. Recovery lists the change. Undo it **while still on battery**: the battery mode returns to what it was. Then try undo while plugged in on a second run and note what happens (known limitation: the undo sets the mode for the current power source). |
| L4 Energy saver level | On battery or plugged in, Low power → Scan → apply **Energy saver turns on at** only. | Settings → Energy saver shows "Turn energy saver on automatically at 50%". The active power plan doesn't change (`powercfg /getactivescheme` before and after). Undo in Recovery restores the old level (often 20% or 30%, or "Never"). |
| L5 Plug-in step | On battery, Tune my PC → Gaming + Performance → Scan. | A "Plug in the charger" step; no Energy saver item. |
| L6 Network | On Wi-Fi, Gaming + Performance → Scan. | Optional "A network cable" step and an optional "Downloads while you play" step under **Network**; "Open settings" on the downloads step opens Delivery Optimization's advanced options. With a cable at 1 Gbps: the wired connection is listed under "What's already right". |
| L7 Driver age | Gaming + Performance → Scan. | Driver under 6 months old: "Graphics driver age" under "What's already right". Older: a step with the vendor's update instructions. |
| L8 Creative color | Creative work → Scan. | The built-in screen never gets a 6-bit color-depth step. On Windows 11 24H2+ with a wide-gamut screen and "Automatically manage color for apps" off: an optional step whose "Open settings" goes to Advanced display. Check against Settings that the on/off state Hanki reports is right. |
| L9 Processor helpers | Any gaming choice → Scan. | Ryzen 9 HX3D laptops: an "AMD 3D V-Cache Performance Optimizer" item. Intel Core i9/i7-14x00HX or Core Ultra 200HX/300H: an "Intel Application Optimization" step only if Intel Dynamic Tuning isn't installed (check Services for "Intel(R) Innovation Platform Framework"). Other processors: neither. |
| L10 Per-game advice | Tune my PC → Gaming → Games: Launch and measure one game (run Hanki as administrator for frame rates). Then Gaming + Performance → Scan. | A "Measured: <game>" item under **In your games** matching the Launch and measure verdict (for example textures one step lower when video memory was full). A run from before 0.19 gives no item. |
| L11 OBS | If OBS Studio is installed: Creative work → Scan. | A profile set to x264 gets an optional "OBS encoder" step naming NVENC, AMF or Quick Sync; one already on a hardware encoder is under "What's already right". Nothing in OBS changes. |
| L12 Scaling | Look through Home, Fix my PC, Tune my PC and its plan at the laptop's usual scaling (and 150%/200% if possible). | Nothing clipped, overlapping or cut off; the plan's lines wrap. Screenshot anything odd. (Still open from VALIDATION-v0.18.md.) |

## Results

| ID | Result | Notes |
| --- | --- | --- |
| L1 | Not tested | |
| L2 | Not tested | |
| L3 | Not tested | |
| L4 | Not tested | |
| L5 | Not tested | |
| L6 | Not tested | |
| L7 | Not tested | |
| L8 | Not tested | |
| L9 | Not tested | |
| L10 | Not tested | |
| L11 | Not tested | |
| L12 | Not tested | |
