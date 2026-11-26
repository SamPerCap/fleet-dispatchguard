# Fleet DispatchGuard design system

> SPEC 0057 (base), SPEC 0058 (visual language: light surfaces with navy blocks, tints, icons in a circle, charts) and SPEC 0060 (fluid layout, fixed header, command palette and motion).
> **Single source of the tokens:** `web/src/index.css` (`:root`). `web/tailwind.config.js` only exposes them to Tailwind.
> Changing a colour or a size = changing it in `:root` (and, if it is a size, in `fontSize` of the config). No component writes colours in hexadecimal (not even the brand illustration).

## 1. Principles

1. **Rhythm of surfaces.** Almost everything is light (`surface` over `bg`). The **navy** blocks (compliance engine, efficiency and sidebar) and the **critical tint** of the priorities with something urgent are the focal points: they are not used to decorate.
2. **Each figure with its shape.** Next to each KPI goes an icon in a coloured circle, and where there is a real series, its trend (sparkline), its ring or its curve. There is no chart without real data behind it.
3. **Strong typographic contrast and few sizes.** Figures are large, bold and black; labels, small and grey. There are six sizes (§3).
4. **Colour means something, per row.** Critical in red with **a single** strong button; high impact in amber; preventive in blue; notice in grey. The rest of the actions are discreet (“Change vehicle →”). The status is always said with text + icon + colour, never with colour alone.
5. **Honest figures.** There is no telemetry: “in service” is always labelled “by schedule”. Each figure has its ⓘ with the definition, and related ones add up (§8). If a module could not be refreshed, it says so itself: “Not updated for X min”.
6. **Soft depth and no constant motion.** Light shadows and generous radii. There are no pulses or glows. Transitions of 120–160 ms only respond to an interaction, and `prefers-reduced-motion` cancels them. When refreshing, figures do not re-animate and there are no layout jumps: skeletons are only seen on the first load. The only animations that do not respond to a click are the entrance of a view when opening it and the transition of a figure that **really changes** (§4.2). **Exception (user, 2026-11-25):** the wait for the database (`Traffic.tsx`) has vehicles in a loop, because there is no tool to use in the meantime; with `prefers-reduced-motion` they do not appear.
7. **Width is for the data (0060).** The web app takes the whole screen with a margin that grows with it; grids gain columns on wide screens and only prose is limited so it reads well.

## 2. Colour

Tokens are stored in RGB (`--token: R G B`) to be able to use opacity (`bg-navy/40`). Contrasts are calculated with the relative luminance of WCAG 2.1. AA requires 4.5:1 for normal text and 3:1 for large text and graphical components.

### Brand and semantics

| Token | Hex | Use | As text over `surface` |
|---|---|---|---|
| `navy` | `#101B30` | Navigation, focal blocks, main text (`ink`), “in service” | 17.19:1 ✅ |
| `navy-2` | `#172A4D` | End of the gradient of the navy surface | — |
| `blue` | `#2463EB` | Primary button, selection, focus, “now”, today | 5.17:1 ✅ (white on top: 5.17:1 ✅) |
| `blue-ink` | `#1D4FD0` | Blue text and discreet action (`.btn-link`) | 6.81:1 ✅ · 5.92:1 over `blue-soft` ✅ |
| `success` / `success-ink` | `#16A36A` / `#0E7A4E` | Success fill / text | 3.24:1 ⚠️ fill only · 5.37:1 ✅ |
| `warning` / `warning-ink` | `#D99524` / `#8F5B0A` | Warning fill / text | 2.54:1 ❌ fill only · 5.72:1 ✅ |
| `critical` / `critical-ink` | `#D94747` / `#B8302F` | Critical fill / text | 4.25:1 ⚠️ large text · 6.00:1 ✅ |
| `critical-strong` | `#9E2726` | Hover of the critical button (`.btn-danger`) | white on top 7.58:1 ✅ |
| `navy-tint` | `#C9D5EA` | “Allocated” bar in the strip and the timeline (visible over white) | `ink` on top 11.62:1 ✅ |

Soft backgrounds are the base colour at 10 % over white: `success-soft #E8F6F0`, `warning-soft #FBF4E9`, `critical-soft #FBEDED`, `blue-soft #E9EFFD` and `navy-soft #E4EAF3`. The `-ink` text over its soft background gives ≥ 4.8:1.

### Navy surface (0058)

