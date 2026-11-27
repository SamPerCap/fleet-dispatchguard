# Architecture Decision Records

Decisions already taken, one per file (Context · Decision · Consequences · Origin). The living table is still in [`docs/PROJECT_DEFINITION.md §4`](../PROJECT_DEFINITION.md).

| ADR | Decision |
|---|---|
| [0001](0001-validacion-legal-antes-de-asignar.md) | Legal validation before allocating, fail-fast |
| [0002](0002-cifras-ce561-en-una-sola-fuente.md) | CE 561/2006 figures in a single source |
| [0003](0003-historial-derivado-de-servicios.md) | Driving history derived from the services |
| [0004](0004-multiempresa-db-por-tenant.md) | Multi-company: one DB per company, rules as data |
| [0005](0005-m1-despachador-una-empresa.md) | M1: the dispatcher's tool for one company |
| [0006](0006-disponibilidad-aproximada.md) | Approximate daily availability |
| [0007](0007-ranking-de-candidatos.md) | Candidates: hard legal filters, a ranking that does not exclude |
| [0008](0008-descanso-semanal-no-verificable.md) | Weekly rest period not verifiable = warning |
| [0009](0009-road-distances.md) | Road distances with a cache |
| [0010](0010-api-minimal-y-problemdetails.md) | Minimal APIs and ProblemDetails |
| [0011](0011-web-react-sin-librerias-pesadas.md) | React + TS web without heavy libraries |
| [0012](0012-datos-de-ejemplo-crtm.md) | Sample data with real CRTM stops |
| [0013](0013-optimizador-heuristico-sincrono.md) | Synchronous, read-only heuristic optimiser |
| [0014](0014-reinicio-de-demo-por-endpoint-con-clave.md) | Demo reset: endpoint with a key from configuration + reseeding through the API |
| [0015](0015-forzar-asignacion-con-motivo.md) | Forcing an allocation: CE 561/2006 only, with a reason, audited and visible |
| [0016](0016-servicios-posteriores-ya-asignados.md) | Allocating cannot make a later, already allocated service illegal |
| [0017](0017-semana-y-jornada-del-articulo-6.md) | Week in Madrid time and UTC, working day between rests and two calendar weeks |
| [0018](0018-asignaciones-simultaneas.md) | Simultaneous allocations: application lock in SQL Server, in a fixed order |
| [0019](0019-conductor-de-baja-o-inactivo.md) | A driver on sick leave or inactive is neither allocated nor forced; a single criterion |
| [0020](0020-recomendar-asignacion.md) | Recommend an allocation: simulate with the same classes and apply through `AllocateDriver` |
| [0021](0021-anonymous-session.md) | Anonymous session: a signed cookie, required to write |
| [0022](0022-perimeter.md) | Perimeter: chained limits per session and per address |
| [0023](0023-cloud-ready.md) | One container that serves everything, in front of a database that sleeps |
| [0024](0024-postgresql.md) | PostgreSQL in the place of SQL Server: instants, case and accents, locks |
| [0025](0025-public-deployment.md) | Public deployment: nothing without a session, a daily rebuild and a release by hand |
| [0026](0026-audit-trail.md) | Audit trail: in the transaction, append only, and never wakes the database on its own |
| [0027](0027-one-repository.md) | One repository for the API and the web: one deployment, one origin, changes that land whole |
