# V1 build verification — 2026-09-25

Version: **1.0.0**. Status: **usable local V1 build; broader release validation remains open**.

## Verified

| Area | Evidence | Result |
|---|---|---|
| Build | Native x64 executable compiled using Windows .NET Framework compiler | Passed |
| Core behavior | 27 deterministic tests in `tests/Tests.cs`: workspace persistence/order/duplicate/delete, transaction rollback, corrupt/newer/tampered database rejection, path validation, all four modes, pressure clearing/maximum wait/critical memory, retry, pause/skip/cancel, external launch/closure, abnormal exit, pre-existing and shared-app ownership, in-flight cancellation | 27 passed |
| Real Windows integration | Real resource readings; launch/match/graceful close of purpose-built test app; process exit event; mismatched creation-time protection; installed-app discovery and Store identity validation | 3 passed |
| UI workflow | Empty-state launch guard, create workspace, add through picker, configure timing, rename, duplicate, reload persisted results | 8 functional assertions passed |
| UI idle check | 10-second idle sample after dialog workflow; provisional threshold below 2% of one core | Passed, 0.78%; 131.0 MiB working set |
| UI startup sample | Time to loaded UI inside test harness | 852 ms; warm-machine sample |
| Visual verification | Rendered actual WPF content to `tests/output/interface.png` and visually inspected | Passed at 1220 × 820 |
| Optional installation | Installed into isolated workspace folder, then removed only known product files | Passed with shell integration disabled |
| Privacy/privilege | Source inspection: no network client or telemetry, no credential storage, `asInvoker` manifest, runtime-only process ownership | Implemented |
| Source preservation | 13 original Markdown files copied to `docs/source`; original Downloads files untouched | Preserved |

Final discovery found **139 applications, including 57 Store applications**. All 57 Store identities were verified against Windows package registration and executable paths, and a deliberately mismatched path was rejected. Shortcuts with launch arguments are omitted, because executing their targets without those arguments could launch the wrong application.

## Remaining release gates

- Interactive UAC accepted/denied flows and higher-privilege application behavior.
- Real multi-heavy-application stress testing and agreed numerical CPU/memory budgets.
- Broader Store activation/bootstrap/single-instance compatibility matrix.
- Start Menu and Installed Apps integration in a disposable Windows profile.
- Config cleanup with real per-user uninstall, small-screen/high-DPI and screen-reader testing.

No full production-readiness claim is made. The original release checklist remains unchanged in `source/09_RELEASE_CHECKLIST.md`; this file records actual evidence instead of checking unverified boxes.

## Reproduce

```powershell
.\build.ps1 -Test
.\tests\run.ps1 -Native
.\tests\ui.ps1
```

Native/UI checks open purpose-built or isolated test windows. They do not stop arbitrary user applications. Logs, temporary test databases and renders remain under `tests/output`.
