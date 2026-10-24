# Razer App Controller

An unofficial Macro Deck 3 plugin. Not affiliated with or endorsed by Razer Inc.

Control Razer devices from Macro Deck 3 buttons while Razer Synapse 4 keeps running:

- switch, step or cycle Synapse profiles on any Razer device
- set a device's lighting effect in its Synapse profile, or use Chroma lighting (static, breathing, spectrum, wave, with brightness and speed) without Synapse control
- toggle a keyboard's gaming mode
- step the mouse's DPI, jump to a DPI stage, or set a temporary DPI

Tested on a Razer Naga Trinity and a Razer Cynosa Chroma with Synapse 4.0.827.

## Install

1. Build the package with `macrodeck-plugin build --source src/RazerController --rid win-x64 --output ./artifacts`, or use a release artifact.
2. In Macro Deck 3, install `com.keystonedigital.razer-<version>-win-x64.macroDeckPlugin`. It is not signed, so Macro Deck asks you to confirm and marks it unverified.
3. Open the plugin's settings (**Synapse control**) and turn on **Enable Synapse control**. Also tick **Restart Synapse when I save**, then save. Or skip that and sign out of Windows and back in. Opening Synapse from the Start menu does not work: that shortcut starts it without the flag.

Mouse DPI and Chroma lighting work without step 3. Profiles, Synapse lighting effects and gaming mode need it. Until you turn it on, the plugin does not touch Synapse's startup entry and does not use the debug port, even if the port is already open.

### What Synapse control changes

Turning it on adds `--inspect=127.0.0.1:9229` to the end of the `RazerAppEngine` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Nothing else in that entry changes. Turning it off removes exactly that flag again and leaves the rest of the entry as it is. Both take effect the next time Synapse starts.

