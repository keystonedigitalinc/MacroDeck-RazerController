# Macro Deck Store submission

Checked against Creator Guidelines **version 6** and the SDK policy on 2026-10-16. Re-read both before every submission, because they change:

- https://api.macro-deck.app/api/v1/public/creator-guidelines (the `X-Creator-Guidelines-Version` header gives the version)
- https://api.macro-deck.app/api/v1/public/dependency-policy/sdk (minimum SDK at that date: `3.0.0-beta.17`)
- https://api.macro-deck.app/api/v1/public/dependency-policy/blocked-packages (empty at that date)

## Status

| Item | State |
| --- | --- |
| Public GitHub repository, MIT `LICENSE` | Done: https://github.com/keystonedigitalinc/MacroDeck-RazerController |
| SDK at or above the Store minimum | Done: `3.0.0-beta.17` |
| Package ID names the developer, not Razer | Done: `com.keystonedigital.razer` |
| Display name implies no official relationship | Done: "Razer App Controller" (describes what it controls; guideline section 5 allows a brand name used descriptively); "Unofficial" leads both descriptions |
| `publisher.name` equals the Creator Portal owner | `Keystone Digital`. **You:** create the "Keystone Digital" organization in the Creator Portal |
| AI declaration (rule 9) | Done: `generatedAssets: true` (icon and text written with AI help), no runtime AI. **You:** declare the same in the Portal |
| Only needed permissions (rule 8) | Done: `device:usb` only |
| Review findings fixed (rule 6) | Done: all important and minor findings from the 2026-10-04 review |
| CI on every push | Done: `.github/workflows/ci.yml`, Windows runner |
| Store release workflow | Done: `.github/workflows/release.yml`, `build-per-platform: true` |
| Publication validation | Done: 0 errors, 0 warnings (`macrodeck-plugin validate --artifact ... --level Publication`) |
| Conformance | Done: 32 passed, 0 failed, 17 skipped (beta.17 CLI) |
| Tested on real hardware by the publisher (rule 6) | Done: Naga Trinity and Cynosa Chroma, Synapse 4.0.827 |
| Screenshots of real use (rule 10) | Done |
| Creator Portal project and GitHub connection | Done: organization Keystone Digital, repo keystonedigitalinc/MacroDeck-RazerController |
| Release `v1.0.0` | Done: built and accepted by the Platform on 2026-10-16 (run 38946903872) |
| Submitted for review | Done: 2026-10-16 |

## Your steps in the Creator Portal

1. Sign in at macro-deck.app and create the organization **Keystone Digital**.
2. In that organization, create a Project of type **Plugin / Integration** with Package ID `com.keystonedigital.razer`.
3. Under **Builds**, select **Connect GitHub**, install the Macro Deck Platform App, and choose `keystonedigitalinc/MacroDeck-RazerController`. The Portal should then find `.github/workflows/release.yml` on `main`.
4. Fill in the Store listing: use the description below, upload 2 or 3 screenshots, and set the AI declaration to match the manifest.
5. Tell me. I then publish GitHub release `v1.0.0`, which builds, tests, runs conformance and uploads the build.
6. Under **Builds**, select **Create release**, then **Add to submission**, then **Submit for Review**. Consider leaving the moderator a note (see "Note for the moderator").

Screenshots: real captures only. Useful ones are:

- the Switch Synapse profile action editor with the device and Cycle profiles
- a deck page with profile, DPI and lighting buttons showing live variable values
- the Synapse control settings screen with its warning

Crop out the desktop and anything unrelated.

## Brief description

For the Portal's short description field:

```text
Unofficial. Control Razer Synapse profiles, lighting, brightness, gaming mode and mouse DPI from Macro Deck, plus Chroma lighting effects with no setup.
```

## Store description

Paste this into the Portal as is. The same text is the manifest description.

