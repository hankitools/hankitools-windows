# Tune my PC: recommendations and sources (HANKI-PERF-320)

Tune my PC asks what you want today, reads the current settings, and shows a plan: the
changes Hanki can make (each reviewed, saved to Recovery, restorable) and the steps only
you can take (BIOS, in-game options, driver switches Hanki can't read). This page is the
contract for `Performance/TunePlanner.cs`: every rule has a reason, and each reason
comes from the sources at the end. "No change" is a valid answer.

## The four choices

| | Gaming + Performance | Gaming + Quality | Creative work | Low power |
| --- | --- | --- | --- | --- |
| For | Competitive games: lowest input lag, most frames | Single-player games: smooth, tear-free, sharp | Editing, rendering, streaming | Cooler, quieter, longer battery |
| Refresh rate | Highest | Highest | Highest | Laptop: 60 Hz (optional); desktop: unchanged |
| HDR | Your choice (no latency effect) | Suggest on only for DisplayHDR 600+ or OLED, then calibrate | Suggest off for SDR color work unless calibrated | Suggest off (power) |
| Adaptive sync (G-SYNC / FreeSync) | On | On | – | – |
| V-Sync (driver) | With adaptive sync: **on**. Without: off | On | – | – |
| Frame cap | With adaptive sync: refresh − 3 | With adaptive sync: refresh − 3 | – | 60 FPS |
| Low Latency Mode (NVIDIA) | On (Reflex overrides it where a game has Reflex) | Game decides | – | – |
| Texture filtering (NVIDIA) | Performance (optional) | High quality, 16x anisotropic (optional) | – | – |
| Preferred refresh rate (NVIDIA) | Highest available | Highest available | – | – |
| Power management (NVIDIA global) | Normal; full clocks per game (Gaming → Games) | Normal | Normal | Normal (never "Prefer maximum" globally) |
| Game Mode | On | On | – | – |
| Optimizations for windowed games | On | On | – | – |
| Variable refresh rate (Windows) | On with an adaptive-sync display | On with an adaptive-sync display | – | – |
| Game Bar background recording | Off | Off (optional) | Off (optional) | Off |
| Mouse acceleration | Off | Off | – | – |
| Windows power mode (plugged in) | Best performance (optional) | Balanced or better | Best performance (optional) | Best power efficiency |
| Maximum processor state (plugged in) | 100% | 100% | 100% | unchanged |
| Hardware-accelerated GPU scheduling | On only for RTX 40/50 (DLSS Frame Generation needs it) | same | – | – |
| Memory at rated speed (XMP/EXPO) | Step: enable in BIOS | Step | Step | – |
| Games on an SSD, free space, TRIM | Steps from the Storage check | Steps | Steps | Steps |

"–" means Tune my PC leaves the setting alone for that choice.

## Why

**Highest refresh rate.** A game can't show more frames than the display refreshes, and
a higher refresh rate lowers the time until a frame appears. The only exception is a
laptop on the Low power choice, where 60 Hz saves battery [1].

**V-Sync is not "always off".** With G-SYNC or FreeSync, the measured best setup is
adaptive sync on, V-Sync **on in the driver** and off in the game, and a frame cap about
3 FPS below the refresh rate: no tearing, and no V-Sync lag because the frame rate never
reaches the refresh rate [2][3]. Games with NVIDIA Reflex cap automatically [3]. Without
adaptive sync, V-Sync adds lag, so competitive players turn it off and accept tearing,
while single-player players usually keep it on [4]. Windows can't tell Hanki whether the
monitor supports adaptive sync, so Tune my PC asks, and leaves V-Sync and caps alone
when you're not sure.

**G-SYNC / FreeSync stays on in every gaming choice.** It removes tearing without V-Sync's
lag [2]. The only common reason to turn it off is backlight strobing (ULMB), which needs
a fixed refresh rate; Hanki doesn't change that.

**Low Latency Mode.** Limits the queue of frames to one when the graphics card is the
bottleneck. Reflex does this better and overrides it, so games with Reflex should use
Reflex [3]. NVIDIA's "Ultra" mode isn't in its public SDK, so Hanki sets "On".

**NVIDIA power management.** "Prefer maximum performance" set globally keeps the card at
3D clocks at the desktop too, adding roughly 15–25 W; set it per game instead [5][6].
The presets offer it as optional, and Tune my PC points to per-game settings.

**HDR.** Worth it on bright displays with local dimming, or OLED; on basic DisplayHDR 400
panels it often looks worse than SDR [7]. Calibrate once with Microsoft's Windows HDR
Calibration app [8]. HDR doesn't add input lag, so competitive play leaves it to you. In
HDR mode Windows converts SDR content, which can look washed out, so color work in SDR
is better with HDR off unless the display is calibrated [8].

**Mouse acceleration off for games.** "Enhance pointer precision" scales pointer movement
by speed, so the same hand movement aims differently at different speeds. Games that
read raw input aren't affected; the rest are [9][10].

**Optimizations for windowed games.** Moves DirectX 10/11 games in windowed and
borderless modes to the flip presentation model: lower latency, and Auto HDR and VRR
work there. Auto HDR requires it [11].

**Game Mode.** On by default. Measurable gains mostly when background apps are busy, or
on modest hardware; negligible on a clean high-end system [12].

**Game Bar background recording.** Windows' own setting says it may affect game
performance: the encoder and disk are busy the whole time. Off unless you use the clips.

**Hardware-accelerated GPU scheduling.** No consistent gain in benchmarks [13], so Hanki
doesn't recommend it generally. DLSS Frame Generation on RTX 40/50 cards requires it.

**Windows power mode.** "Best performance" lets the processor reach higher clocks sooner;
the gain is small when the graphics card is the limit, larger in processor-heavy work;
laptops run warmer and louder [14]. Offered as optional.

**Memory speed (XMP/EXPO).** Memory running below its rated speed costs roughly 4–10% FPS
in processor-limited games; enabling the profile is a BIOS step [15].

**AMD Radeon.** Anti-Lag on and Chill off for competitive play, FreeSync with a frame-rate
target just under the refresh rate rather than Enhanced Sync, Chill for low power [16].
Hanki applies these through AMD's driver interface when it can read Radeon settings, and lists them as
steps otherwise. Radeon support isn't yet tested on AMD hardware.

## What Tune my PC doesn't do

Everything in `Guardrails.NotRecommended` still applies: no overclocking, no disabling
security, no timer, BCDEdit, service, scheduler or network "tweaks", no standby-list
purging, no pagefile removal, no shader-cache clearing. Shader cache size is left at the
driver default, which is already large [17].

## Sources

1. NVIDIA, System Latency Optimization Guide. https://www.nvidia.com/en-us/geforce/guides/system-latency-optimization-guide/
2. Blur Busters, G-SYNC 101: Optimal G-SYNC settings. https://blurbusters.com/gsync/gsync101-input-lag-tests-and-settings/
3. Blur Busters forum, NVIDIA Reflex and low latency mode. https://forums.blurbusters.com/viewtopic.php?t=8645
4. TechSpot, Screen tearing or input lag? To V-Sync or not to V-Sync. https://www.techspot.com/article/2192-screen-tearing-fix-pc-gaming/
5. NVIDIA support, Setting "Power management mode" from Normal to Maximum Performance. https://nvidia.custhelp.com/app/answers/detail/a_id/3130/~/setting-power-management-mode-from-normal-to-maximum-performance
6. BetterFPS, Best NVIDIA Control Panel settings for gaming. https://betterfps.com/blog/best-nvidia-control-panel-settings-gaming
7. PCWorld, Your monitor says it has HDR. That might not mean much. https://www.pcworld.com/article/3128871/your-monitor-says-it-has-hdr-that-might-not-mean-much.html
8. Microsoft Support, Calibrate your HDR display using the Windows HDR Calibration app. https://support.microsoft.com/en-us/windows/hardware/display-graphics/calibrate-your-hdr-display-using-the-windows-hdr-calibration-app
9. Microsoft Learn, Taking advantage of high-definition mouse movement. https://learn.microsoft.com/en-us/windows/win32/dxtecharts/taking-advantage-of-high-dpi-mouse-movement
10. How-To Geek, What is "Enhance Pointer Precision" in Windows? https://www.howtogeek.com/321763/what-is-enhance-pointer-precision-in-windows/
11. Microsoft Support, Optimizations for windowed games in Windows 11. https://support.microsoft.com/en-us/windows/optimizations-for-windowed-games-in-windows-11-3f006843-2c7e-4ed0-9a5e-f9389e535952
12. MakeUseOf, I tested Windows Game Mode on and off for a month. https://www.makeuseof.com/i-tested-windows-game-mode-for-month-what-benchmarks-actually-showed/
13. Gamers Nexus, Windows 10 hardware-accelerated GPU scheduling benchmarks. https://gamersnexus.net/guides/3599-windows-10-hardware-accelerated-gpu-scheduling-benchmarks
14. Microsoft Support, Change the power mode for your Windows PC. https://support.microsoft.com/en-us/windows/change-the-power-mode-for-your-windows-pc-c2aff038-22c9-f46d-5ca0-78696fdf2de8
15. NZXT, How to enable XMP or EXPO (and whether you should). https://nzxt.com/en-intl/blogs/news/how-to-enable-xmp-expo
16. Tier1Settings, Best AMD Radeon settings for gaming. https://tier1settings.com/best-amd-adrenalin-settings-for-gaming/
17. SmoothFPS, NVIDIA shader cache size. https://smoothfps.com/solutions/nvidia-shader-cache
