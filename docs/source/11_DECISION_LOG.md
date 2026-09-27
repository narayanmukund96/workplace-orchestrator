# Decision Log

This file records product and architecture decisions that should be treated as locked unless explicitly reopened.

| ID | Decision | Status | Rationale |
|---|---|---|---|
| DEC-001 | V1 is Windows-only | Locked | Keeps platform scope focused and enables Windows-specific process/resource integration |
| DEC-002 | V1 supports applications only | Locked | Avoids premature expansion into URLs/files/scripts |
| DEC-003 | Multiple editable workspaces are core | Locked | Modularity is central to product value |
| DEC-004 | Any installed application can be added | Locked | Product should not be AI-app-specific at technical level |
| DEC-005 | Local-only persistence | Locked | Simpler architecture and privacy model |
| DEC-006 | No import/export in V1 | Locked | Not required for initial utility |
| DEC-007 | No telemetry in V1 | Locked | Preserve lightweight, local-first behavior |
| DEC-008 | Hybrid launch is default | Locked | Balances predictability and system responsiveness |
| DEC-009 | Smart engine considers CPU, memory, disk, process state, and timing | Locked | Avoids simplistic single-threshold logic |
| DEC-010 | Timed mode remains available | Locked | Gives users explicit sequencing control |
| DEC-011 | User can start/stop individual apps from workspace | Locked | Supports modular control |
| DEC-012 | Workspace stop should not close apps that pre-existed the session | Locked | Prevents destructive/unexpected termination |
| DEC-013 | Manual Windows close must update workspace state | Locked | Workspace should reflect live state rather than fire-and-forget launch |
| DEC-014 | Orchestrator runs as standard user by default | Locked | Reduces privilege and attack surface |
| DEC-015 | Automatic updates excluded from V1 | Locked | Avoids unnecessary network, signing, rollback, and updater complexity |
| DEC-016 | Lightweight resource consumption is a product requirement | Locked | Product exists specifically to improve startup efficiency |
