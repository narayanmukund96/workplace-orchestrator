# V1.1 remediation — implementation record

Date: 2026-09-26 to 2026-09-27.

V1.1 was initiated after hands-on use of the first deployed build exposed several gaps between the product specification and the actual Windows experience. The remediation deliberately preserved the existing application and corrected the smallest necessary areas rather than rebuilding the product.

## Issues addressed

| Area | Root problem | V1.1 outcome |
|---|---|---|
| Windows installation / discovery | Initial packaging and shell integration did not produce normal Start/Search discoverability | Production metadata, icon, registration and shortcut handling implemented; Start/Search/Get-StartApps validation passed |
| Native window behaviour | Initial shell did not expose the expected Windows desktop lifecycle clearly enough | Native minimise/maximise/restore/resize/taskbar behaviour implemented and validated |
| Application identity | Discovery was too executable-centric and did not consistently surface normal application identity/icons | Metadata-first discovery, icon extraction/cache and repair workflow implemented |
| Individual Stop | Original process handling was insufficient for modern multiprocess applications | Ownership-aware process groups, graceful shutdown and controlled fallback implemented; automated coverage passed |
| Stop Workspace | Workspace-wide stop needed stronger session ownership protection | Only safely attributable session-owned processes are stopped |
| Startup / tray | Not present in the first deployment | Optional Start with Windows, tray Open/Exit, close-to-tray and single-instance activation implemented and validated |
| Appearance | Initial UI lacked System/Light/Dark support | Local appearance preference and runtime theme manager implemented |
| Performance validation | One UI-harness idle CPU sample appeared abnormally high | Installed production process was remeasured after settling; ~0.258% of one core over ~60 seconds, so no runtime optimisation was required |

## Validation evidence

After the V1.1 changes:

- current source build succeeded,
- core suite: **36 passed / 0 failed**,
- native integration suite: **4 passed / 0 failed**,
- Windows Start/Search catalogue validation passed,
- startup enable/disable passed,
- tray Open/Exit and duplicate-icon checks passed,
- close-to-tray and exit-on-close passed,
- preference persistence passed,
- single-instance activation passed,
- existing workspace database was preserved.

## Product decisions retained

V1.1 did not expand the core product into a cloud service or general automation platform.

Still retained:

- Windows-first,
- applications-only workspace content,
- local persistence,
- no account,
- no telemetry,
- no cloud dependency,
- Smart / Timed / Hybrid / Immediate sequencing,
- Hybrid as the default launch approach,
- conservative process ownership.

## Remaining work

No further broad remediation pass is planned.

Future engineering should be driven by reproduced issues from normal product use or a deliberate decision to prepare the application for wider distribution.
