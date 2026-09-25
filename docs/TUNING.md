# Tune my PC: recommendations and sources (HANKI-PERF-320)

Tune my PC asks what you want today, reads the current settings, and shows a plan: the
changes Hanki can make (each reviewed, saved to Recovery, restorable) and the steps only
you can take (BIOS, in-game options, driver switches no app is allowed to change). This page is the
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
| Auto HDR | – | On with HDR on (optional) | – | – |
| Laptop graphics mode (MUX / Advanced Optimus) | Screen on the graphics card (optional step) | same | – | Hybrid (step) |
| Graphics driver age | Step at 6 months (optional), a year (required) | same | – | – |
| Network | Wired at 1 Gbps; cable over Wi-Fi (optional); pause downloads while playing (optional) | – | – | – |
| PCIe lanes, Resizable BAR | Steps from the Gaming check; Resizable BAR optional | same | Lanes only | – |
| Color signal | – | – | Steps: RGB instead of YCbCr 4:2:2/4:2:0; 8 bits or more on external displays | – |
| Automatic color management | – | – | On, in SDR (optional step) | – |
| OBS encoder | Hardware encoder instead of x264 (optional step) | same | same | same |
| Your measured games | Advice from each game's latest Launch & measure run | same, quality-minded | – | – |
| Variable refresh rate (Windows) | On with an adaptive-sync display | On with an adaptive-sync display | – | – |
| Game Bar background recording | Off | Off (optional) | Off (optional) | Off |
| Mouse acceleration | Off | Off | – | – |
| Windows power mode (plugged in) | Best performance (optional) | Balanced or better | Best performance (optional) | Best power efficiency |
| Windows power mode on battery (laptop) | – | – | – | Best power efficiency |
| Energy saver starts at (laptop) | – | – | – | 50% battery (optional) |
| AMD Ryzen 9 X3D with two core groups | Step: AMD's 3D V-Cache optimizer; Balanced power plan | same | – | – |
| Intel APO (processors Intel lists) | Step: Intel Dynamic Tuning Technology (optional) | same | – | – |
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
a fixed refresh rate; Hanki doesn't change that. On NVIDIA, Hanki reads whether G-SYNC is on
for the main display (NVAPI `NvAPI_Disp_GetVRRInfo`): when it is, "Not sure" is answered
as Yes and there is no step. NVIDIA's public interface has no function to switch G-SYNC on,
so when it's off that stays a step.

**Low Latency Mode.** Limits the queue of frames to one when the graphics card is the
bottleneck. Reflex does this better and overrides it, so games with Reflex should use
Reflex [3]. NVIDIA's "Ultra" mode isn't in its public SDK, so Hanki sets "On".

**NVIDIA power management.** "Prefer maximum performance" set globally keeps the card at
3D clocks at the desktop too, adding roughly 15–25 W; set it per game instead [5][6].
The presets offer it as optional. Gaming + Performance offers it for each of your games
(Gaming → Games, up to 12 at a time) as optional per-game changes, the same change
Optimize this game makes.

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
laptops run warmer and louder [14]. Offered as optional. Hanki changes it the way
Settings → System → Power does, on the Balanced power plan (the only plan it applies to);
with another plan it stays a step.

**Memory speed (XMP/EXPO).** Memory running below its rated speed costs roughly 4–10% FPS
in processor-limited games; enabling the profile is a BIOS step [15]. When memory ran short
earlier (the commit peak is above today's limit, so the pagefile grew), the gaming choices
add an optional step to close programs before playing.

**Auto HDR.** Adds HDR to DirectX 11 and 12 games made for SDR, and needs HDR switched on
[18]. Offered only for Gaming + Quality with HDR on, and optional, since some games look
better without it. Hanki sets the same `AutoHDREnable` flag Settings uses.

**Laptop graphics mode.** On most gaming laptops the screen is wired through the integrated
graphics, so every frame is copied across. A MUX switch, or NVIDIA Advanced Optimus,
connects the screen straight to the graphics card; ASUS measured about 9% more frames on
average, over 30% in some games, while hybrid mode can more than double battery life [19].
The switch lives in the maker's app or NVIDIA Control Panel, so it's a step: optional for
gaming when the screen is on the integrated GPU, and "back to hybrid" for Low power when
it's on the graphics card.

**Graphics driver age.** NVIDIA, AMD and Intel release game fixes and optimizations about
monthly. At six months old the driver is an optional step, after a year a required one.
Only judged when Hanki knows the scan time.

**Network (Gaming + Performance).** A wired link below 1 Gbps, or in half duplex, usually
means a bad cable or port (the Network connection speed check explains it). Wi-Fi's
latency varies far more than a cable's, and other devices on the network make it worse
[20], so a cable is an optional step. Game launchers and Windows Update downloading in the
background fill the connection; Steam can pause downloads during gameplay, and Delivery
Optimization can limit Windows' background bandwidth [21].

**PCIe lanes and Resizable BAR.** The Gaming check's hardware findings join the plan: a
card on x4 or fewer of its lanes is a step (move it to the top slot); Resizable BAR off is
optional for gaming, since only some games gain a few percent [22].

**Power on battery and Energy saver.** Windows keeps one power mode for plugged in and one
for battery, and Hanki sets the one for the current power source, so Low power on battery
now sets the battery mode. Energy saver dims the screen and pauses background apps, sync
and non-critical updates on battery [25]; Low power offers to start it at 50% instead of
the default 20–30%. Hanki changes the plan's battery setting (`ESBATTTHRESHOLD`) and
records the old level in Recovery; it never switches the active plan.

**AMD Ryzen 9 X3D.** The 7900X3D, 7950X3D, 9900X3D, 9950X3D and the HX3D laptop chips
have two core groups, and only one has 3D V-Cache. AMD's chipset driver (the 3D V-Cache
Performance Optimizer) parks the other group while a game runs, which needs the Balanced
power plan and Game Bar [23]. Hanki checks that the driver is installed and the plan is
Balanced. Single-group X3D chips (7800X3D, 9800X3D) don't need it.

