# Validation — 2026-10-11

## Release 1.0.0 audit — 2026-10-24

Reviewed the official watcher-writing and editor-contract documentation linked below.
Client and bucket prefix already follow `aw-watcher-notepadpp`; retain the existing
repository, DLL and installation-folder identities. Because this is a host plugin,
Settings exposes Testing and Port instead of standalone CLI flags. Testing maps
the default 5600 to 5666; an explicit nondefault port takes precedence. Existing
tests cover these settings, privacy/title redaction, UTC timestamps, schema checks,
producer restart, delayed server availability and resumed heartbeat delivery.
The throwing bodyless-304 content regression covers nonzero Content-Length without
buffering; heartbeat 304 and incompatible bucket types remain errors.

Fresh x64 and x86 `scripts/build.ps1 -Platform <architecture> -Test -Package`:
PASS, 106 assertions per architecture, including native DLL exports and synthetic
loopback HTTP. No compiler warnings were reported. The first restricted x64 run
was blocked by loopback socket permissions; successful runs used approved execution
outside the sandbox and checkout-local TEMP/TMP. Release packaging verifies DLL
product version 1.0.0 and file version 1.0.0.0 and writes SHA256 sidecars.

This release changes version metadata, packaging and documentation; collection and
transport behavior are unchanged. No new installed-editor, reboot, interactive UI
or deployed-server acceptance was performed. Earlier deployed-server evidence below
remains historical evidence, not a fresh 1.0.0 host acceptance claim.

## Build and synthetic evidence

Visual Studio installation 18.10.12224.181, MSBuild, .NET Framework 4.8 target,
pinned DllExport 1.7.4. Both x64 and x86:

- `scripts/build.ps1 -Platform <architecture> -Test -Package`: PASS, 99 assertions
  each, no compiler warnings. Packages remain ignored under dist/.
- Native DLL load, six exports, Unicode marker, menu stride and repeated menu access.
- In-memory HTTP handler: delayed availability, bounded queue, genuine bucket/heartbeat
  failures, resumed delivery, original UTC timestamps, cancellation, ordered backlog
  transfer on settings change, restart, incompatible existing bucket rejection.
- Real loopback TCP: first creation 200, existing creation 304 followed by type GET,
  heartbeat 404 and bucket recreation, producer restart, heartbeat 304/500 rejection,
  subsequent successful delivery, redirects rejected, capture timestamps and
  zero-pulsetime collection boundaries. Server outage here means synthetic endpoint
  responses/handler disconnection, not restarting the deployed server.
- Tracker: throttling, file/language/focus changes, no background/empty-context samples,
  focus-return and delayed-sample boundaries. Overflow marks the next accepted sample
  as a boundary. Notification/sample failures reset tracker continuity in source.
- Settings: legacy JSON privacy defaults, round trips, validation, strict/extended
  choices, title/basename redaction and explicit stop setting round trip.

Initial restricted runs failed on temporary-file rename and forbidden loopback
connections. Final tests used an ignored checkout-local TEMP/TMP and approved
execution outside the sandbox. These environment failures are not counted as passes.

## Settings UI evidence

Controls instantiated offscreen only, without interactive testing or installation.
`--preview assets/settings.png`: PASS, 103 assertions including bounds checks;
render visually inspected with all new controls and Save/Cancel visible at default
size. Full host UI, DPI scaling and minimum-size resizing remain unverified.

## Deployed AW evidence

Read-only info endpoint reports AW v0.13.2 on port 5600. Explicit opt-in test:

`tests/bin/x64/AwWatcherNPP.Tests.exe bin/x64/Release/AwWatcherNPP.dll --aw 5600`

PASS, 104 assertions. A unique synthetic validation bucket exercised first creation,
matching heartbeat merge (5 seconds), a new producer initializing against the same
bucket, and a resumed observation at +60 seconds with pulsetime=0. API returned two
events, preserving the 55-second gap, with expected file/title values. API data keys
were file,project,language,title. All temporary validation buckets were removed;
final count zero. No installed watcher bucket or existing events were modified.

The opt-in harness initially stalled after UI tests installed a synchronization
context; moving the async server check before UI checks fixed it. Initial cleanup
was rejected because production-mode AW requires force=1. Cleanup now uses force=1
only for its exact newly generated synthetic bucket, including finally cleanup.

Actual creation-304 status is explicitly asserted in synthetic tests. Deployed AW
restart delivery succeeded, but the production server was not restarted or stopped.
GUI tooltip ordering/custom visualization is unverified. The earlier user screenshot
is receipt evidence for an earlier build and is not proof of this build's contract.

## Recovery architecture and remaining checks

