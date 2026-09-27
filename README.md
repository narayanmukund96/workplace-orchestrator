# Workplace Orchestrator

A lightweight Windows desktop utility for launching groups of applications in a controlled sequence instead of opening everything at once.

The product started from a simple problem: when several heavy desktop applications launch together, they compete for CPU, memory and disk resources. Workplace Orchestrator lets you create reusable workspaces and launch those applications progressively using smart, timed, hybrid or immediate sequencing.

## What it does

- Create and manage multiple workspaces.
- Add installed desktop and Microsoft Store applications.
- Launch applications in a defined order.
- Use **Smart**, **Timed**, **Hybrid** or **Immediate** launch modes.
- Monitor CPU, available memory and disk pressure during sequencing.
- Detect applications that are already running.
- Track live application state when apps are opened or closed outside Workplace.
- Stop individual applications or a whole workspace with ownership safeguards.
- Run optionally at Windows startup and stay available in the system tray.
- Use **System**, **Light** or **Dark** appearance modes.
- Store configuration locally with no account, cloud dependency or telemetry.

## Why it exists

The aim is not simply to replace a shortcut or batch file. The application is designed to prepare a working environment while reducing startup contention and avoiding unnecessary impact on machine responsiveness.

A typical flow is:

```text
Start workspace
    ↓
Launch first application
    ↓
Wait for minimum delay / machine readiness
    ↓
Check CPU, memory and disk pressure
    ↓
Launch next application
    ↓
Repeat until workspace is ready
```

## Current status

The current Windows build is functional and has been used interactively on the development machine.

Validated areas include:

- workspace persistence and sequencing,
- application discovery and icon resolution,
- Windows Start/Search registration,
- standard desktop window behaviour,
- individual and workspace Stop behaviour,
- process ownership safeguards,
- Windows startup registration,
- system-tray lifecycle,
- close-to-tray / exit-on-close preferences,
- single-instance activation,
- local preference persistence.

Idle resource usage was also rechecked after an earlier test-harness measurement appeared high. A settled production-process sample measured approximately **0.26% of one CPU core** over ~60 seconds, with a working set around **126–127 MB** on the test machine. These figures are observations from one machine, not product-wide performance guarantees.

See:
- [Release status](docs/RELEASE_STATUS.md)
- [Known limitations](docs/KNOWN_ISSUES.md)
- [V1.1 remediation record](docs/V1_1_REMEDIATION.md)
- [Implementation decisions](docs/IMPLEMENTATION_DECISIONS.md)

## Architecture

The application is Windows-first and local-only.

| Area | Implementation |
|---|---|
| UI | WPF |
| Runtime | .NET Framework 4.8 |
| Persistence | SQLite via Windows `winsqlite3` |
| Launch orchestration | C# sequencing engine |
| Windows integration | Start Menu, App Paths, package metadata, tray/startup integration |
| Process safety | executable identity + PID/start-time/session ownership checks |

Primary source files:

| File | Responsibility |
|---|---|
| `src/Domain.cs` | Workspace and application models |
| `src/Store.cs` | Local persistence and schema migration |
| `src/WindowsServices.cs` | Discovery, process monitoring and system resource sampling |
| `src/Engine.cs` | Launch sequencing, retries, cancellation and safe stop |
| `src/App.cs` | Application lifecycle and UI controller |
| `src/MainWindow.xaml` | Main Windows interface |
| `src/WindowsIntegration.cs` | Windows shell/startup integration |
| `src/ProcessGroups.cs` | Process-group and ownership handling |
| `src/ThemeManager.cs` | System/Light/Dark appearance |

## Build and test

The repository contains source and build/test scripts rather than committed generated binaries.

```powershell
.\build.ps1 -Test
.\tests\run.ps1 -Native
.\tests\ui.ps1
```

Generated build output, candidate packages, local databases, logs and verification artifacts are intentionally excluded from Git.

## Installation

The project includes per-user installation and uninstall scripts:

```powershell
.\install.ps1
```

The installer registers Workplace Orchestrator with Windows Start/Search and Installed Apps without requiring the application itself to run permanently elevated.

The executable is currently unsigned.

## Local data and privacy

Workspace configuration is stored locally under:

```text
%LOCALAPPDATA%\WorkplaceOrchestrator
```

The application has:

- no user account,
- no cloud storage,
- no telemetry,
- no advertising,
- no automatic updater.

## Product documentation

The repository includes the product and engineering documentation used during development. The original product specification set is retained under `docs/source/`, while the top-level documents in `docs/` capture implementation decisions, release evidence, known limitations and the V1.1 remediation cycle.

## Development approach

This project was built iteratively through AI-assisted product design and software development: defining the product requirements, implementing the application, testing real Windows behaviour, identifying deployment defects, and remediating them through targeted engineering iterations.

The intent of this repository is to show the complete product-building process rather than only the final source code.
