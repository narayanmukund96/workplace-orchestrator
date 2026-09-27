# Workplace Orchestrator

A local Windows application that opens your working applications in sequence, watches their live state, and safely closes only the processes owned by the current workspace session.

## Run

Open **`dist\WorkplaceOrchestrator.exe`** on 64-bit Windows 10 or 11 with .NET Framework 4.8 or newer. No SDK, package download, administrator account, or network connection is needed. Keep `WorkplaceOrchestrator.exe.config` beside the executable.

The distributable `release\WorkplaceOrchestrator-1.0.0.zip` includes the executable, source, setup scripts, documentation and test sources. Extract it before running. Rebuild the archive with `.\package.ps1`.

1. Choose **New workspace** and give it a name.
2. Choose **Add application**. Search discovered desktop/Store applications, or browse to an `.exe`.
3. Use each row's **•••** menu to adjust timing, enable/disable, or move the application up/down.
4. Choose **Launch workspace**, or start an individual application.
5. **Stop workspace** requests a normal close only for processes verified as opened by this session. Applications that were already running remain open.

The first run starts empty. Preview screenshots and UI tests use isolated example data; they do not create your workspaces.

## Included

- Multiple workspaces; create, rename, duplicate, delete, and persist locally.
- Desktop app discovery through App Paths and Start Menu shortcuts, Store app discovery through package manifests, and manual `.exe` selection.
- Smart, timed, hybrid (default), and immediate launch modes; per-application delay, maximum wait, retries, and enable/disable.
- CPU, available-memory and disk pressure sampling during launches, with bounded waits and critical-memory protection.
- Already-running detection, external launch/closure detection, abnormal exit reporting, retry/skip, and pause/continue failure policies.
- Graceful workspace stop; individual process selection with a separately confirmed force-end option.
- SQLite persistence with schema versioning, parameter binding, transactions, and validation.
- No accounts, cloud features, telemetry, automatic updates, or app launch arguments.

## Timing and ownership

The rule on an application controls the wait **before that application**, measured from the preceding successful launch. The first application has no predecessor delay, but smart/hybrid still respect pressure. Smart has a 750 ms settling floor after a previous launch. Hybrid defaults to 2 seconds; maximum wait defaults to 30 seconds. At the maximum wait the sequence continues with a visible warning, unless available memory is critical; critical memory causes a failure and follows the workspace failure policy.

Ownership requires the exact executable path, PID, and process start time returned from the launch. The app never adopts arbitrary descendants or matching processes that merely appear later. If a launcher hands off to another process, running detection may succeed while ownership remains uncertain: workspace stop leaves that application open. Use its individual Stop control when appropriate. Save prompts and background applications can keep a process running after a graceful close request.

Ownership is in memory. Closing Orchestrator leaves applications open. On reopening, those processes count as pre-existing and are protected. Repeated launches during the same Orchestrator run preserve verified ownership. Only one launch sequence runs at a time across all workspaces.

## Local data

Configuration: `%LOCALAPPDATA%\WorkplaceOrchestrator\workspaces.db` (SQLite WAL/SHM files may also be present). Contains only workspace names, selected application identities and rules. No unrelated process history is retained. The application refuses unsupported newer schemas or invalid configuration rather than silently resetting it.

To reset, close Orchestrator and remove this configuration folder. Backups are the user's responsibility; import/export is outside V1.

## Optional installation

From this directory in PowerShell:

```powershell
.\install.ps1
```

This installs for the current user under `%LOCALAPPDATA%\Programs\WorkplaceOrchestrator`, adds a Start Menu shortcut, and registers an uninstall entry. No elevation is required. Run the installed `uninstall.ps1` or use Windows Installed Apps to remove the app; configuration is preserved by default. Running the installed script with `-RemoveData` also deletes the local configuration. There is no updater or background service.

The binary is unsigned. Code signing is deferred by the supplied product backlog.

## Build and verify

The build uses the C# compiler included with Windows .NET Framework, native WPF and Windows SQLite. There are no third-party runtime or NuGet dependencies.

```powershell
.\build.ps1 -Test       # Compile and run deterministic core tests
.\tests\run.ps1 -Native # Real process/resource/discovery integration checks
.\tests\ui.ps1         # UI workflow and idle measurement, isolated data folder
```

The native test opens/closes a purpose-built test application. The UI test opens an isolated Orchestrator window and exercises dialogs without launching your applications. Results are saved under `tests\output`. Build output is in `dist`.

## Source map

| File | Responsibility |
|---|---|
| `src/Domain.cs` | Workspace/app models, executable validation, pressure classification |
| `src/Store.cs` | Windows SQLite persistence and schema |
| `src/WindowsServices.cs` | Discovery, process identities, activation, monitoring, resource sampling |
| `src/Engine.cs` | Serialized sequencing, sessions, cancellation, retry, safe stop |
| `src/App.cs` | WPF UI controller and dialogs |
| `src/MainWindow.xaml` | Main window and control styles |

The original 13 supplied documents are preserved in `docs/source`. See [implementation decisions](docs/IMPLEMENTATION_DECISIONS.md), [verification and release status](docs/RELEASE_STATUS.md), and [known limitations](docs/KNOWN_ISSUES.md).
