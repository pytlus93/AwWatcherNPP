# ActivityWatch for Notepad++

A Notepad++ plugin that records active file and language context in local [ActivityWatch](https://activitywatch.net). It samples while the editor is foreground and stops after the configured Windows input-idle timeout. It does not capture document contents.

## Features

- Tracks saved files and unsaved tabs while Notepad++ is foreground and active.
- Optionally detects Git, Mercurial and Subversion project roots, including Git worktrees.
- Supports custom language mappings and individual context/privacy controls.
- Delivers activity locally, with background retries when ActivityWatch is unavailable.
- Provides x64 and x86 plugins for Windows.

## Requirements

- Windows with .NET Framework 4.8.
- Unicode Notepad++ x64 or x86; ARM64 is not supported.
- ActivityWatch running locally, normally on port `5600`.

Both architectures have build and synthetic protocol/native-export evidence. The installed older plugin has real event receipt; acceptance of the current DLL inside Notepad++ remains separate. See [validation](VALIDATION.md).

## Install or upgrade

Build a package or check [repository releases](https://github.com/pytlus93/AwWatcherNPP/releases) for a matching archive. Close Notepad++, then extract the `AwWatcherNPP` folder into its `plugins` directory. For a standard x64 installation:

```text
C:\Program Files\Notepad++\plugins\AwWatcherNPP\AwWatcherNPP.dll
```

Replace the matching-architecture DLL when upgrading; settings are stored separately. Restart Notepad++ and start ActivityWatch normally. **Plugins → ActivityWatch → Open ActivityWatch** opens the dashboard. Inspect `aw-watcher-notepadpp_<hostname>` in Raw Data to verify receipt.

## Configuration and everyday use

Open **Plugins → ActivityWatch → Settings…**. Save applies settings; Cancel discards edits. Disabling tracking stops sampling/delivery, discards pending observations and persists across editor restarts.

![ActivityWatch settings dialog with a language-mapping table](assets/settings.png)

| Setting | New configuration default |
| --- | --- |
| Tracking | Enabled |
| Port / Testing | `5600` / off; testing maps default port to `5666` |
| Heartbeat / pulse time | 5 / 20 seconds |
| Idle timeout | 180 seconds |
| Pending capacity | 1000 observations in memory |
| Payload | Strict; file and language enabled; project path and extended fields off |

Strict sends editor fields `file`, `project`, `language` plus this plugin's readable `title`; disabled standard context becomes an empty string. Extended mode adds selected `filename`/`project_name`. Privacy choices also restrict titles. See [the field catalog](DATA-CONTRACT.md) for defaults and migration.

Language mappings use one extension and name per table row (for example `tpl` → `Template`). Matching ignores case; duplicate extensions, wildcards and compound extensions are rejected. New configurations have no custom overrides.

Unsaved tabs use their host tab label, with `[unsaved]` in the title, and an empty project. Saved files can optionally use the nearest VCS root or parent directory as project context. Labels are not stable document identities.

Settings are in `AwWatcherNPP.json` in the host's plugin configuration directory; the fallback is `%APPDATA%\AwWatcherNPP`. Earlier JSON settings remain supported. Changing privacy choices does not erase existing AW history.

## Tracking behavior

File and language changes trigger an immediate observation. While Notepad++ is
foreground, sampling continues until the Windows input-idle timeout. Switching to
another application or becoming idle stops collection; returning starts a new
interval. Missing collection is left as a gap.

When ActivityWatch is unavailable, queued observations retry in order with their
original timestamps. The queue is bounded; when full, new observations are dropped.
Closing or crashing Notepad++ loses pending observations because the queue is kept
in memory.

## Troubleshooting

Use **Plugins → ActivityWatch → Status** for delivery/queue information. Check DLL architecture, server port, tracking enablement, foreground state and idle timeout. Invalid settings disable capture until corrected.

`AwWatcherNPP.log` sits beside the config and excludes paths/document contents. Pending observations are memory-only, bounded and lost when the host exits. Delivery retries preserve observation times; collection gaps remain gaps. Combine editor data with AW window/AFK information rather than summing overlapping watcher durations.

## Local development

Requires MSBuild and the .NET Framework 4.8 developer pack. The first build downloads pinned DllExport tooling if absent.

```powershell
./scripts/build.ps1 -Platform x64 -Test -Package
./scripts/build.ps1 -Platform x86 -Test -Package
```

Packages are written to `dist/`. [Validation](VALIDATION.md) distinguishes synthetic HTTP/native checks, installed receipt and outstanding host/UI/reboot acceptance. [The data contract](DATA-CONTRACT.md) describes transport boundaries and privacy.

## Thanks

Thanks to **Alan Hamlett** and the contributors to the
[WakaTime Notepad++ plugin](https://github.com/wakatime/notepadpp-wakatime), whose
plugin integration provided the starting point for this watcher.

## License

[BSD 3-Clause](LICENSE.txt), including adapted integration code attributed to Alan Hamlett and contributors to the WakaTime Notepad++ plugin.