```text
Unofficial. Control your Razer devices from Macro Deck while Razer Synapse 4 keeps running.

WHAT IT DOES
- Switch, step or cycle Synapse profiles on any Razer device
- Set lighting effects and brightness saved in your Synapse profile, with each effect's colours, duration or direction
- Chroma lighting without Synapse control: static colour, breathing, spectrum cycling or wave, with brightness and speed
- Toggle a keyboard's gaming mode
- Step or temporarily set your mouse's DPI
- Variables for active profiles, DPI, lighting effect, brightness and gaming mode

REQUIRES
- Windows, Razer Synapse 4 and Macro Deck 3
- For Chroma lighting: the Chroma module in Synapse, with Chroma apps allowed

SETUP
1. Open the plugin's settings (Reconfigure) and turn on Synapse control.
2. Tick "Restart Synapse when I save" and save. Or sign out of Windows and back in instead. (Opening Synapse from the Start menu starts it without the flag.)
Mouse DPI and Chroma lighting work without this. Until you turn it on, the plugin does not change anything and does not use Synapse's debug port.

WHAT SYNAPSE CONTROL CHANGES
- Turning it on adds --inspect=127.0.0.1:9229 to the end of Synapse's startup entry (value RazerAppEngine under HKCU\Software\Microsoft\Windows\CurrentVersion\Run). Nothing else in the entry changes.
- Turning it off removes exactly that flag again.
- The plugin never changes the entry or restarts Synapse on its own. Only when the user presses Restart Razer Synapse (or restarts from the settings screen) with Synapse control on, the plugin adds the flag first and, for one minute after the restart, puts it back if Synapse rewrites its startup entry without it. Synapse does that whenever a program, not Windows, starts it.
- If Synapse drops the flag, razer_status shows flag_missing: save the settings again.
- If Synapse runs without the flag, razer_status shows restart_needed: save the settings with "Restart Synapse when I save" ticked, or sign out and back in.

SECURITY
While Synapse control is on, Synapse opens a debug port on 127.0.0.1:9229. Any program or Windows user on this PC can connect to that port and run code inside Razer Synapse, not only control it. Turn it on only if you trust every program and every user on this PC.
The plugin connects only to 127.0.0.1, checks that the port belongs to Razer Synapse, and sends nothing off the PC. Chroma lighting does not use this port.

LIMITATIONS
- Chroma lighting lasts while Macro Deck runs. In the Chroma lighting action, choose "Synapse lighting (give control back)" to return to your profile.
- A device may not support every Synapse lighting effect.
- Tested with a Razer Naga Trinity and a Razer Cynosa Chroma.

Not affiliated with or endorsed by Razer Inc.
```

## Note for the moderator

> This plugin drives Razer Synapse 4 through Synapse's own Node.js debugging port, because Synapse has no public API for profile switching. It is opt-in: nothing changes until the user turns on "Synapse control", and the settings screen, README and this listing explain the security cost. The plugin connects to 127.0.0.1 only, verifies the process is RazerAppEngine.exe, sends only Synapse's own UI commands (switch profile, set quick effect, set brightness, set gaming mode), reads only device and profile data, and sends nothing off the machine. Mouse DPI uses HID feature reports and works without the debugging port.

## Reply to the review of 2026-10-16 (v1.0.1)

> Thanks for the review. v1.0.1 changes:
>
> - **Synapse restart:** the plugin no longer kills or relaunches Synapse on its own. The background check only reports: `razer_status` shows `flag_missing` or `restart_needed`, and the settings screen explains what to do. The settings screen has a "Restart Synapse when I save" switch, off by default. Synapse restarts only when the user ticks it and saves.
> - **Implicit consent:** removed. A working debug port no longer turns Synapse control on. The plugin does not even connect to the port until the user enables Synapse control in the settings (`razer_status` shows `control_off`). The startup entry is changed only when the user saves the settings screen; the background check never writes it.
> - **Security warning:** the settings screen, README and listing now say that while Synapse control is on, any program or Windows user on this PC can run code inside Synapse through the debug port. They also say exactly what is added to the startup entry and that turning it off removes exactly that flag.
> - **Chroma SDK:** added a **Chroma lighting** action through the Razer Chroma SDK REST server, which works without Synapse control: Off and Static use the SDK's own effects, and Breathing, Spectrum cycling and Wave (with brightness and speed) are drawn by the plugin as custom frames. Profile switching, gaming mode and the other Synapse effects still need the debug port, because the Chroma SDK has no API for them. A Chroma colour is an overlay that lasts while Macro Deck runs, so the Synapse-backed lighting action stays for users who want the change saved in the profile. It talks only to http://localhost:54235.
> - **Permissions:** the manifest now declares `process:spawn`. It is used only to start Synapse again when the user asks for a restart.

## Risks that remain

- **The debugging port (rule 4).** Opt-in and disclosed. A moderator may still decline. The fallback is a DPI-only Store version, with the full plugin kept on GitHub for sideloading.
- **AI-assisted code (rule 6).** It is allowed. You, as publisher, must be able to explain, debug and maintain the code. `docs/superpowers/` holds the design and plan, and every part has tests.
- **The icon (rule 10).** A neon-green gaming mouse over a key cluster, with a sync badge for switching profiles. No Razer or Macro Deck logo. It was generated with AI and is declared in the manifest and the Portal. The source is in `docs/branding/icon-source.png`; the package ships a 512 px copy.
- **Synapse updates.** These can change its internal messages. If profiles stop working after an update, `razer_status` shows `synapse_unrecognised`. Fix it and ship a new version, because approved versions are final.

## Release workflow lessons

- **Single platform:** do not set `build-per-platform: true` while the manifest declares only one platform. In `publish-plugin.yml@v1` the merge job then finds no per-platform folder ("No platform packages were handed over").
- **CLI version:** pin `cli-version` to the Platform's minimum CLI. The workflow's default CLI is older, and the Platform refuses it.
- **Next version:** for a fix after review, bump nothing in the repository. Publish GitHub release `v1.0.1`, because the tag sets the version.
