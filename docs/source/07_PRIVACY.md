# Privacy Specification

## Principle
V1 is local-first and should collect only what is required to operate the application.

## Data Stored Locally
- Workspace names
- Selected application names
- Executable paths
- Launch order
- Launch mode and timing configuration
- Optional local runtime/session state

## Data Not Collected
- Credentials
- Passwords
- API keys
- File contents
- Browser history
- Screen contents
- Clipboard contents
- Keystrokes
- User documents
- Cloud account data

## Telemetry
No telemetry in V1.

## Network Access
The core application should not require network access for V1 operation.

## Local Process Monitoring
Process monitoring must be limited to what is necessary to determine whether configured workspace applications are running.

Avoid maintaining an unnecessary historical log of unrelated processes.

## User Control
Users can:
- delete workspaces
- remove applications
- delete local configuration by uninstalling/resetting the application

## Future Changes
Any future cloud sync, telemetry, or automatic update capability requires explicit revision of this document and the threat model.
