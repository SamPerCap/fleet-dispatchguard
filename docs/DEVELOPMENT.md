# Fleet DispatchGuard — Development Guide

Quick reference of conventions for contributing to the project. The full spec-driven development flow lives in the private working repository; [`specs/samples/`](../specs/samples/) shows three SPECs as they were closed.

---

## Mermaid Diagram Conventions

`docs/index.html` uses [Mermaid v11](https://mermaid.js.org/) (loaded from jsdelivr, pinned to `@11` for stability) to visualise workflows, architecture and states. Follow these rules to avoid rendering errors in the console.

### 1. Node IDs

- Alphanumeric, no spaces, no special characters (`camelCase` or `snake_case`).
- Never leave an ID empty (`""["label"]`) nor duplicate an ID inside the same diagram.

```
INCORRECT:  node 1["label"]        // space in the ID
INCORRECT:  [node]["label"]        // special characters in the ID
CORRECT:    node1["label"]
CORRECT:    driverCheck["Driver Compliance"]
```

### 2. Escaping Characters in Labels

Parentheses and quotes inside a label can confuse the parser even between square brackets. Avoid them or use safe alternatives:

```
INCORRECT:  node1["Driver: Validating (9h) - OK"]
CORRECT:    node1["Driver: Validating 9h - OK"]        // avoid parentheses
CORRECT:    node2["Driver: Validating #40;9h#41;"]      // or HTML entity if they are needed

INCORRECT:  node3["Trip with 'quotes'"]
CORRECT:    node3["Trip with &quot;quotes&quot;"]       // HTML entity
CORRECT:    node4["Trip with quotes"]                    // or avoid quotes
```

This rule also applies to `subgraph` titles: use `subgraph id["Title without parentheses"]`.

### 3. Edges / Connections

Only use quotes in an edge label when the text has special characters (`:`, `/`).

```
INCORRECT:  node1 -->|"check" fails| node2   // unnecessary quotes
CORRECT:    node1 -->|Check fails| node2
CORRECT:    node1 -->|"Check: CE 561/2006"| node2
```

### 4. Initialisation in HTML

The CDN is loaded only once in `<head>`, pinned to major version 11:

```html
<script src="https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js"></script>
```

`mermaid.initialize({ startOnLoad: true, ... })` processes all the `.mermaid` present in the DOM when the page loads, even those inside hidden tabs (`display: none`). If you need to re-render content added dynamically, filter by `:not([data-processed="true"])` before calling `mermaid.run()` — never reprocess a node already rendered.

### 5. How to Debug Errors

1. Open `docs/index.html` in the browser and check the console (F12).
2. A Mermaid syntax error appears as `Parse error on line X` with the exact text that failed.
3. Copy the diagram block to [mermaid.live](https://mermaid.live/) — the online editor shows the error with more context and a live preview.
4. Fix it following the rules of this section and paste again to confirm.
5. Before committing, validate locally with `npx @mermaid-js/mermaid-cli -i diagram.mmd -o diagram.svg` — if the command ends without an error, the syntax is valid.

### 6. Reusable Templates

See [docs/web/MERMAID_PATTERNS.md](web/MERMAID_PATTERNS.md) for Mind Map, Flowchart and Swimlanes templates ready to copy into new features, together with a pre-commit validation checklist.

---

## Build & Test

```bash
dotnet build DispatchGuard.slnx
dotnet test
dotnet test Tests/Core.Tests/Core.Tests.csproj
dotnet test /p:CollectCoverage=true
```

## EF Core Migrations

```bash
dotnet ef migrations add MigrationName --project src/Infrastructure --startup-project src/Web.Api
dotnet ef database update --project src/Infrastructure --startup-project src/Web.Api
```

## Naming Conventions

Naming of classes, methods, properties and branches follows the conventions of the private working repository (`CLAUDE.md`).

---

## Sensitive Files & .gitignore

The root `.gitignore` excludes several categories of files for reasons of security and repository hygiene (SPEC 0010, not published):

- **Sensitive configuration**: `appsettings.*.json` (Development/Production/local) is excluded — only the base `appsettings.json` (without secrets) is tracked. Once committed, a credential stays in the git history permanently even if it is deleted later, so this rule is defence in depth, not just cleanup.
- **Environment variables**: `.env`, `.env.local`, `.env.*.local`.
- **Explicit credentials**: `**/secrets/`, `**/credentials/`, `*.secrets.json`, `*.private.json`.
- **IDE/OS/build/test artifacts**: `.vs/`, `.idea/`, `bin/`, `obj/`, `TestResults/`, `Thumbs.db`, etc. — regenerable or specific to each developer, they add no value to the history.
- **Claude Code local state**: `.claude/worktrees/` (ephemeral copies of the repo per agent session) and `.claude/settings.local.json` (personal override) — `.claude/launch.json` and `.claude/settings.json` (without `.local`) are tracked because they are shared team config.

### How to work with local configuration

1. Copy `appsettings.json` (or the `appsettings.Development.json` you already have locally) and adjust connection strings / API keys for your environment.
2. Never remove the `appsettings.*.json` entry from `.gitignore` to "fix" that your local file does not show up in `git status` — it is the expected behaviour.

### Forcing the commit of an excluded file (exceptional)

If you need to commit a file that falls under an excluded pattern (e.g. a test fixture that must be reproducible for the team):

```bash
git add -f path/to/file.txt
git commit -m "Add excluded file (justification)"
```

Always document the reason in the commit message.
