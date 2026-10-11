# AwWatcherNPP guidance

Follow root AGENTS.md. This is a Notepad++ DLL, not a separately deployed watcher.
Run scripts/build.ps1 -Platform x64 -Test -Package and repeat for x86.
Tests instantiate settings controls without interactive UI; --preview is optional
and must not be used to open interactive UI without authorization. In restricted
sessions set TEMP and TMP to an ignored .tools/test-temp directory in this checkout.
Keep DATA-CONTRACT.md and VALIDATION.md current. Do not infer live editor/reboot
recovery from DLL export or synthetic HTTP evidence. Preserve 304 as successful
bucket creation only; test resumed delivery, privacy and interval boundaries.
Do not edit root documentation as part of concurrent component validation.
