# Smart Launch Engine Specification

## Objective
Launch applications as quickly as reasonably possible without creating avoidable startup contention.

## Default Mode
Hybrid.

A minimum delay may be configured, but the engine may wait longer if the machine remains under pressure.

## Inputs
- CPU utilization over rolling window
- Available RAM / memory pressure
- Disk activity or disk pressure indicator
- Previous app process-start confirmation
- Time since previous launch
- Minimum configured delay
- Maximum wait
- Retry count

## High-Level Algorithm
1. Capture apps already running.
2. Queue remaining enabled apps in order.
3. Launch first eligible application.
4. Confirm process creation.
5. Start minimum settling period.
6. Sample resource pressure over a short rolling window.
7. If pressure is acceptable and minimum delay elapsed, launch next app.
8. If pressure is high, wait with bounded backoff and recheck.
9. If maximum wait expires, follow configured fallback policy.
10. Continue until queue completes.

## Pressure Model
V1 should avoid relying on one hard threshold such as `CPU < 50%`.

Instead classify the machine into a small number of states:
- Healthy
- Moderate pressure
- High pressure

The classifier can use weighted signals from CPU, available memory, and disk activity.

Exact thresholds should remain configurable internally during testing and be tuned empirically.

## Timed Mode
Launch next application after configured interval regardless of machine pressure, except for critical safety conditions.

## Smart Mode
No user delay required. Engine uses system state and launch confirmation.

## Hybrid Mode
Launch only when both are true:
- minimum delay has elapsed
- machine pressure is acceptable

If machine remains under pressure, the engine waits until either pressure clears or maximum wait is reached.

## Already Running Application
If application is already running:
- mark Running
- do not launch duplicate by default
- preserve `was_running_before_session = true`

## Failure Handling
On launch failure:
- mark Failed
- retry according to configured retry count
- if retries exhausted, continue or pause based on workspace failure policy

## Maximum Wait
Required to prevent one application or background event from blocking the queue indefinitely.

On maximum wait expiry, options include:
- launch next app anyway
- skip current pending action
- pause sequence

V1 default should favor continuity while surfacing the warning.

## Efficiency Guardrail
Resource monitoring should become much less frequent once launch sequencing completes.
