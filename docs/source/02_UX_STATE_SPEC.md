# UX and State Specification

## Primary Navigation
### Home / Workspaces
Displays all workspaces as cards or rows.

Each workspace shows:
- Workspace name
- Number of configured applications
- Current state if active
- Primary `Launch` action
- Overflow menu for Edit / Duplicate / Delete

Primary action:
- `+ New Workspace`

## Workspace Detail
Displays ordered applications and their live state.

Per application:
- App icon
- App name
- Launch mode
- Optional delay
- Current state
- Start/Stop action

Workspace actions:
- Launch Workspace
- Stop Workspace
- Edit Order
- Add Application
- Workspace Settings

## Add Application Flow
1. User selects `Add Application`.
2. App presents searchable installed-application list.
3. User selects application.
4. Default launch mode is inherited from workspace defaults.
5. User may override mode/timing.
6. Application is added to ordered workspace list.

Manual `.exe` selection may be supported where an installed application is not discovered automatically.

## Launch Flow
1. User selects `Launch Workspace`.
2. Existing process state is captured.
3. Already-running applications are marked Running and not relaunched by default.
4. Remaining applications enter Queued state.
5. Orchestrator starts the sequence.
6. UI updates continuously.
7. On completion, workspace becomes Ready/Running.

## Stop Flow
### Individual
`Stop` terminates only the selected process subject to safety rules.

### Workspace
`Stop Workspace` attempts to close applications launched by the current workspace session.

Applications already running before the session are not closed automatically.

## External State Changes
If the user manually closes an application through Windows:
- Process termination is detected.
- UI changes to Closed.
- Workspace remains active if other applications are running.

If the user manually launches a configured application outside the orchestrator:
- The process is detected.
- UI updates to Running.
- Launch engine should not create an unnecessary duplicate instance unless explicitly configured.

## Failure UX
If launch fails:
- App state becomes Failed.
- User sees concise reason where available.
- User may Retry, Skip, or Open Details.
- Workspace sequence behavior follows failure policy.

## Advanced Settings
Not shown by default.

May include:
- Minimum delay
- Maximum wait
- Retry count
- Resource sensitivity
- Application launch arguments in a future version

## UX Rule
Resource metrics should not dominate the main screen. CPU/RAM/disk information belongs in optional diagnostics, not the primary workflow.
