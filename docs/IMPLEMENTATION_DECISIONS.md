# Implementation decisions

The original product specification set is retained in `docs/source/`. This file records implementation choices made while turning the specification into the Windows application.

| ID | Decision | Reason |
|---|---|---|
| DEC-017 | User-facing name is Workplace Orchestrator. | Matches the final product naming used during development. |
| DEC-018 | Native WPF, C# / .NET Framework 4.8, x64 Windows. | Keeps the application Windows-native and avoids a heavyweight browser runtime. |
| DEC-019 | Use Windows SQLite with parameter-bound SQL, schema versioning and atomic transactions. | Reliable local persistence without cloud infrastructure. |
| DEC-020 | Use executable identity plus PID/start-time/session ownership for stop operations. | Reduces risk of terminating unrelated or pre-existing processes. |
| DEC-021 | Keep launch-session ownership in memory. After restart, running applications are protected as pre-existing. | Conservative recovery behaviour. |
| DEC-022 | Combine process-event handling with bounded reconciliation where Windows event access is unavailable. | Maintains live state without aggressive continuous polling. |
| DEC-023 | Sample CPU, available memory and disk primarily during sequencing/diagnostics. | The orchestrator should return to near-idle behaviour after work completes. |
| DEC-024 | Smart launch uses multiple system-pressure signals rather than a single CPU threshold. | Avoids simplistic launch decisions. |
| DEC-025 | Hybrid launch combines a minimum delay with machine-readiness checks. | Provides predictability while still protecting responsiveness. |
| DEC-026 | A maximum wait prevents a single application from permanently blocking the workspace sequence. | Maintains continuity. |
| DEC-027 | Stop behaviour is ownership-aware and attempts graceful shutdown before controlled fallback. | Balances safety with practical ability to stop modern multiprocess applications. |
| DEC-028 | Prefer installed-app/Start Menu/package metadata for application identity; manual executable selection remains a fallback. | Presents applications as applications rather than raw executable paths. |
| DEC-029 | V1 remains focused on local applications; scripts, URLs and arbitrary automation are outside scope. | Prevents scope creep. |
| DEC-030 | Only one workspace launch sequence runs at a time. | Avoids recreating the startup-contention problem the product is designed to solve. |
| DEC-031 | Per-user installation; no permanent elevation, background service or automatic updater. | Keeps deployment and runtime footprint small. |
| DEC-032 | Windows startup is optional and **off by default**. Enabling it starts Workplace in background mode but does not auto-launch a workspace. | Gives fast access without increasing boot workload unnecessarily. |
| DEC-033 | Workplace supports a lightweight system-tray lifecycle with Open and Exit actions. | Allows the utility to remain available without occupying the main taskbar continuously. |
| DEC-034 | Close behaviour is user-configurable between close-to-tray and full exit. | Makes background behaviour explicit rather than surprising. |
| DEC-035 | Appearance supports System, Light and Dark modes, with System as the default. | Fits normal Windows expectations without maintaining separate products. |
| DEC-036 | Generated builds, candidate packages, backups, local databases, logs and verification output are excluded from Git. | Keeps the repository focused on source, tests and product documentation. |

Future changes should extend this list only when a real product or engineering decision needs to be preserved.