It is used in `.surface-navy` (gradient from `navy` to `navy-2`) and in the sidebar. Text and accents carry their own tokens:

| Token | Hex | Use | Over `navy` · over `navy-2` |
|---|---|---|---|
| `on-navy` | `#FFFFFF` | Main text | 17.19:1 · 14.24:1 ✅ |
| `on-navy-2` | `#A9B6CC` | Secondary text | 8.39:1 · 6.95:1 ✅ |
| `blue-bright` | `#6EA0FF` | Curves, rings and bars | 6.66:1 ✅ |
| `success-bright` | `#34C38F` | “Active”, accepted, saving | 7.65:1 ✅ |
| `warning-bright` | `#F2B544` | Forced, “not updated” | 9.39:1 ✅ |
| `critical-bright` | `#FF7A7A` | Blocked | 6.81:1 ✅ |
| `side-fg` / `side-muted` | `#C7D0E0` / `#8E9AB2` | Sidebar text | 11.07:1 · 6.07:1 (5.03:1 over `navy-2`) ✅ |

### Light surfaces and text

| Token | Hex | Level / use | Contrast |
|---|---|---|---|
| `bg` | `#F2F5FA` | Level 0: canvas | — |
| `surface` | `#FFFFFF` | Level 1: modules, panels, cards | — |
| `sunken` | `#F7F9FC` | Level 2: zones inside a module (record header, lists) | — |
| `muted` | `#EEF2F7` | Chips, hints, skeletons | — |
| `line` | `#E3E8F0` | Thin separators (not frames) | — |
| `ink` | `#101B30` | Main text and figures | 17.19:1 |
| `ink-2` | `#475266` | Secondary | 7.87:1 (6.91:1 over `critical-soft`) |
| `ink-3` | `#5B667A` | Tertiary: the lightest allowed | 5.79:1 · 5.49:1 on `sunken` · 5.08:1 on `critical-soft` |

`.surface-critical` is the tint of “Priorities” when there is something urgent: a gradient from `critical-soft` to `surface`. All its text has been measured over `critical-soft`.

The aliases of 0047/0051 (`fg`, `primary`, `ok`, `warn`, `danger`, `info`, `border`, `side-on`…) were removed in 0058 together with the `.legacy` layer.

## 3. Typography

Inter (served by the web app itself, `@fontsource-variable`, SPEC 0089), with **tabular figures** in the whole document (`font-variant-numeric: tabular-nums`). There are six sizes and it never goes below 12 px:

| Class | Size / line height | Use |
|---|---|---|
| `text-display` | 36 / 40 px, bold | The figure of a KPI |
| `text-page` | 28 / 34 px, bold | Page title and card figures (`Stat`) |
| `text-module` | 16 / 24 px, semibold | Module title |
| `text-data` · `text-sm` | 14 / 20 px | Data; it is the base size of the `body` |
| `text-meta` | 13 / 18 px | Secondary |
| `text-xs` | 12 / 16 px | **Absolute minimum** |

## 4. Spacing, shape and shadow

- **Spacing in multiples of 8 px:** 8 · 16 · 24 · 32. Modules carry 24 px of padding and the separation between them is 24 px.
- **Radii:** `sm` 8 px (small controls) · `md` 10 px (buttons, fields, rows) · `lg` 16 px (modules and cards) · `xl` 20 px (brand header) · `full` (pills and icons).
- **Shadows:**
  - `shadow-1`: modules and cards at rest.
  - `shadow-2`: emphasis, KPIs of the header.
  - `shadow-navy`: navy blocks.
  - `shadow-pop`: tooltips, panels and dialogs.

### 4.1 Fluid layout (SPEC 0060)

- **No fixed width.** Each view is a `.page` (`width: 100%`, `max-width: var(--page-max)`, centred). The canvas carries `.gutter-x`: `--gutter = clamp(24px, 2.5vw, 48px)` (16 px below 640 px) → 32 px at 1280, 36 at 1440 and 48 from 1920.
- **High cap: `--page-max` = 2400 px.** With the sidebar (240 px) and two margins of 48 px, at 2560 px the content measures 2224 px: the cap does not act on any usual screen. It only holds back an ultra-wide one (3440 or 5120 px), where a four-column table would leave the cells too far apart to follow a row.
- **Breakpoints:** those of Tailwind (`lg` 1024, `xl` 1280, `2xl` 1536) and `3xl` = 1920 (`tailwind.config.js`). Being window breakpoints, they include the sidebar: at 1920 the content measures ~1580 px.

