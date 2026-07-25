# ADR 0063 — bUnit UI test guards

## Status

Accepted — implemented. Starts the UI test net deferred since ADR 0025.

## Context

The Blazor WASM client had **no** automated tests — a growing risk as the UI took on real logic
(recurrence editor, bulk actions, and the ADR 0062 collections pane with cross-collection merge,
filtering, active-collection targeting, and colours). Regressions could only be caught by hand.

## Decision

Add a **bUnit** (MIT) component-test project, `tests/SimplCalCon.WebTests`, registered in
`SimplCalCon.slnx` so the existing CI `dotnet test SimplCalCon.slnx` runs it on both providers.

- **`ApiHarness`** — a shared helper that wires a page for rendering against a **fake `/api`**: an
  `ApiClient` backed by an `HttpMessageHandler` returning canned JSON per GET path, a never-called
  `IAccessTokenProvider` stub, the real `LiveUpdates` (inert without a hub connection —
  `SubscribeAsync` no-ops while disconnected, so no server is needed), loose JS interop (localStorage,
  the `columnResize`/`splitter` module imports all no-op), and an authorized test user.
- **Guards added:**
  - `CollectionColors` — stored colour used verbatim; missing → a stable palette hue per id.
  - `CollectionsPane` — one row per collection, name/swatch rendering, shared collections have **no**
    colour picker, the active row is highlighted, and the checkbox/name/colour interactions raise
    `OnFilterChanged` / `OnActiveChanged` / `OnColorChanged` (and mutate the checked set).
  - `CalendarView` + `Contacts` — the pane lists every collection; the list **merges** entries from
    all checked collections with a **colour column + collection column**; activating a collection
    moves the highlight; unchecking one filters its entries out.

Test-only transitive `AngleSharp` (bUnit's HTML parser) trips `NU1902` (moderate); it's covered by the
repo-wide `WarningsNotAsErrors` for `NU19xx` (a test dependency, not shipped), and the license gate
scans only the Api tree.

## Consequences

- The highest-value UI paths — the shared pane and both merged tabs — now have a regression net that
  runs in CI without a browser or a live backend.
- The `ApiHarness` makes future page tests cheap (register canned GETs, render, assert).

## Deferred

- Interaction coverage of the editors (recurrence, event create/edit, raw vCard), bulk actions, and
  the modals; end-to-end (browser) tests remain out of scope.