**Intel APO.** Application Optimization steers the threads of games on Intel's list across
performance and efficiency cores. Intel lists full support for 14th-gen K and HX, and Core
Ultra 200S K, 200HX and 300H processors; it runs inside Intel Dynamic Tuning Technology
from the motherboard or laptop maker [24]. Hanki suggests it (optional) on those processors
when Dynamic Tuning isn't installed; others need Intel's Advanced Mode, so Hanki stays quiet.

**Color for creative work.** Windows reports the signal's color encoding and depth.
YCbCr 4:2:2 or 4:2:0 carries color at a lower resolution than brightness, so colored text
and fine edges blur; 6 bits per color shows banding. Both usually come from cable
bandwidth, and the driver's RGB / 8 bpc settings fix them. On Windows 11 24H2 Hanki also
reads automatic color management, which maps every app's colors to a wide-gamut display so
sRGB content isn't oversaturated [26]; it's an optional step in SDR.

**OBS encoder.** x264 encodes on the processor and takes time from the game or the edit;
NVENC, AMF and Quick Sync run on a separate video engine [27]. Hanki reads OBS's profiles
(Simple and Advanced output) and suggests the hardware encoder for any profile on x264.
Nothing is changed in OBS.

**Your measured games.** Launch & measure now saves what limited each run. The gaming
choices turn each game's latest run (last 90 days) into advice for that game: textures one
step lower when video memory was full; upscaling or lighter graphics when the graphics card
was the limit; lighter processor settings, or for Gaming + Quality higher graphics
settings for free, when the processor was; and cooling, SSD or background steps for the
rest. Mixed results give no advice.

**AMD Radeon.** Anti-Lag on and Chill off for competitive play, FreeSync with a frame-rate
target just under the refresh rate rather than Enhanced Sync, Chill for low power [16].
Hanki applies these through AMD's driver interface when it can read Radeon settings, and lists them as
steps otherwise. Radeon support isn't yet tested on AMD hardware.

**In-game settings stay a step.** Each game keeps its own settings in its own files and
formats, often rewrites them on exit, and some anti-cheat systems check them. Reflex,
in-game V-Sync and quality presets are therefore listed for you to set.

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
18. Microsoft Support, Use Auto HDR for better gaming in Windows. https://support.microsoft.com/en-us/windows/hardware/display-graphics/use-auto-hdr-for-better-gaming-in-windows
19. ASUS ROG, How to maximize your ROG laptop's performance with the MUX Switch. https://rog.asus.com/articles/rog-gaming-laptops/maximize-your-rog-laptops-performance-with-a-mux-switch/
20. jitter.is, Ethernet vs Wi-Fi: the jitter difference. https://jitter.is/blog/ethernet-vs-wifi-jitter/
21. Microsoft Support, Delivery Optimization in Windows. https://support.microsoft.com/en-us/windows/deployment/updates-lifecycle/delivery-optimization-in-windows
22. Intel Support, What Is Resizable BAR and How Do I Enable It? https://www.intel.com/content/www/us/en/support/articles/000090831/graphics.html
23. Hardware Busters, AMD Ryzen 9 7950X3D core parking problem and solution. https://hwbusters.com/cpu/amd-ryzen-9-7950x3d-core-parking-problem-solution/
24. Intel Support, Intel Application Optimization overview. https://www.intel.com/content/www/us/en/support/articles/000095419/processors.html
25. Microsoft Learn, Energy Saver. https://learn.microsoft.com/en-us/windows-hardware/design/component-guidelines/energy-saver
26. DirectX Developer Blog, Advancing the state of color management in Windows. https://devblogs.microsoft.com/directx/auto-color-management/
27. NVIDIA, NVIDIA NVENC OBS Guide. https://www.nvidia.com/en-us/geforce/guides/broadcasting-guide/
