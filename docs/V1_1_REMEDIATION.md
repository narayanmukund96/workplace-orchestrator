# V1.1 remediation — implementation record

Date: 2026-09-26. This is an execution/evidence log; `source/01_PRD.md` through `source/12_FUTURE_BACKLOG.md` remain the canonical product specifications and are updated in place. The original Downloads pack is untouched. `artifacts/v1-before-remediation.zip` captures the pre-change source and documentation.

## Audit and plan

Existing stack: C# 5 / .NET Framework 4.8 WPF, embedded runtime XAML, SQLite through Windows `winsqlite3`, PowerShell per-user install/uninstall. Discovery reads App Paths, argument-free Start Menu links and Store manifests. Monitoring combines exact-path scans, filtered WMI start events (unavailable on this machine), and `Process.Exited`. CPU/RAM/disk samples activate during sequencing. These components remain.

| Priority / issue | Actual root cause | Incremental approach / affected files | Acceptance and coverage | Status |
|---|---|---|---|---|
| P0 installation | Deliverable includes developer/test sources, no easy setup entry, no embedded product icon; no installed registration exists on this machine | Product-only ZIP, explicit setup, metadata/icon, Start Menu/App Paths/uninstall registration; build/install/package scripts | Real per-user install, shortcut target/name/icon, Get-StartApps and Search, uninstall, package deny-list | In progress |
| P0 window | Source relies on WPF native defaults; old preview only rendered content and excluded chrome | Make native chrome, resize/taskbar, AppUserModelID explicit; verify real window | Minimise/maximise/restore/resize/drag/Alt+F4; native style assertions and desktop checks | In progress |
| P0 identity | Picker template lacks icons; manual browse uses filename only; shortcut metadata is discarded | Source and icon-reference fields, atomic schema migration, bounded icon cache, metadata-first discovery | v1 migration preserves rows, names and icons for desktop/Store/manual entries | In progress |
| P0 individual Stop | Only one selected PID receives CloseMainWindow; no wait/Stopping state; no descendant tracking | Retain verified roots plus descendants with creation-time ancestry; bounded graceful close then safe owned-process termination | Controlled multiprocess app, stubborn child, pre-existing app, reused PID, external closure | In progress |
| P0 workspace Stop | Close requests never checked for completion; ownership only root PID | Reuse the owned app-group stop pipeline; reconcile immediately | Multiple apps, shared app, pre-existing protection, cancellation during launch | In progress |
| P0 live state | Running scan only considers same executable; removed executable checked only on selected workspace display | Include verified descendants; continuous missing-path state; guard in-flight refresh during stop | Crash/normal exit/external launch/missing executable; bounded idle polling | In progress |
| P1 startup/tray | No settings table, startup registration, tray icon, or activation handoff | Local preferences, opt-in HKCU Run, NotifyIcon, single-instance activation; no workspace autolaunch | Startup off by default; tray open/exit; close preference; duplicate invocation | Pending P0 |
| P1 appearance/UI | Hard-coded brushes, large dashboard spacing, technical paths dominate secondary areas | Runtime theme dictionary, System/Light/Dark, compact application-first layout | Theme persistence/system changes, light/dark renders and interactive workflows | Pending P0 |
| Regression/performance | Previous measurements are short warm-machine samples, arbitrary pass threshold | Retain functional tests, expand native/UI/release tests, measure idle/tray/active without invented numerical gate | Logs and observed limits recorded in canonical checklist | Pending |

Work order: identity/migration → installation/native window → discovery → process tracking/stops and P0 verification → startup/tray → appearance/UI → full regression and measured release candidate. Dependencies may be implemented together, but visual polish will follow P0 functional acceptance.

No framework replacement, cloud feature, updater, content-type expansion or destructive data migration is planned. Existing workspaces must survive. Stop will no longer offer arbitrary termination of a pre-existing app instance; this is the explicit V1.1 safety requirement. Close-to-tray will default on with a visible explanatory preference; Windows startup defaults off.

## Evidence

To be filled with actual test outputs and remaining gates before release. No P0 item is marked complete without evidence.
