# ADR 0027 — One repository for the API and the web

- **Status:** accepted (user, 2026-11-27) · **Completes:** ADR 0023 (one container) and ADR 0025 (release by hand)

## Context
Splitting the frontend and the backend into two repositories is common, and a reader used to it will ask why `web/` lives next
to `src/`. The split pays off when separate teams ship on their own schedules, or when the API has other clients that need it
versioned on its own. Neither holds here: one developer, and the web is the API's only client.

## Decision
The API and the web stay in one repository, as two units with their own build and tests (`src/` with `dotnet`, `web/` with
`npm`), because they are deployed, changed and tested as one:

- **One deployment, one version.** A single image serves both from the same origin (ADR 0023), and `release.ps1` moves a
  single `production` branch (ADR 0025). With two repositories a new web could reach users before the API it needs, or the
  other way round, and the release would have to pin and order two versions.
- **The same origin is a security decision.** The session cookie never leaves the site (ADR 0021), so there is no CORS to open
  and no `SameSite=None` cookie; the CSP only admits the page's own origin. Separate repositories would push towards separate
  hosts and give these up.
- **A change lands whole.** Most features touch both sides: an endpoint and its screen, an error code and the text that
  explains it (SPEC 0095), a contract field. One commit carries both and CI tests them together; there is no window where the
  contract and its client disagree.
- **Tests cross the line.** The E2E and ZAP run the image itself (web against the real API), and `noRequests.test.ts` checks
  what the web may load before it talks to the API. In two repositories these would need a published artefact of the other side.

## Discarded
- **Two repositories with a versioned API contract** (OpenAPI client generated in the web): the right shape for several clients
  or teams; here it adds a release to coordinate for every feature and no reader of the contract other than the web.
- **Two repositories deployed to two hosts** (web on a CDN, API apart): CORS, a cross-site session cookie and a wider CSP for a
  demo whose web is six files.

## Consequences
- The boundary is kept by folders, not by repositories: `web/` never imports from `src/` and talks to it only over HTTP. If the
  API gains another client or another team, the split is a move of `web/` plus a generated client.
- One CI runs both builds; a change on one side runs the other's tests too, which costs minutes, not correctness.
