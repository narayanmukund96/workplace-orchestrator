# V1 Release Checklist

## Product
- [ ] Multiple workspaces supported
- [ ] Workspace create/edit/duplicate/delete works
- [ ] Installed apps can be added
- [ ] App order can be changed
- [ ] Smart mode works
- [ ] Timed mode works
- [ ] Hybrid mode works
- [ ] Per-app overrides work
- [ ] Individual start/stop works
- [ ] Workspace launch/stop works safely

## State Accuracy
- [ ] Pre-existing running apps detected
- [ ] External app closure detected
- [ ] External app launch detected
- [ ] Failed launch surfaced clearly
- [ ] Crash/unexpected stop surfaced

## Performance
- [ ] Idle CPU within agreed budget
- [ ] Idle memory within agreed budget
- [ ] Monitoring uses backoff/event-driven behavior
- [ ] Sequencing does not materially freeze UI
- [ ] Stress test with multiple heavy apps completed

## Security
- [ ] No permanent admin requirement
- [ ] UAC flow validated
- [ ] Executable paths validated
- [ ] Workspace stop ownership safeguards validated
- [ ] No credentials stored

## Privacy
- [ ] No telemetry
- [ ] No unnecessary network dependency
- [ ] Local data documented

## Quality
- [ ] Core test plan passed
- [ ] Known issues reviewed
- [ ] Decision log updated
- [ ] Version number set
- [ ] Installer/uninstaller tested
- [ ] Local configuration cleanup behavior tested