| View | lg / xl | 2xl | 3xl |
|---|---|---|---|
| Home: priorities + engine | 8/12 + 4/12 (xl) | = | = |
| Home: resources · activity · efficiency | 6 + 6, efficiency below at full width | = | 4/5/3 |
| KPIs (home, figures of each view) | one row of 4 from xl | = | = |
| Compliance engine: curve + categories | 8 + 4 | = | 9 + 3 |
| Planning: “Unallocated” queue | 300 px | 340 px | 380 px |
| Drivers: column of the last-minute absence | 400 px | 440 px | 480 px |
| Rule catalogue | 1 column | 2 columns | 2 columns |
| Strip: names column · max. height | 200 px · 460 px | = | 240 px · 560 px |
| Panels (`Sheet`) | `clamp(560px, 32vw, 680px)`; wide: `clamp(760px, 42vw, 960px)` | | |
| Driver/vehicle modal record (0061) | `min(1180px, 100vw − 48px)` × `min(920px, 100vh − 48px)` | = | `min(1320px, 100vw − 96px)` |

- **Prose:** no width cap; texts take their whole block (user request, 0070 and 2026-11-24).
- **Fixed header** (`.topbar`, 56 px = `--topbar-h`): canvas at 72 % with `backdrop-filter: blur(12px)`; the edge and the shadow appear only when scrolling (`data-scrolled`). It shows the path (group › view, text, not buttons) and the palette button. `html` carries `scroll-padding-top` with its height, so no `scrollIntoView` leaves the target underneath.
- When changing view, the page goes back to the top.

### 4.2 Motion (SPEC 0060)

| What | How | When |
|---|---|---|
| Entrance of a view | Direct children of `.page`: opacity 0 → 1 and 6 px → 0, 240 ms, steps of 40 ms (max. 200 ms) | Only when mounting the view; never when refreshing. `fill-mode: backwards`: when it ends no `transform` is left (it would break `position: fixed`) |
| Figure that changes (`AnimatedNumber`) | Interpolation of 400 ms with deceleration (`motion.ts:tween`) | Only if the value changes; the first paint and an equal refresh are not animated. Screen readers read only the final value |
| Palette | Appears in 160 ms (`.palette-pop`), background with a slight blur | When opening it |
| Hover of a clickable surface (`.lift`) | Rises to `shadow-2` in 160 ms | When hovering or focusing |
| Pressing a button | Goes down 0.5 px | While it is pressed |

With `prefers-reduced-motion: reduce` there is no animation at all (`animation: none`, no delays) and figures change instantly.

### 4.3 Layers

Canvas (`bg` with a very faint blue halo at the top, which is not fixed so as not to repaint when scrolling) → glass header → modules (`shadow-1`) → emphasis (`shadow-2`) → panels and palette (`shadow-pop`). The home header adds a gradient towards `blue-soft` and a halo under the illustration, without motion.

## 5. Components (`web/src/ui.tsx`, `web/src/charts.tsx`)

