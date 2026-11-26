# Security policy

## Reporting a vulnerability

Please report it privately through GitHub: **Security → Report a vulnerability** in this repository (private vulnerability
reporting). Do not open a public issue. Include what you did, what you expected and what happened; a request and its response are
enough, no exploit is needed.

You will get an answer within 7 days. A confirmed issue is fixed in the code first and then in the public demo, and the report is
credited unless you prefer otherwise. There is no bug bounty.

## Scope

In scope: the code in this repository and the public demo built from it (API, web, session, perimeter, X-ray, audit).

Out of scope: volumetric denial of service against the demo, social engineering, the hosting platform and the managed database
(report those to their providers), findings that need the private keys of the deployment, and the demo data, which are fictitious
and reset every day.

Please test only with your own session, do not try to read another visitor's data beyond proving it is possible, and stop at the
first proof.

What is defended and how, threat by threat, with the test behind each mitigation: [`docs/THREAT_MODEL.md`](docs/THREAT_MODEL.md).
