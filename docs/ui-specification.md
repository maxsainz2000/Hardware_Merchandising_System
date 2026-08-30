# UI Specification

**Status: written at P5-14, covering all three WPF clients as they stand at Phase 5 close.**
`plan.md` §7 places this document here on purpose: *"the shared visual system is settled once
POS forces the hardest layout decisions."* Every rule below is stated so a **fourth screen** —
inside an existing client or a new one — could be built from it without looking at any of the
three that exist; none of it is a description of what those three happen to do. Every fact this
document states about the running clients is checked on every test run by
`UiSpecificationDocumentationTests` — a drift check in the P2-12/P3-08/P4-13 shape, so a window
resized, retitled, or a status-bar binding renamed without updating this file fails that suite
rather than shipping a stale document.

**Scope note.** This system is an **academic prototype**. MariaDB is supplied through XAMPP
because the course requires it (ADR-000), and Visual Basic is a binding course requirement
(CLAUDE.md §2). Nothing in this document — including the visual system it describes — should be
read as a production-readiness claim; `plan.md` §5 requires this framing to appear in every
Phase 5+ document, not only the ones that touch the database.

**Known gap this document does not paper over.** Spec §16 describes "XAML resource dictionaries,
reusable styles, templates, converters, custom controls where justified, and shared client
resources." As built through Phase 5, `Merchandising.ClientCommon` carries shared **code**
(`MerchandisingApiClient`, `ApiFailurePresenter`, `ObservableObject`,
`InverseBooleanToVisibilityConverter`) but **no shared XAML** — no `Styles.xaml`, no theme
`ResourceDictionary`, nothing merged across the three `Application.xaml` files. Each client's
`MainWindow.xaml` inlines its own `BooleanToVisibilityConverter`/`InverseBoolToVis` resources and
its own window chrome. The "shared visual system" this document records is real but is currently
a **convention independently reproduced three times**, proven identical by inspection and by
`UiSpecificationDocumentationTests`, not enforced by a single shared resource a fourth client
would automatically inherit. `plan.md` §7's Phase 7 "UI refinement pass across all three clients
for consistency" is the natural place to promote these conventions into an actual shared
`ResourceDictionary` in `ClientCommon`; until then, a fourth screen must copy the rules below by
hand, and this document is what to copy them from.

---

## 1. Screen inventory

| Client | Role (spec §9) | Screens (`TabItem` headers) |
|---|---|---|
| `Merchandising.Procurement` | Procurement Officer, and roles above it | Sign in · Suppliers · New Order · Orders · History |
| `Merchandising.Inventory` | Inventory Clerk, and roles above it | Sign in · Stock · Receive · Counts · Adjustments · Low stock |
| `Merchandising.POS` | Cashier | Sign in · Session · Checkout · Returns |

Sign in is not a `TabItem` in any client — it is a panel shown in place of the tab workspace
while `IsSignedIn` is false (§3). Every other screen listed is one `TabItem` inside a single
`TabControl` that fills the window's workspace row.

---

## 2. Window chrome and the layout grid

Every client's `MainWindow` is one `Window` containing one `Grid`, `Margin="12"`, with exactly
three rows:

```
Row 0  Auto   Top bar    — title, connection state, current user, sign out
Row 1  *      Workspace  — the sign-in panel, or the TabControl, whichever IsSignedIn selects
Row 2  Auto   Status bar — the outcome of the last API call, and the maintenance banner
```

**Top bar** (`DockPanel`, `LastChildFill="False"`): the client's title docked left
(`FontSize="18" FontWeight="Bold"`, e.g. *"Merchandising - POS"*), then — docked right, in this
order — a **Sign out** button, the current user (`CurrentUserDisplay`), and the connection state
(`ConnectionText`). The sign-out button and both text blocks are bound through the same
`BoolToVis` converter on `IsSignedIn`, so the top bar carries only the title and the connection
state while signed out.

**A fourth screen inherits this shape unchanged**: one `TabItem` added to the existing
`TabControl`, its content a `Grid` with its own `RowDefinitions` sized to that screen's own
content, `Margin="8"` on the screen's root `Grid` (not the window's outer `12`). No screen owns
its own top bar or status bar — those are the window's, once, for every screen.

---

## 3. The 1366×768 @ 125% constraint, as a number