| Component | What it is | Rules |
|---|---|---|
| `PageHeader` | Common view header: icon in a circle, title, subtitle and actions | Used by all views except the home (which has its brand header) |
| `Module` | Surface with icon, title and actions; `tone` = `light`/`navy`/`critical`; `stale` shows the old-data notice | No border |
| `IconBadge` | Icon in a coloured circle (`blue`, `success`, `warning`, `critical`, `navy`, `neutral`, `onNavy`) | Always decorative: the text next to it says the same |
| `Stat` | Figure card: icon, value, label and footer; texts wrap, they are never cut (0070) | Real figures calculated in the view itself |
| `Avatar` | Initials (`people.ts:initials`) with a stable tone | Decorative; the name always goes next to it |
| `StatusPill` | General status (“Stable operation”, “Operation with warnings”, “Urgent action required”) | Its dot is the image “Traffic light …” for screen readers |
| `StatusBadge` | **The only** status component of a service or a booking | Closed taxonomy of `status.ts` (§6) |
| `BarLegend` | Legend of the strip and of the timeline | The sample looks the same as the bar |
| `Hint` | ⓘ with the definition, in a portal with fixed position | It opens with a click or focus; Esc or a click outside close it; it stays above everything (`z-60`) |
| `StaleNote` | “Not updated for X min” | Inside the affected module |
| `Skeleton`, `SkeletonLines`, `EmptyState`, `ErrorState` | Loading, empty and error | Each module shows its own error |
| `Sparkline`, `Ring`, `AreaChart`, `MiniBars` | Charts in our own SVG, without dependencies | They carry `role="img"` and a description with the values (`chartmath.ts:seriesText`). A series all at 0 is drawn on the baseline |
| `DayBars` (0061) | Bars per day (28 days of the record) with reference lines | Ceiling and tones from `history.ts` (`chartCeiling`, `barTone`: ≤ 9 h normal, > 9 h warning, > 10 h critical; figures from the API). The lines and their labels go above the bars; a day at 0 leaves a minimal stroke; description with `history.ts:historyText` |
| `GapLines` (0059) | Several series per hour with gaps (Compliance engine) | Same scale for all (`chartmath.ts:scaleSeries`); a gap (`null`) is an hour without data and is not drawn as 0; each data point carries its dot so that an isolated hour can be seen |
| `ExplanationBody` (`ExplanationDialog.tsx`, 0059) | Summary and the two layers of a 0042 explanation | Used by the ⓘ (dialog) and the panel of the attempt register |
| `AnimatedNumber` (0060) | Figure that is interpolated when it changes | Used by `Stat` (if the value is a number), the KPIs of the home and those of the Compliance engine |
| `ResourceModal` (0061) | Modal record of a driver or vehicle: header (initials/icon, identification, base, today's status) and tabs Summary, History, Audit and Agenda | Centred Radix Dialog; X, Esc and a click outside close it; focus goes back to whoever opened it. It lives in the URL (`?conductor=N` / `?vehiculo=N` + `&pestana=`, `resourceUrl.ts`): opening adds an entry (back closes it), changing tab replaces it. **No stacked modals:** opening a booking closes it and opens the panel; Ctrl+K closes it before the palette; the detail of an attempt is navigated inside with “← Back”. Replaces the side records of 0048 |
| Agenda in the timeline (0078) | `.agenda-band` (amber hatching with icon and type) for the absences of the row; `ConflictMark` + amber outline on the bar and “N in conflict with the agenda” in the row header | Never colour alone: icon, `title`/`aria-label` with who, what and when, and legend |
| `TripBlock` (0080) | Block of a trip in the booking panel: solid `navy-tint` band (`muted` if it is cancelled) with “Trip 2 of 4 · day” and its status; warnings; itinerary (time and place together); data in a `dl` with a visible label; the allocation form opens inside and then the band stays fixed at the top, and the button bar (“Confirm allocation”, “Cancel” and what was chosen) at the bottom, when scrolling | Border `ink-3` at 75 % (3.38:1 over white): what delimits something with meaning reaches 3:1, `line` does not. Cancelled, blocked, forced and agenda: one warning each, with icon, title and edge. The one opened from outside carries a blue ring and “Selected”. Contrasts in `bookingContrast.test.ts` |
| `CommandPalette` (0060) | Ctrl+K / ⌘K or the header button: views, bookings (from day −15 to +16, loaded when opening), drivers and vehicles | Search in `palette.ts:searchPalette` (without accents or capitals, all the words; groups by their best match, max. 6 per type). Choosing opens the same as normal navigation |
| `SearchPicker` (0077, 0082) | Field that searches while typing: places (`LocationPicker`), driver and bus when allocating and in “Last-minute absence” (0083); it shows the chosen one with its detail | At most 20 options and the note of how many remain; only what is chosen counts, never what is typed. What cannot be chosen only comes out when searching for it, at the end and with its reason. List in the top layer: a panel does not clip it; Esc closes only the list and Enter never submits the form |
| `LofMark`, `BrandScene` (`brand/BrandArt.tsx`) | Logo and the illustration of the coach over the city | Drawn by hand for this repo, with token colours, and `aria-hidden` |

### Priority tiers (SPEC 0058 R5)

The backend decides the severity, the grouping and the order; the web app only labels (`control.ts:incidentTier`):

| Tier | When | Style |
|---|---|---|
| Critical | Urgent incident | Red bar and tint; the first one carries the only strong button (`.btn-danger`) |
| High impact | Vehicle not available, gap due to unavailability or uncovered service | Amber |
| Preventive | Close to the driving limit or forced allocation | Blue |
| Notice | Incomplete booking | Grey |

### Buttons

There is **a single strong action per context**:

| Class | What it is | When it is used |
|---|---|---|
| `.btn-primary` | Blue | Main action of a view |
| `.btn-danger` | Red | The first priority, if it is critical |
| `.btn` | Secondary | Normal actions |
| `.btn-text` | Text | Tertiary actions |
| `.btn-link` | Link | Discreet action, with its arrow |

The clickable area always measures at least 44 × 44 px (`::after`). Over navy, actions are white text buttons with a `white/10` background on hover.

## 6. Status taxonomy (0057 R9, closed)

| Key | Text | Bar in strip and timeline | When |
|---|---|---|---|
| `planned` | Planned | Neutral dashed | It lacks driver or vehicle and leaves **after today** |
| `uncovered` | Uncovered | Red dashed with hatching | It lacks driver or vehicle and leaves today or should have left already |
| `assigned` | Allocated | `navy-tint` | It has driver and vehicle and has not departed yet |
| `running` | In service by schedule | `navy` | It is within its schedule |
| `completed` | Completed | `muted` | Its end has passed or it was marked as completed |
| `forced` | Forced | Amber edge on the left | CE 561/2006 was forced; it is shown in addition to the time status |
| `partial` | Partial | — | Booking with part of its trips covered |

The status of a **resource** (available, not available, inactive, external) is not a service status. It is shown with its own pill (`ResourcesPages.tsx:ResourceState`), also with text, icon and colour.

## 7. Accessibility

- **Visible focus:** blue outline of 2 px separated 2 px; over navy (`.on-navy`, `.surface-navy`), white.
- **Keyboard:** everything that looks clickable is a `<button>` and does something real; what has no target does not look like a button. Activity rows open their detail.
- **Screen readers:**
  - Skeletons carry `role="status"` with hidden text.
  - Charts carry `role="img"` with the series described.
  - The bars of the strip and of the timeline have a complete label.
  - In the sidebar there is a single element with `aria-current="page"`.
- **Palette:** combobox + listbox pattern with `aria-activedescendant`; ↑/↓, Home/End, Enter and Esc; when closing it without choosing, focus goes back to where it was.
- **`prefers-reduced-motion: reduce`** cancels all transitions and animations.

## 8. Consistency of figures (contract with the backend)

- **Tasks = urgent + notices = number of incidents** (0057 R4).
- **Validations = accepted + blocked + forced**, also in each hour of the curve, and the sum of the hours equals that of the day (0058 R2).
- **Compliance engine (0059):** KPIs, curve and register come from the same filter. Each KPI equals the number of rows of the register filtered by that result, and the sum of the curve equals the validations of the period (with the same filters except the result).
- **Trend of 7 days:** the last point equals today's summary and the second to last, yesterday's figures (0058 R1).
- **Resources:** each active resource is in a single status, so the sum of statuses is the total.
- **Fleet utilisation** = allocated hours / available hours. The available ones are the fixed working day of each base (06:00–22:00 by default, configurable in `Control:OperatingHours`) minus the workshop. It never exceeds 100 % (0058 R3).
- **Km saving** (report, 0069) = empty km of the current plan → those of the optimised plan, with the same calculation as 0043; services that have already departed keep their bus and count in both figures (0070); no bus departs before now and the week is that of Madrid (0071). A % of empty km over the total is not shown.
- **History of the record (0061):** hours per calendar day in Madrid of services already finished; the sum of the days = the total; services and km count on their start day. Km = estimated in a straight line along origin → stops → destination; a service with the same origin and destination without stops has no km (it is counted apart, never invented). The audit of the record is the 0059 register filtered by the resource in the same window: its figures match those of the Compliance engine.
- **Versus yesterday:** always in absolute value (“+3 vs yesterday”); if yesterday was 0, “new today”.

## 9. What is not done

The following are not added:

- map, “real time” or telemetry;
- full-screen marketing heroes, video, 3D, parallax or looping animations (0060: they take away space, readability and performance from a daily tool);
- time saved;
- invented composite indices;
- “Guaranteed compliance” (“0 illegal allocations unforced today” is used, which is true by construction);
- “AI” with the brand name (“Recommendations / Km saving” is used);
- bell, invented profile (the name from “Allocating as” is used), “Reports” or “Settings”.

In form: no text below 12 px, colour as the only signal, blue to decorate, or bordered cards for everything.
