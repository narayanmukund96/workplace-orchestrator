# Product Requirements Document (PRD)

## Product
Workspace Orchestrator

## Problem
Users with multiple desktop applications, especially resource-heavy AI and productivity applications, often manually stagger application startup after Windows boots to avoid temporary CPU, RAM, and disk contention.

Existing launchers can open groups of applications, but the target problem is broader: prepare a Windows machine for a workload as quickly as possible without materially degrading machine responsiveness.

## Product Objective
Provide a lightweight Windows application that lets users create modular workspaces and launch selected applications in an intelligent sequence based on system readiness, with optional user-defined timing overrides.

## Core Product Principle
The orchestrator must reduce startup contention rather than create additional contention itself.

## V1 Users
Primary user:
- Windows user running several desktop applications in one work session
- May use multiple AI applications, browsers, coding tools, collaboration tools, or productivity applications
- Values speed, simplicity, and machine responsiveness

## V1 Scope
### Included
- Windows desktop application
- Local-only operation
- Create multiple workspaces
- Rename, edit, duplicate, and delete workspaces
- Add installed applications to a workspace
- Reorder applications within a workspace
- Start one application from the workspace
- Stop one application from the workspace
- Launch an entire workspace
- Stop a workspace safely
- Detect if a configured application is already running
- Detect when an application is manually closed outside the orchestrator
- Live running/closed/launching/error state
- Smart launch mode
- Timed launch mode
- Hybrid launch mode
- Per-application launch rules
- Failure handling and retry
- Local persistence of configuration

### Explicitly Excluded from V1
- macOS/Linux
- URLs, folders, files, scripts, browser tabs
- Cloud sync
- User accounts
- Telemetry or analytics
- Import/export
- Automatic updates
- Remote control
- Team/shared workspaces
- AI recommendation engine
- Window layout restoration

## Workspace Model
A workspace is an editable collection of applications and launch rules.

Examples:
- AI Workspace
- Job Search
- Coding
- Research

Each workspace contains:
- Name
- Ordered application list
- Per-application launch mode
- Optional minimum delay
- Optional retry behavior

## Launch Modes
### Smart
The orchestrator automatically determines when to launch the next application based on system pressure and previous application status.

### Timed
The user specifies fixed delay intervals between applications.

### Hybrid
A minimum user-defined delay is respected, but the orchestrator may wait longer if system pressure remains high.

Hybrid is the default V1 behavior.

## Application States
At minimum:
- Closed
- Queued
- Launching
- Running
- Waiting
- Failed
- Stopped unexpectedly

## Stop Behavior
- User may stop one application from the workspace UI.
- User may close an application normally through Windows; the workspace must update automatically.
- Workspace-level stop must avoid terminating applications that were already running before the workspace session started.
- Only applications launched by the current workspace session should be automatically eligible for workspace-level stop by default.

## UX Principles
- Modern Windows design
- Minimal cognitive load
- Fast startup
- Primary actions visible without technical jargon
- Advanced controls hidden until needed
- Real-time status without intrusive notifications

## Performance Requirements
The orchestrator should:
- Start quickly
- Use minimal CPU when idle
- Use minimal memory relative to managed applications
- Avoid aggressive polling
- Prefer event-driven process state monitoring
- Return close to idle resource consumption once launch sequence completes

## Success Criteria
V1 succeeds if a user can:
1. Create a workspace.
2. Add installed applications.
3. Configure order and launch behavior.
4. Launch the workspace.
5. Have applications start progressively without noticeable machine lock-up caused by simultaneous startup.
6. Observe application state accurately.
7. Stop one app or safely stop a workspace.

## Non-Goals
V1 is not intended to be:
- A replacement for Windows Task Manager
- A full automation platform
- A shell replacement
- A remote device manager
- A general scripting environment
