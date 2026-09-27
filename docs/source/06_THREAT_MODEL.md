# Threat Model

## Security Objective
Allow launching and monitoring local applications without introducing unnecessary privilege, execution, persistence, or data exposure risks.

## Assets
- Workspace configuration
- Executable paths
- Local application state
- Session process ownership state
- User's machine resources

## Trust Boundaries
- Orchestrator process
- Windows operating system
- External applications launched by the orchestrator
- Local configuration store

## Key Threats

### 1. Arbitrary Executable Abuse
Risk:
A malicious or unintended executable path could be added and launched.

Mitigation:
- explicit user selection
- path validation
- visible executable identity
- no silent remote execution

### 2. Privilege Escalation
Risk:
Running the orchestrator elevated could widen attack surface.

Mitigation:
- standard-user operation by default
- rely on Windows UAC per application
- no stored elevation credentials

### 3. Unsafe Process Termination
Risk:
Workspace stop could terminate apps the user was already using.

Mitigation:
- record pre-existing process state
- only auto-stop processes launched by the current workspace session
- require explicit action for ambiguous cases

### 4. Configuration Tampering
Risk:
Local config modified to reference unexpected executables.

Mitigation:
- schema validation
- executable existence checks
- surface changed/missing app paths clearly

### 5. Resource Exhaustion
Risk:
Poor sequencing logic launches too many apps and degrades the machine.

Mitigation:
- smart/hybrid launch control
- bounded concurrency
- maximum waits
- resource-pressure classification

### 6. Denial of Service via Monitoring
Risk:
The orchestrator itself consumes material CPU/RAM through aggressive polling.

Mitigation:
- event-driven process tracking
- adaptive sampling
- idle backoff

### 7. Update Supply Chain
Not in V1 because automatic updates are excluded.

Future mitigation if added:
- code signing
- signed update manifests
- TLS
- rollback protection
