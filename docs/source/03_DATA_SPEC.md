# Data Specification

## Storage Principle
All V1 configuration is stored locally on the user's machine.

No cloud sync or telemetry is required.

## Core Entities

### Workspace
- `workspace_id` - UUID
- `name` - string
- `created_at` - timestamp
- `updated_at` - timestamp
- `default_launch_mode` - enum: smart | timed | hybrid | immediate
- `default_min_delay_ms` - integer nullable
- `failure_policy` - enum: continue | pause

### WorkspaceApplication
- `workspace_app_id` - UUID
- `workspace_id` - UUID
- `display_name` - string
- `executable_path` - string
- `process_match_rule` - string/object
- `icon_reference` - local reference nullable
- `sort_order` - integer
- `launch_mode` - enum
- `min_delay_ms` - integer nullable
- `max_wait_ms` - integer nullable
- `retry_count` - integer
- `enabled` - boolean

### WorkspaceSession
Runtime-only or optionally persisted for crash recovery.

- `session_id` - UUID
- `workspace_id` - UUID
- `started_at` - timestamp
- `ended_at` - timestamp nullable
- `status` - launching | running | stopping | completed | failed

### SessionApplicationState
- `session_id`
- `workspace_app_id`
- `was_running_before_session` - boolean
- `launched_by_session` - boolean
- `process_id` - integer nullable
- `state` - closed | queued | waiting | launching | running | failed | stopped_unexpectedly
- `launch_started_at` - timestamp nullable
- `running_at` - timestamp nullable
- `stopped_at` - timestamp nullable
- `error_code` - string nullable

## Local Persistence
Recommended V1 options:
- SQLite for structured, durable storage; or
- local JSON for simplest prototype

Production preference: SQLite because workspace, app, and session relationships are naturally structured and future migrations will be easier.

## Sensitive Data
V1 should avoid storing:
- user credentials
- tokens
- passwords
- browser session data
- document contents
- unrelated process history

## Data Integrity
- Validate executable path before saving.
- Handle missing/uninstalled executables gracefully.
- Preserve workspace order.
- Use schema versioning from V1.
