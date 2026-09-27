# Technical Architecture

## Architecture Goal
Build a lightweight Windows-native or near-native application that remains resource-efficient while managing other applications.

## Logical Components

### 1. UI Layer
Responsibilities:
- Workspace list
- Workspace editor
- App state display
- Start/stop controls
- Settings and diagnostics

### 2. Workspace Service
Responsibilities:
- Create/edit/delete workspaces
- Persist configuration
- Resolve workspace application order

### 3. Application Discovery Service
Responsibilities:
- Discover installed applications
- Resolve display names and icons
- Validate executable paths
- Support manual executable selection if needed

### 4. Process Monitor
Responsibilities:
- Detect whether configured applications are running
- Detect process start/stop events
- Avoid aggressive polling where Windows process events can be used
- Track process IDs for session ownership

### 5. Launch Orchestrator
Responsibilities:
- Queue applications
- Execute smart/timed/hybrid launch rules
- Handle timeout/retry/skip
- Maintain session state

### 6. Resource Monitor
Responsibilities:
- Sample CPU load
- Available memory
- Disk activity or pressure signal
- Produce a simplified pressure state for the launch engine

### 7. Local Persistence Layer
Responsibilities:
- Store workspaces and settings
- Optional session/crash-recovery state

## Execution Model
1. App starts.
2. Local config is loaded.
3. Process monitor synchronizes live application states.
4. User launches a workspace.
5. Session snapshot records apps already running.
6. Launch orchestrator sequences remaining applications.
7. Resource monitor informs readiness decisions.
8. Process monitor confirms actual state.
9. Once complete, launch engine becomes mostly idle while process monitoring remains lightweight.

## Privilege Model
- Application should normally run as standard user.
- Applications requiring elevation should trigger standard Windows UAC behavior.
- Orchestrator should not run permanently elevated.

## Performance Principle
Do not create a high-frequency monitoring loop unless required during active launch.

Prefer:
- process events
- bounded sampling windows
- backoff while waiting
- low-frequency checks when idle
