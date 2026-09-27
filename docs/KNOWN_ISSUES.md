# Known limitations

These are current product boundaries rather than open defects in the core personal-use workflow.

| Area | Current boundary / mitigation |
|---|---|
| Application compatibility | The product is designed around locally installed applications. Apps that require unusual launch arguments, inaccessible virtual identities, scripts, URLs or network executables may not be supported. |
| Store / bootstrap behaviour | Discovery and activation are implemented, but not every Store/bootstrap/single-instance model has been exhaustively tested. |
| Elevated / protected apps | Workplace does not run permanently elevated. Windows UAC and OS process-access rules may restrict monitoring or stopping higher-privilege applications. |
| Ownership after restart | Process ownership is intentionally conservative. Applications already running when Workplace starts are treated as pre-existing and protected from workspace-wide termination. |
| Smart-launch tuning | CPU, memory and disk thresholds are practical heuristics. They have automated coverage and local validation but are not benchmarked across a broad hardware matrix. |
| Accessibility / display matrix | Normal desktop use has been validated, but broad screen-reader, DPI, multi-monitor and very small-screen coverage is not yet complete. |
| Distribution | The executable is currently unsigned. There is no automatic updater or public installer distribution channel. |
| Import / export | Workspace portability is outside the current version; configuration remains local to the machine. |

## Closed V1.1 issues

The following deployment issues identified after the first build have since been remediated and validated:

- Windows Start/Search discoverability,
- native minimise/maximise/close behaviour,
- application identity and icon discovery,
- individual Stop behaviour,
- safe Stop Workspace behaviour,
- Windows startup preference,
- system-tray lifecycle,
- close-to-tray / exit-on-close behaviour,
- single-instance activation.

New issues should be recorded only when reproduced in normal product use.
