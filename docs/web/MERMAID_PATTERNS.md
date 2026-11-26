# Mermaid Patterns — Reusable Templates

Templates ready to copy when documenting a new feature in `docs/index.html`. See also the escaping rules in [docs/DEVELOPMENT.md](../DEVELOPMENT.md#mermaid-diagram-conventions).

---

## Template 1: Mind Map (overview, non-technical audience)

Use it to summarise input → validations → decision → output of any feature, without technical jargon.

```
mindmap
  root((FeatureName))
    Input
      Field1
      Field2
    Validations
      RuleA
      RuleB
    Decision
      Key question
    Output OK
      Successful result
    Output Rejection
      Reason for the rejection
```

---

## Template 2: Flowchart (workflow of an endpoint or process)

Use it to show the happy path + error path of an endpoint, with coloured layers.

```
flowchart TD
    Start["Request received"] --> Validate["Validate input"]
    Validate --> Decision{Is it valid?}
    Decision -->|Yes| Process["Process"]
    Decision -->|No| Error["Return error"]
    Process --> Save["Persist"]
    Save --> Ok["200 OK"]
    Error --> Bad["400 Bad Request"]

    style Start fill:#dbeafe,stroke:#0c4a6e,stroke-width:2px
    style Validate fill:#dcfce7,stroke:#166534,stroke-width:2px
    style Process fill:#f3e8ff,stroke:#6b21a8,stroke-width:2px
    style Save fill:#fed7aa,stroke:#92400e,stroke-width:2px
    style Ok fill:#d1fae5,stroke:#065f46,stroke-width:3px
    style Error fill:#fee2e2,stroke:#7f1d1d,stroke-width:2px
    style Bad fill:#fee2e2,stroke:#7f1d1d,stroke-width:3px
```

**Colour legend per layer** (use consistently in all diagrams):

| Colour | Hex | Layer |
|-------|-----|------|
| Blue | `#dbeafe` | Web.Api |
| Green | `#dcfce7` | Application |
| Purple | `#f3e8ff` | Core |
| Orange | `#fed7aa` | Infrastructure |
| Success green | `#d1fae5` | OK result |
| Red | `#fee2e2` | Error result |

---

## Template 3: Swimlanes (parallelisation of agents)

Mermaid has no native "swimlane" diagram type; the standard pattern is to use `flowchart` with one `subgraph` per lane and dotted connections (`-.->`) for dependencies between lanes.

```
flowchart LR
    subgraph Lane1["Lane 1"]
        A1["Step 1"] --> A2["Step 2"]
    end
    subgraph Lane2["Lane 2"]
        B1["Step 1"] --> B2["Step 2"]
    end

    A2 -.->|hands over| B2
    B2 --> Sync["Synchronisation point"]

    style Lane1 fill:#f3e8ff
    style Lane2 fill:#dcfce7
    style Sync fill:#d1fae5
```

---

## Template 4: State Diagram (states of an entity)

```
stateDiagram-v2
    [*] --> State1
    State1 --> State2: Event1
    State2 --> State1: Event2
    State2 --> [*]
```

---

## Pre-Commit Validation Checklist

Before committing a new or modified Mermaid diagram:

- [ ] No node ID has spaces, is empty, or is repeated inside the same diagram
- [ ] There are no unescaped parentheses `()` inside labels or `subgraph` titles (use `-` or HTML entities `#40;`/`#41;` if they are essential)
- [ ] Single/double quotes inside a label use `&quot;` instead of literal quotes
- [ ] Edge labels only carry quotes if they contain `:` or `/`
- [ ] The diagram has a descriptive `<p>` before or after it (accessibility / screen readers)
- [ ] Validated locally: `npx @mermaid-js/mermaid-cli -i diagram.mmd -o diagram.svg` ends without an error
- [ ] Reviewed in the browser: console without Mermaid errors when opening/switching tab
- [ ] Colours follow the standard legend (blue=Web.Api, green=Application, purple=Core, orange=Infrastructure)

---

## Origin

Created in SPEC 0011 (not published).