The plugin never changes the entry or restarts Synapse on its own. When Synapse is restarted by a program instead of by Windows, it rewrites its startup entry without the flag; so after a restart you ask for (the **Restart Razer Synapse** action, or the settings screen's restart), the plugin puts the flag back for one minute if Synapse drops it. Synapse sometimes rewrites its startup entry and drops the flag at other times too. Then `razer_status` shows `flag_missing`, and you save the settings again to put it back. If Synapse is running without the flag, `razer_status` shows `restart_needed`. Open the plugin's settings, tick **Restart Synapse when I save** and save. Or sign out of Windows and back in.

## Security

While Synapse control is on, Razer Synapse opens a debug port on `127.0.0.1:9229`. **Any program or Windows user on this PC can connect to that port and run code inside Razer Synapse, not only control it.** Turn it on only if you trust every program and every user on this PC.

The plugin connects only to `127.0.0.1`, checks that the port belongs to `RazerAppEngine.exe` before using it, and sends nothing off the PC. Turning the setting off removes the flag; see [What Synapse control changes](#what-synapse-control-changes).

Chroma lighting does not use the debug port. It talks to the Razer Chroma SDK's own local server, the same way games with Chroma effects do.

## Actions

| Action | What it does |
| --- | --- |
| Switch Synapse profile | Picks a device, then one of: a chosen profile, next, previous, or cycle through chosen profiles |
| Synapse lighting (saved to profile) | Off, Static, Breathing, Spectrum, Wave, Reactive, Ripple or Starlight, saved in the Synapse profile, with the effect's own settings: colour, second colour or random colours (Breathing, Starlight), duration (Reactive, Starlight), direction (Wave). Needs Synapse control |
| Synapse brightness (saved to profile) | Sets a device's lighting brightness in its Synapse profile, or makes it brighter or dimmer by a step. 0 switches the lighting off. Needs Synapse control |
| Chroma lighting (temporary, no setup) | Static colour, Breathing (one or two colours), Spectrum cycling, Wave or Off, with brightness and speed, on all Razer devices or one device type, through the Razer Chroma SDK. The plugin draws the animated effects itself. Works without Synapse control. Needs the Chroma module in Synapse with Chroma apps allowed. The colour lasts while Macro Deck runs; **Synapse lighting (give control back)** returns to the profile |
| Gaming mode | On, Off or Toggle. Gaming mode disables the Windows key |
| Step mouse DPI | One DPI stage up or down within the active profile. Through Synapse when Synapse control is on, so Synapse shows and saves it; otherwise directly on the mouse |
| Temporary mouse DPI | A DPI value until the next profile switch or DPI step |
| Go to mouse DPI stage | Jumps to stage 1 to 5 of the active profile, numbered as Synapse shows them. Through Synapse when Synapse control is on; otherwise directly on the mouse, counted from the lowest DPI |
| Cycle Synapse lighting effects | Each press moves a device to the next of the effects you tick (Static, Breathing, Spectrum and Wave when none are ticked). Needs Synapse control |
| Restart Razer Synapse | Closes Synapse and starts it again from its own startup entry, so a change to Synapse control applies. Runs only when you press it |

Each Synapse action waits until Synapse confirms the change, about 1.5 seconds at most. Otherwise it reports "Synapse did not apply the change".

After a profile switch or a DPI stage change made through Synapse, Synapse shows its own on-screen popup ("Profile: <name>", or "DPI Stage <n>" with the DPI bar), the same popup it shows for the mouse's DPI buttons. It disappears after 3 seconds.

## Variables

| Variable | Value |
| --- | --- |
| `razer_status` | `ready`, `control_off`, `flag_missing`, `restart_needed`, `inspector_unavailable`, `inspector_untrusted`, `synapse_unrecognised`, `no_devices` or `starting` |
| `razer_mouse_profile` | Active profile of the first mouse |
| `razer_mouse_dpi` | Current mouse DPI |
| `razer_mouse_dpi_stage` | `stage/count`, `0/n` when on a temporary DPI |
| `razer_keyboard_profile` | Active profile of the first keyboard |
| `razer_keyboard_effect` | `Off`, `Static`, `Breathing`, `Spectrum`, `Wave`, `Reactive`, `Ripple` or `Starlight` |
| `razer_keyboard_gaming_mode` | `true` or `false` |
| `razer_keyboard_brightness` | Lighting brightness of the first keyboard, 0 to 100 |
| `razer_mouse_dpi_stage_number` | Current mouse DPI stage as a number, `0` when on a temporary DPI |
| `razer_mouse_effect` | Lighting effect of the first mouse, same values as `razer_keyboard_effect` |
| `razer_chroma_effect` | `static`, `breathing`, `spectrum`, `wave` or `off` while Chroma lighting is shown, `synapse` while Synapse has the lighting |
| `razer_mouse_brightness` | Lighting brightness of the first mouse, 0 to 100 |

## Troubleshooting by `razer_status`

- **`control_off`**: Synapse control is off in the plugin's settings. Mouse DPI and Chroma lighting still work.
- **`flag_missing`**: Synapse removed the flag from its startup entry. Open the plugin's settings and save them again.
- **`restart_needed`**: Synapse is running without the flag. Open the plugin's settings, tick **Restart Synapse when I save** and save. Or sign out of Windows and back in.
- **`inspector_unavailable`**: Synapse is not running or not listening on port 9229. Start Synapse. The plugin reconnects on its own.
- **`inspector_untrusted`**: something other than Razer Synapse is using port 9229. The plugin refuses to talk to it.
- **`synapse_unrecognised`**: Synapse is reachable, but its device windows were not found. A Synapse update may have changed them. Check the host log in `%APPDATA%\MacroDeck\logs`.
- **`no_devices`**: Synapse reports no connected Razer devices.

## Development

```
dotnet build
dotnet test
dotnet run --project tools/ManualInspectorCheck -- --roundtrip   # live: switches and restores, needs Synapse
macrodeck-plugin test --project src/RazerController --report markdown --output conformance.md
```

`docs/superpowers/specs/` holds the design. `docs/superpowers/plans/` holds the implementation plan.