This DLL loads with Notepad++; AW does not launch it as a standalone module. A host
crash loses the in-memory queue; reopening the editor reloads its plugin. No watchdog
relaunches the editor. Failed editor launch and Windows login/reboot behavior belong
to the editor/environment, and were not tested. No PC restart, live process termination,
plugin replacement, or interactive host launch was performed.

Source guards initialization against an existing worker. Settings replacement cancels
and joins the previous worker before creating one replacement. Enable tracking=false
stops sampling/delivery and drops backlog, persists across host reload and does not
schedule automatic restart. Actual stop/re-enable/no-duplicate behavior in an installed
editor remains unverified; cancellation and UI persistence are tested in isolation.

Foreground window and Windows input idle gate sampling. Lock normally changes foreground;
long suspend/sample gaps start a new interval. Actual lock/suspend, editor crash/reload,
login after hard reboot, both panes, and idle/focus transitions inside Notepad++ remain
manual validation requirements. There is no real-host evidence for the revised DLL.

Queue is memory-only and delivery is at-least-once: ambiguous HTTP completion can
replay a timestamp, and exiting/crashing discards pending observations. No durable
replay/deduplication guarantee is claimed. Collection intervals stop at the last
observation (up to one configured heartbeat interval of undercount); no stale tail
is fabricated at focus loss. Uninterrupted queued collection during an AW outage
is retained; missing collection is represented as a new interval.

## Protocol references

Reviewed official [watchers](https://docs.activitywatch.net/en/latest/watchers.html),
[writing a watcher](https://docs.activitywatch.net/en/latest/examples/writing-watchers.html),
[data model](https://docs.activitywatch.net/en/latest/buckets-and-events.html), and
[REST API](https://docs.activitywatch.net/en/latest/api/rest.html).
See DATA-CONTRACT.md for the official editor schema versus workspace additions,
privacy controls, migration, unavailable behavior and presentation limitations.

## Unsaved-tab follow-up — 2026-10-16

Both local x64/x86 build/test/package commands PASS, 106 assertions each, including
native exports and loopback HTTP. Added tests verify new 1/new 2 tracking and immediate
switches, saved-path transition, no background sampling, title markers and privacy
redaction. File retains the host tab label; title adds [unsaved] for non-rooted labels.
Source adds NPPM_GETFILENAME fallback when NPPM_GETFULLCURRENTPATH is empty.
The official Notepad++ message header defines these as RUNCOMMAND_USER+3 and +1:
https://github.com/notepad-plus-plus/notepad-plus-plus/blob/master/PowerEditor/src/MISC/PluginsManager/Notepad_plus_msgs.h
Tab name behavior and content-derived labels are documented in:
https://npp-user-manual.org/docs/user-interface/
Live editor fallback behavior, session restoration and GUI tooltip rendering remain
unverified. No installed DLL was replaced, no new AW history was written, and no
GitHub workflow was added. Updated packages are local under dist/.

## Fresh validation — 2026-10-16

Current source at 3342f25: x64 and x86 build/test/package PASS, 106 assertions
per architecture including native exports and synthetic loopback protocol checks.
The restricted initial test could not connect to loopback; the successful rerun
used approved execution outside the sandbox with checkout-local TEMP/TMP.

Read-only production inspection found Notepad++ running and an installed DLL dated
2026-10-05. Its SHA256 differs from the current x64 build. The latest three AW
events were from October 12, each zero-duration, with file/language/project and no
title. This confirms historical receipt by the installed older build, not current
source acceptance or ongoing capture. No DLL was replaced and no host/server was
restarted. Real-host acceptance of the current build remains outstanding.

## AW Rust 304 timeout fix — 2026-10-16

The installed plugin queued observations while the deployed v0.14.0 (rust) API
remained reachable and the existing bucket type matched app.editor.activity.
The old .NET transport reproduced TaskCanceledException with the real-server test.
Disabling Expect: 100-continue did not fix it and was reverted. The deployed
existing-bucket 304 includes Content-Length: 67 without an event response body.
POST now uses ResponseHeadersRead because initialization/heartbeat handling only
needs response status. Existing metadata verification still reads the GET body.
The fake 304 content regression throws if the caller tries to buffer a body.

Local x64/x86 build/test/package passes: 106 assertions each. The x64 real-server
opt-in passes 111 assertions against AW v0.14.0 Rust, including producer restart,
bucket type check, resumed delivery and preserved gap. API reordered strict data
keys to file,language,project,title; serializer order is not a GUI order guarantee.
Synthetic validation buckets were cleaned up. Installed-host queue recovery still
requires loading the updated DLL; replacing a live process discards its memory queue.
