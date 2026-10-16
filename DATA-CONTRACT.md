# Event data contract

The official [app.editor.activity contract](https://docs.activitywatch.net/en/latest/buckets-and-events.html#app-editor-activity)
is `file` (full path), `project` (full path of cwd) and `language`, all strings.
This plugin uses the nearest VCS root, falling back to the parent folder, as its
project approximation rather than claiming an editor-provided cwd. Saved paths use
forward slashes. Unsaved buffers use their tab label and an empty project.

`PayloadMode` defaults to `strict`: official fields first, then the required
workspace addition `title` (not an official editor field). `extended` adds individually
selected extensions. All choices appear in the plugin Settings dialog. Standard
fields remain present with empty strings when their context is disabled/unavailable;
disabled extensions are absent. Titles never contain document contents.

| Field | Type / meaning / example | Applicability and unavailable behavior | Privacy / default |
| --- | --- | --- | --- |
| file | string; active saved path `C:/Work/demo.cs`, or unsaved tab label `new 1` | Every observation; empty when IncludeFile=false; missing host context produces no observation | Path/tab metadata; IncludeFile=true preserves existing collection |
| project | string; inferred root/parent path `C:/Work` | Saved files only; empty when disabled or resolution fails | Additional full directory metadata; IncludeProjectPath=false |
| language | string; editor language/extension override `C#` | Every observation; Text fallback from host, empty when disabled | File type or user-defined label; IncludeLanguage=true |
| title | string; concise filename `demo.cs` or `new 1 [unsaved]` | Every observation; Activity (context unavailable) when file disabled | Basename only; mandatory; respects IncludeFile |
| filename | string; basename `demo.cs` | Extended, IncludeFilename and IncludeFile; absent otherwise | Filename; IncludeFilename=false |
| project_name | string; root/parent basename `Work` | Extended and IncludeProjectName; empty if unsaved/unavailable, absent if disabled | Directory name; IncludeProjectName=false |

Missing new settings in older JSON receive the defaults above. Migration does not
enable full project paths or extensions: old project basenames cease being sent
until explicitly selected as project_name in extended mode. Full file paths and
language retain previous defaults. Required fields with empty values represent
unknown/redacted context, not real paths. Existing AW history is not erased by a
settings change. Queued observations are serialized under current settings after
Save, so newly disabled fields do not leak through backlog titles or payloads.
No event contents or paths appear in diagnostics; there is no local history export.

Serialization order is tested, but JSON key order has no semantic guarantee.
AW v0.13.2 storage/API returned file,project,language,title for the tested strict
payload. GUI tooltips and extended-key presentation remain unverified; no
display-order promise is made. Presentation ordering belongs in AW UI if needed.
The user's earlier screenshot demonstrates event receipt for an earlier build,
not this revised contract or installed-version compatibility.

`Enabled` defaults true. Turning off Enable tracking stops sampling and cancels
delivery, discards the in-memory backlog, and remains stopped after host restart.
Turning it on constructs one sender and one timer; no standalone relauncher exists.

Unsaved buffers with a host-provided non-rooted label remain trackable. The title
adds `[unsaved]`; file and optional filename retain the label. If the full-path
query is empty, the plugin requests the current filename/tab label. If both are
unavailable, sampling skips unknown context rather than inventing an identity.
The marker means no saved path, not pending edits in an already named file.
Tab names can be reused and are not durable IDs. No buffer contents are read.
Notepad++ can itself derive tab labels from content; disable File / title to avoid
sending such labels if that host preference is enabled.
