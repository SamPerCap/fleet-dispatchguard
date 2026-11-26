# ADR 0017 — Week in Madrid time and in UTC, working day between rests and two calendar weeks

- **Status:** accepted (the user, 2026-11-23) · **Origin:** SPEC 0064 · **Corrects:** the previous computation of Art. 6 (day and week in UTC, a rolling window of 14 days)

## Context
The engine counted calendar day and week in UTC. A service that crossed midnight did not count in the next day, and
if it crossed Monday it did not count in the new week. The fortnight was a rolling window of 14 days.

Text read (Regulation 561/2006 consolidated to 31.12.2024, EUR-Lex 02006R0561): Art. 4.i, the week runs from 00.00 on
Monday to 24.00 on Sunday; Art. 4.k, daily driving time is the time between daily rest periods; Art. 4.l, weekly driving time is
the total accumulated during a week; Art. 6.3, 90 h in two consecutive weeks. It does not set a time zone. Regulation 165/2014
(consolidated to 31.12.2024) measures in UTC in the digital tachograph (Art. 2) and in the official time of the country of registration on
the analogue record sheet (Art. 34.5.a).

## Decision
- **Both clocks.** Week, pair of weeks and the allowance of extended days are evaluated in Madrid time and in UTC, and both
  must be complied with (`DrivingWeek.Clocks`). One figure is shown, the strictest; if they differ, it says which clock it comes from.
  Later services (ADR 0016) are also compared clock by clock: if one was already illegal in one, that does not hide that it stops
  complying in the other.
- **Split.** A service that crosses Monday counts in each week the part that falls in it, by clock, and it is checked in
  every week and pair it touches.
- **Working day.** Daily driving is that of the working day between rests of 9 h or more, with the same cut as the daily rest
  period (`SplitIntoWorkingDays`).
- **Own criterion, not legal text:** a working day of more than 9 h that crosses Monday counts in the week in which it starts.
- **Two consecutive calendar weeks.** The rolling window of 14 days is withdrawn as a limit.
- **Horizon.** `LegalComplianceEvaluation.LookbackWindow` = `DrivingWeek.LongestTwoWeeks` = 14 days and 1 h: the two
  calendar weeks of the autumn clock change. It is the only figure and the later services also use it (ADR 0016).

## Not checked
- With which clock the Spanish inspection counts the week (the Ministry page answered 403).
- Implementing Regulation 2016/799 (it could not be opened).
- To which week an extended working day that crosses Monday is attributed: the text read does not say.

## Consequences
- Stricter than before on night services and on those that cross Monday; more permissive in some cases that the rolling window of 14 days
  rejected without the text asking for it.
- An independent oracle, written from the text (`Article6Oracle`; neither the engine nor the time zone database), gives the
  same infringed articles as the engine over random calendars at any time, including the weeks of the two
  clock changes.
- The driver's hours, the warnings at 90 % and the state of the resources use the strictest figure. A card shows
  “Working day” where it said “Today”. The demo, with its Monday-to-Friday pattern, only has a driver
  near the two-week limit from Friday to Sunday.
- `DriverAvailability` (0030) and the card of 28 days (0061) do not change: they use the calendar day and do not decide CE 561/2006.
