# ADR 0022 — Perimeter: chained limits per session and per address

- **Status:** accepted · **Origin:** SPEC 0089

## Context
The demo will face the Internet without login. The 120 requests per minute and IP of SPEC 0044 cut “Allocate week” short, let anyone repeat the heaviest calculation 120 times, and behind a proxy were one quota for everybody.

## Decision
- **Two ceilings at once.** The session keeps one browser from taking everything from those that share its address; the address (an IPv6 /64) is the real ceiling, because a session is thrown away by clearing cookies and the reads need none. Creating sessions has its own limit, so that they are not a way around the first one.
- The limits of the session go **first**: what a browser wastes is not charged to its neighbours.
- Authentication runs before the limiter, which has to know the session. It costs one decryption per refused request and no query.
- What is heavy is said by the endpoint (`[HeavyCalculation]`), not by its path. The “Recommended” badge is not heavy: it costs as much as the lists that open with it.
- **Forwarded headers are configured always**, also to nothing. `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, which some hosts set by themselves, trusts any sender.
- The seeding is exempt by its key and not by its address: in the deployment it does not come from loopback.
- What cannot be safe by default stops the start: `AllowedHosts` that allows any host, a trusted network that is everybody, a limit under 1.
- The headers are written when the answer starts, because the exception handler wipes the ones written before.

## Consequences
- The quotas live in the memory of each instance: with several, each has its own. The demo has one.
- An office behind one address shares 600 requests and 10 new sessions per minute.
- The deployment has to set `AllowedHosts` and `ForwardedHeaders:TrustedNetworks`. Without the second, the HTTPS redirection of an API behind a proxy never ends.
- The allocate endpoint no longer reads the query string: a script that used it gets a 400.
