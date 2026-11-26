# ADR 0019 — A driver on sick leave or inactive is neither allocated nor forced; a single criterion

- **Status:** accepted (the user, 2026-11-23) · **Origin:** SPEC 0066 · **Completes:** ADR 0001 and 0018 · **Qualifies:** ADR 0015 (what is not forced)

## Context
Allocating did not look at whether the driver was on sick leave or inactive. The candidate searches looked at it and, only for sick leave, the
Control Centre. The rules catalogue already announced it as a rule that blocks. “Apply to the whole booking”, the API and a
sick leave recorded with the screen open could leave a service to someone who cannot work.

## Decision
- **One criterion, `DriverEligibilityCheck.For(driver, schedule)`:** active and without any unavailability, of whatever type,
  that overlaps `[departure, arrival)`. Allocating, the searches and the warnings of the Control Centre use this one, with the same text.
- **First, before CE 561/2006.** It is the only rejection that does not depend on calculating hours. If it came later, whoever also
  infringes would get a rejection that can be forced, would write the reason and would then run into this one.
- **It is never forced**, like the ineligible vehicle and the overlap: 422 with its own reason and without `forcible`.
- **Audited attempt** as `RejectedDriver`, with the rules `driver-active` and `driver-unavailable`. It counts as blocked.
- **Nothing already allocated is removed** when registering a sick leave or deactivating. The Control Centre warns about the day's services not
  yet finished, also if the driver is inactive (until now only for the inactive vehicle). A person decides.
- **Simultaneous:** it is read inside `ExecuteExclusiveAsync`; the sick leave and the deactivation ask for the driver's lock (ADR 0018).

## Discarded
- **A margin before and after the service:** nobody uses it today and there is no approved rule.
- **After CE 561/2006 or at the end:** it offers to force what cannot be forced.
- **A check of its own in allocating:** it would be the third version of the same rule.

## Consequences
- Changing only the vehicle of a service whose driver is on sick leave is rejected: a substitute must be set. It already happened the other way round
  with the vehicle in the workshop.
- An inactive driver only becomes allocatable again if reactivated, and today there is no endpoint to do it.
- External and non-auto-allocatable drivers can still be allocated by hand: only the suggestions exclude them.