Spec §16: *"laptop screens at 1366×768 and above... usable at 125% display scaling."* WPF lays
out in DIPs (1 DIP = 1/96"), so that physical display is `1366 / 1.25 = 1092.8` DIP wide. A
default Windows 11 taskbar is 48 physical pixels tall, i.e. `48 / 1.25 = 38.4` DIP, and a window
may not open underneath it, so the **work area** is:

```
1092.8 × 576.0 DIP        ( = (1366/1.25) × ((768-48)/1.25) )
```

`Window.Width`/`Height` include the caption and border, so all four declared values must fit
inside that work area, and **`MinHeight`/`MinWidth` are the values that matter** — a minimum
larger than the work area can never be resized to fit, so whatever falls outside it leaves the
screen and no amount of dragging brings it back. This is not hypothetical: the Phase 3 gate found
exactly this defect in an earlier `Merchandising.Procurement` window, and `plan.md`'s own Phase 3
closure note carries it forward by name.

All three clients currently declare the same four numbers, each comfortably inside the budget:

| | Declared | Budget | Fits |
|---|---|---|---|
| `Width` | 1024 | 1092.8 | yes |
| `Height` | 560 | 576.0 | yes |
| `MinWidth` | 960 | 1092.8 | yes |
| `MinHeight` | 520 | 576.0 | yes |

**The rule a fourth screen (or a fifth client) must follow:** before raising `Height`, `Width`,
`MinHeight`, or `MinWidth` on any client window, recompute this table and confirm every value
still fits the same 1092.8 × 576.0 DIP budget. `ProcurementLayoutTests`,
`InventoryLayoutTests`, and `POSLayoutTests` each assert their own client's four numbers against
this arithmetic on every test run — a window whose declared size no longer fits fails the suite,
not the demo.

A screen's *content*, laid out inside the window at its own `MinWidth`/`MinHeight`, must not
starve a `DataGrid` below the height of a header plus one row (48 DIP) — the same three test
suites assert this per tab, for every screen currently built. Use proportional row heights
(`1.4*`, `1*`, with a `MinHeight` floor on each) for a screen with two grids competing for
vertical space, never a fixed pixel height — a fixed height is what produced the Phase 3 defect
in the first place (see the comment carried in `Merchandising.Procurement/MainWindow.xaml`'s own
"New Order" tab).

---

## 4. Keyboard and focus conventions

Spec §16 requires keyboard navigation, visible focus states, and predictable tab order on every
screen; spec §16's POS requirement is stronger — *"POS keyboard workflows are tested without a
mouse for search, cart changes, payment, completion, and session closing"* — and P5-13 built to
that standard for every POS screen, not only those five actions.

**Every interactive control carries an explicit `TabIndex`.** WPF visits tab stops in `TabIndex`
order, so the authored numbers *are* the keyboard focus order. The convention, identical across
all three clients:

- **0–2 — sign-in panel**: username (`0`), password (`1`), the Sign in button (`2`,
  `IsDefault="True"` so Enter submits it).
- **10, 20, 30, …, up to the 90s — one screen (`TabItem`) each**, ascending within a screen in
  the order the XAML declares the controls, so Tab order and reading order agree. A screen with
  several logical groups (e.g. POS Checkout's product search, product grid, cart, and payment
  controls) numbers them as one ascending run, not restarted per group.
- **99 — the window chrome**: the Sign out button, always last, regardless of which screen is
  selected.

No two controls anywhere in one window share a `TabIndex`, and no interactive control is left
without one — an unnumbered control's position in the tab sequence is whatever the visual tree
happens to yield, which is exactly the failure mode this convention exists to rule out.
`ProcurementLayoutTests`, `InventoryLayoutTests`, and `POSLayoutTests` each assert both
properties — uniqueness and per-screen ascent — over every `TextBox`, `PasswordBox`,
`ButtonBase` (including `CheckBox`), `ComboBox`, and `DataGrid` in their client's window, walking
the **logical** tree so every `TabItem`'s content is checked whether or not that tab has ever
been selected.

**A default action per screen.** Where a screen has one obviously primary action reachable from
a text box (signing in, looking up a product, searching a list), that button carries
`IsDefault="True"` so Enter triggers it without a Tab. This is authored per screen, not global —
a screen with no single obvious default action (e.g. POS Checkout, which has both a lookup and a
Complete-sale action) does not force one.

**What this convention does not assert.** The `TabIndex`/`IsDefault` properties are declarative
inputs to WPF's own focus traversal, not evidence that traversal happens as declared. `*LayoutTests`
assert the numbers themselves (unique, ascending, present); that a person pressing Tab actually
lands where those numbers predict is confirmed once by hand per client and recorded in that
client's own evidence file (POS: `evidence/phase-5/p5-13-pos-client.txt` §2) — not re-asserted
here or by any automated suite. **Access keys (Alt+letter mnemonics)** are not used anywhere in
any of the three clients as of Phase 5 — every control is reached by `Tab`/`Shift+Tab` and
activated by `Enter`/`Space`, never by an underlined-letter shortcut. A future screen that adds
one should update this paragraph.

---

## 5. Error and refusal presentation

CLAUDE.md §5: *"errors never leak internals"* and *"the client never invents its own wording"* —
except in the one situation where the API was never reached, where there is no wording to
inherit. `Merchandising.ClientCommon.Api.ApiFailurePresenter.Describe` is the single place this
logic lives (used by every view model in every client — P3-07's own reason for factoring it out:
five view models needing the identical three-outcome logic is five chances to drift if copied by
hand). It produces exactly one of three outcomes:

| Outcome | `StatusMessage` shown | `Detail`/`ResultText` shown | Correlation ID |
|---|---|---|---|
| **Unavailable** — the API was never reached | Fixed client sentence: *"The API could not be reached. Nothing was sent and nothing was saved."* | Fixed client sentence stating the request was not queued and will not retry automatically, plus the transport error | **None** — no correlation ID exists, because no request was ever logged server-side |
| **Maintenance** — `503` with error code `MAINTENANCE_MODE` | Fixed client sentence: *"The system is under maintenance. Your change was not saved."* | **The API's own `Message`, verbatim**, plus a fixed sentence that nothing was written and this client does not retry | Shown |
| **Rejected** — any other API refusal | **The API's own `ErrorCode`, verbatim** (e.g. `INSUFFICIENT_STOCK`, `VALIDATION_FAILED`) | **The API's own `Message`, verbatim** | Shown |

**The rule a fourth screen must follow:** never construct a `StatusMessage`/`ResultText` pair by
hand from a failed `ApiResult(Of T)`. Call `ApiFailurePresenter.Describe` and bind to its output.
The two fixed client-authored sentences (Unavailable and the non-message half of Maintenance)
are the *only* wording in this system that is not the API's own — both name themselves as such
(*"could not be reached"*, *"could not be reached"*'s no-correlation-ID case, and the
maintenance framing sentence) and neither ever substitutes for or rewrites what the API said.

**Where this appears on screen.** Every screen in every client ends with the same three-line
block, bound to that screen's own view model:

```
<TextBlock Text="{Binding StatusMessage}" FontWeight="Bold" TextWrapping="Wrap"/>
<TextBlock Text="{Binding ResultText}" TextWrapping="Wrap"/>
<TextBlock Visibility="{Binding HasCorrelationId, Converter={StaticResource BoolToVis}}">
    <Run Text="Correlation Id: "/><Run Text="{Binding CorrelationId, Mode=OneWay}"/>
</TextBlock>
```

**The correlation ID is always visible when one exists** — bound through `HasCorrelationId`
rather than hidden behind a details toggle or omitted from the happy path, because CLAUDE.md §5
makes the correlation ID the caller's whole route to a diagnosis and a hidden one is as good as
none during a live demo. The window's own status-bar row (Grid row 2, §2 above) repeats this same
three-line block for the *last* call across any screen, so leaving a tab does not lose the most
recent outcome; each screen additionally shows it inline against that screen's own fields.

**The maintenance banner.** A second, visually distinct block sits below the status bar's
three lines, shown only while `IsInMaintenance` is true:

```
<Border Background="#FFF3CD" Padding="8" Margin="0,4,0,0"
        Visibility="{Binding IsInMaintenance, Converter={StaticResource BoolToVis}}">
    <TextBlock Text="{Binding MaintenanceMessage}" TextWrapping="Wrap"/>
</Border>
```

`#FFF3CD` (a pale amber) is the one color this document assigns meaning to across all three
clients: a write that was refused because the system is under maintenance, distinct from an
ordinary validation or business-rule refusal, which stays in the plain status-bar text with no
background color.

---

## 6. Connection and session states

- **`ConnectionText`** — always visible in the top bar, signed in or not. Spec §16 requires a
  "connection-unavailable state"; this client posture is *report, don't hide*: the connection
  text reflects the last known state rather than blocking the UI, consistent with this system
  being **online-only** (ADR and CLAUDE.md §5) — there is no offline mode to fall back into, so
  the UI's job is honest status, not degraded functionality.
- **`IsSignedIn`** gates the entire workspace: false shows only the sign-in panel (top-left,
  `MaxWidth="360"`, bordered), true shows the `TabControl` and the Sign out button. The two are
  mutually exclusive `Visibility` bindings on the same converter (`BoolToVis`/`InverseBoolToVis`)
  applied to the same property, so there is no third, partially-signed-in visual state to design
  for.
- **Loading indicators and empty states**: no dedicated spinner or empty-state control exists in
  any of the three clients as of Phase 5 — a command in flight is not visually distinguished from
  an idle screen, and an empty `DataGrid` (no search run yet, or a search with no results) renders
  as a grid with a header row and no data rows, with no "no results" message. Spec §16 lists both
  as requirements this document is not certifying as met; `plan.md` §7's Phase 7 UI refinement
  pass is where this is owed.

---

## 7. Typography and visual weight

No shared style resource exists (see the *Known gap* note above), so these are conventions
reproduced by hand in each `MainWindow.xaml`, not resource lookups:

| Use | Setting |
|---|---|
| Client title (top bar) | `FontSize="18" FontWeight="Bold"` |
| Screen/panel section heading (e.g. "Sign in", `GroupBox` headers) | `FontWeight="Bold"` (via the `GroupBox.Header` default style, or an explicit `TextBlock`) |
| Primary/destructive action on a screen (Complete sale, Close session, Create purchase order) | `FontWeight="Bold"` on the `Button` |
| Status message (first line of the three-line block, §5) | `FontWeight="Bold"` |
| Result detail, correlation ID (remaining lines of the same block) | Regular weight |
| Body text, grid contents, field labels | Regular weight, default `TextBlock`/`DataGrid` font |

Base font family and size are the WPF/Windows default (no `FontFamily` is set anywhere in any of
the three clients) — spec §16's "premium, modern, calm, and lightweight" direction is carried by
layout discipline (generous `Margin`, one clear primary action per screen, no decorative chrome)
rather than by a custom typeface, consistent with the light-first theme spec §16 calls a
starting point rather than a finished system.

---

## 8. Data presentation

- Every list of records is a `DataGrid`, `AutoGenerateColumns="False"`, `IsReadOnly="True"`,
  `SelectionMode="Single"` where a row selection drives a detail panel or a follow-on action
  (e.g. selecting a purchase order loads its lines; selecting a POS product adds it to the cart
  context). Column widths are fixed DIP values for short, bounded fields (SKU, quantity, status)
  and `Width="*"` for the one column expected to carry the longest text (name, supplier,
  message) — the same split in every grid in every client.
- Money and quantity columns bind directly to the `Decimal` properties on the response DTOs
  (never a client-side reformat or rounding) — CLAUDE.md §5's decimal-precision rule applies to
  what the client *displays* as much as to what it stores; a client that reformatted a
  `DECIMAL(19,4)` value through a `Double` on the way to the screen would defeat the rule even
  though no write occurred.
- Timestamps shown to the user (e.g. Procurement History's `CreatedAtUtc` column) are labelled
  with their zone explicitly (`AppliedTimeZone`) rather than presented as a bare value with an
  assumed zone — CLAUDE.md §5: UTC in storage, store time zone (`Asia/Manila`) on display, and
  the label makes which one is on screen unambiguous rather than implicit.

---

## 9. Conformance

`UiSpecificationDocumentationTests` (`Merchandising.Tests.Unit`, no database required — the same
posture as `ProcurementLayoutTests`/`InventoryLayoutTests`/`POSLayoutTests`) checks this document
against the running clients on every test run:

- Constructs each of the three `MainWindow` types on a dedicated STA thread and asserts their
  declared `Width`/`Height`/`MinWidth`/`MinHeight` match both each other and the table in §3.
- Asserts the 1092.8 / 576.0 DIP work-area figures this document states in §3 are the correct
  result of the `1366/1.25` and `(768-48)/1.25` arithmetic — recomputed in the test, not copied
  from the document — so the document and the arithmetic cannot silently diverge.
- Reads each client's `MainWindow.xaml` as source text and asserts the sign-in `TabIndex`
  triplet (`0`/`1`/`2`), the chrome `TabIndex="99"` on `SignOutButton`, and the status-bar
  binding markers (`StatusMessage`, `ResultText`, `HasCorrelationId`, `CorrelationId`,
  `IsInMaintenance`, `MaintenanceMessage`, and the `#FFF3CD` maintenance color) are present in
  all three files, and that §4/§5 of this document mention each convention it asserts on.
- Reads `Merchandising.ClientCommon/Api/ApiFailurePresenter.vb` as source text and asserts its
  `MaintenanceErrorCode` constant and its three literal status sentences match what §5 of this
  document quotes.

A window resized outside the §3 budget, a status-bar binding renamed, or an `ApiFailurePresenter`
sentence reworded without updating this file fails that suite rather than shipping a document
nobody re-checked.
