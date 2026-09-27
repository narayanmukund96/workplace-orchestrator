# Known limitations and release follow-ups

| ID | Area | Status | Details / mitigation |
|---|---|---|---|
| ISS-001 | Process start events | Environment limitation | WMI start tracing was unavailable as standard user on the test machine. Five-second reconciliation detects external launches; matched process exits use events. This is visible in Diagnostics. |
| ISS-002 | Bootstrap and multi-process applications | Conservative support | Only the exact returned, newly created process is owned. Handoff-only processes are detected when their executable matches but are not adopted. Workspace stop may leave a bootstrapped application open; individual Stop lets the user choose a process. |
| ISS-003 | Application discovery | Compatibility boundary | Apps whose only launch mechanism requires command-line arguments, inaccessible/virtual executable identities, network locations, or non-EXE targets are not covered. These entries are omitted; arguments/scripts/URLs are explicitly outside V1. Direct executable and manifest-backed Store apps are supported. The broad “any installed app” objective still requires a larger compatibility matrix. |
| ISS-004 | Elevated/protected apps | Validation pending | Windows UAC is delegated to shell activation; no permanent elevation. Higher-privilege processes may not expose identities or accept close requests. Do not infer ownership when identity cannot be read. UAC acceptance/denial and protected-app behavior need interactive verification. |
| ISS-005 | Heavy workload pressure tuning | Validation pending | Deterministic tests cover sustained CPU/disk/memory pressure and timeouts. A real multi-heavy-app stress comparison is not yet performed. Thresholds are provisional. |
| ISS-006 | Store activation compatibility | Validation pending | Store discovery and COM activation code implemented. Discovery found 57 Store apps in the first native run. Activation/stop across different Store application models has not been exhaustively tested. |
| ISS-007 | Installer shell integration | Partial validation | File installation/removal passed in an isolated folder. Start Menu, Installed Apps registration, and full interactive removal remain to be checked in a disposable Windows profile. |
| ISS-008 | Resource budget | Provisional | UI harness loaded in 852 ms; after exercising several dialogs, measured 131.0 MiB working set and 0.78% of one logical core over 10 seconds. This is one warm-machine measurement, not a cold-start or heavy-workload benchmark. Product documents provide no numerical acceptance budgets. |
| ISS-009 | Signature | Deferred by source backlog | Executable is unsigned. Code signing is not included in the supplied V1 scope. |
| ISS-010 | Crash classification | OS-observable limit | Nonzero exit status is shown as an unexpected stop; normal exit shows Closed. Some applications use unusual exit codes, and very short-lived processes can exit before an event handler attaches. No intrusive crash collector is added. |

These are explicit boundaries, not claims that all release-checklist gates have passed.
