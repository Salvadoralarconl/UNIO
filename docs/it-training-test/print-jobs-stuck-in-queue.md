# Print jobs stuck in queue

TEST KNOWLEDGE BASE - Example IT procedure, version 1.0, reviewed 2026-10-10. This is training/test documentation, not an adopted UNIO policy. Applies to Windows 11 unless stated otherwise. Product models, approvals, support contacts, access groups, SLAs and configuration values must be confirmed locally. Never invent them.

## Purpose and symptoms
Use for documents stuck in a queue or a printer that stops after one job.

## Before you start
Record the ticket, employee, asset tag, exact symptom/error, when it began, recent changes, and whether other users are affected. Obtain consent before interrupting work. Protect private information; never collect passwords, MFA codes, recovery keys or unrestricted screenshots.

## Procedure
1. Record the affected queue, job owner, time and error. Confirm whether the printer itself reports a jam or supply issue.
2. Ask the employee to cancel their own blocked job and retry a short non-sensitive test document.
3. Test whether the issue follows one file, one application or all printing.
4. If jobs remain stuck, contact the queue administrator. Restarting a shared print service or clearing its queue can interrupt other users and requires approval.
5. Check the approved driver and connection with IT. Do not delete spooler files, alter registry settings or install unrelated drivers as a first step.
6. Warn users before any approved queue restart and record what was removed/retried.

## Verification and ticket closure
Confirm the test job and the employee original job complete without duplication. Record the steps attempted, observed result, technician and time in the ticket. Close only after the employee confirms the original task works.

## Stop and escalate
Escalate a queue that repeatedly hangs, shared service errors or jobs that return after cancellation.

## Technical reference
Microsoft support reference (consult for device-specific details): https://support.microsoft.com/en-us/windows/hardware/printer/fix-printer-connection-and-printing-problems-in-windows
