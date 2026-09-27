# Release status — V1.1

Status: **functional local Windows build; suitable for continued personal use and portfolio demonstration. Broader public-distribution validation remains open.**

## Verified

| Area | Current evidence | Result |
|---|---|---|
| Build | Current source builds successfully | Passed |
| Core automated suite | 36 tests after the V1.1 source changes | 36 passed / 0 failed |
| Native integration suite | Resource sampling, process identity, graceful closure, application discovery and multiprocess termination | 4 passed / 0 failed |
| Application discovery | 139 applications discovered in the recorded native test, including 57 Store applications | Passed |
| Individual Stop | Ownership-aware graceful close with bounded fallback and multiprocess coverage | Passed |
| Stop Workspace | Session ownership protection and safe process handling | Passed |
| Windows Start/Search | Start Menu shortcut, AppUserModelID, shell registration and Get-StartApps/Search catalogue validation | Passed after V1.1 fix |
| Window behaviour | Native minimise/maximise/restore/resize/taskbar/icon checks | Passed; normal interactive use also confirmed |
| Windows startup | Default-off preference; enable/disable registration | Passed |
| System tray | Tray icon, Open, Exit, close-to-tray and duplicate-icon checks | Passed |
| Single-instance activation | Second launch activates existing process/window | Passed |
| Preference persistence | Close behaviour persisted across restart | Passed |
| Idle CPU | Settled installed production process measured for ~60 seconds | ~0.258% of one CPU core |
| Idle memory | Same settled production sample | ~126.9 MB average working set |
| Local data preservation | Existing workspace database preserved through remediation | Passed |

The earlier UI-smoke CPU result of 18.72% of one core was not reproducible in the installed production process and is treated as a test-harness artefact rather than an application idle-performance result.

## Remaining release boundaries

The following are not blockers for current personal use, but should be considered before treating the application as broadly production-distributable:

- executable code signing,
- wider compatibility testing across more Store/bootstrap/single-instance application models,
- interactive elevated/protected-application scenarios,
- broader high-DPI/accessibility testing,
- heavier real-world stress testing across different hardware,
- a polished installer/updater strategy if the product is ever distributed beyond manual installation.

## Reproduce core validation

```powershell
.\build.ps1 -Test
.\tests\run.ps1 -Native
.\tests\ui.ps1
```

Generated test output remains local and is intentionally excluded from the public repository.
