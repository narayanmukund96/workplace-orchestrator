# Test Plan

## Test Objectives
Validate:
- workspace management
- app discovery
- launch sequencing
- state detection
- stop safety
- resource efficiency
- resilience
- permissions behavior

## Functional Tests
### Workspace CRUD
- create workspace
- rename workspace
- duplicate workspace
- delete workspace
- persist changes across restart

### Application Management
- add installed app
- remove app
- reorder app
- add same app to different workspaces
- handle missing executable

### Launch Modes
- smart
- timed
- hybrid
- immediate if supported

### State Detection
- app already running before launch
- app launched by orchestrator
- app manually launched outside orchestrator
- app manually closed outside orchestrator
- app crashes unexpectedly

### Stop Behavior
- stop one orchestrator-launched app
- stop workspace
- confirm pre-existing app is not terminated
- manual Windows closure updates UI

## Smart Engine Tests
- low system pressure
- high CPU pressure
- low available RAM
- high disk activity
- pressure clears during wait
- maximum wait reached
- launch failure
- retry succeeds
- retry fails

## Performance Tests
Measure:
- cold start time
- idle memory
- idle CPU
- active monitoring CPU
- active sequencing CPU
- response time of UI state changes

Acceptance principle:
The orchestrator must remain materially lighter than the applications it manages and must not create visible machine degradation under normal use.

## Security Tests
- malformed executable path
- deleted executable
- elevated app launch
- config tampering
- stop ownership correctness

## UX Tests
- first workspace creation
- adding first app
- understanding running/waiting/failed states
- editing timing without technical expertise

## Regression Areas
- process matching
- session ownership
- workspace stop safety
- persistence migrations
- resource-threshold changes
