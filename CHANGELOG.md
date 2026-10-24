# Changelog

## 1.0.0 — 2026-10-24

- First versioned release of the current Notepad++ plugin, for Windows x64 and x86.
- Foreground/idle-gated editor tracking, unsaved-tab labels, configurable privacy,
  language mappings, testing mode and custom local server port.
- Ordered bounded retry queue and headers-only POST completion, including existing
  bucket initialization and producer restart after a bodyless HTTP 304.
- Add DLL version metadata and version-derived release ZIPs with SHA256 sidecars.
- Keep `aw-watcher-notepadpp` client/bucket identity and `AwWatcherNPP.dll` host identity.

Earlier development implemented the tracking and transport features above; this
release adds versioning, packaging and convention documentation. See VALIDATION.md
for synthetic, deployed-server and remaining installed-host acceptance evidence.
