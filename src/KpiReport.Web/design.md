# Design — HR KPI Monitoring System (KpiReport.Web)

A locked design system for this internal ASP.NET MVC app. Every future page
redesign or new view should read this file before touching CSS. Extend or
amend it when the system needs to grow — don't invent a parallel theme.

## Genre
modern-minimal (internal enterprise tool — dashboard + admin, not marketing)

## Macrostructure family
- **App pages** (Dashboard, Users, ReportSubscriptions): Workbench — page-head
  toolbar, stat row, card/table grid. Data density over decoration.
- **Content pages** (About, Contact): Letter — narrow left-biased intro
  panel + an unequal vertical point list. No centered hero, no 3-equal-column
  feature grid. (Home/Index was removed — root route defaults straight to
  Dashboard/Index; `.kpi-intro`/`.kpi-point-list` in `kpi-theme.css` are kept
  for reuse on About/Contact if they adopt the same pattern.)
- **Auth pages** (Login, Register, Manage/*, ResetPassword): Letter, narrow —
  the existing `.kpi-auth` centered card pattern, kept as-is (appropriate for
  a single-task form, not a slop tell here).

## Theme
Custom OKLCH palette, cool-blue anchor hue 258 (the brief's blue/white brand).
Values live in `Content/tokens.css` — this is the single edit point for colour.

- `--color-paper`   oklch(97.5% 0.006 258)
- `--color-surface` oklch(99% 0.003 258)
- `--color-ink`     oklch(22% 0.020 258)
- `--color-ink-2`   oklch(46% 0.020 258)
- `--color-rule`    oklch(87% 0.012 258)
- `--color-navy`    oklch(26% 0.050 258)   — chrome (navbar, table head)
- `--color-accent`  oklch(48% 0.135 255)   — links, primary actions, focus
- `--color-focus`   oklch(58% 0.160 255)
- status: `--color-success` (150°) / `--color-warning` (75°) / `--color-danger` (25°) —
  semantic, kept distinct from the blue anchor on purpose (data meaning, not brand)

## Typography
Offline-safe pairing — no CDN font request, so the intranet never shows a
tofu/fallback flash and nothing breaks if the box has no internet route.

- Display: `ui-serif, Cambria, Georgia, serif` (resolves to the OS's real serif)
- Body: `"Segoe UI", "Sarabun", system-ui, sans-serif` (existing stack, kept)
- Mono: `ui-monospace, Consolas, monospace` — used for KPI figures, codes,
  formulas (tabular-nums, so columns of numbers align)
- Scale anchor: `--text-display: clamp(2rem, 2.4vw + 1.2rem, 2.75rem)` — modest
  on purpose; this is a utilitarian tool, not a marketing hero.

## Spacing
4-point named scale in `tokens.css` (`--space-3xs` … `--space-3xl`). Pages
must use the named tokens, never raw px.

## Motion
- Easings: `--ease-out` `cubic-bezier(0.16,1,0.3,1)`, `--ease-in`, `--ease-in-out`
- Only `background-color`, `border-color`, `box-shadow`, `width`, `color`
  transition — never layout-triggering properties on hover.
- `prefers-reduced-motion: reduce` turns all listed transitions off.
- No entrance animation on page load; no scroll-triggered reveals — this is a
  dashboard people check daily, not a page that needs to perform itself.

## Microinteractions stance
- Silent success (data just updates; no toast for expected outcomes).
- Every interactive element (button, input, nav link) has `:focus-visible`
  with an instant (non-transitioned) 2px ring at `--color-focus`.
- Row actions (edit/reset/disable) are inline, not hover-only — a11y requirement
  since these are used with keyboard and by admins scanning tables.

## CTA voice
- Primary: filled `--color-accent`, `.btn-kpi`, `border-radius: --radius-sm`.
- Secondary: outline `--color-accent-line` border, `.btn-kpi-outline`.
- Table-row actions: `.btn-kpi-sm`, same voice at a smaller scale — never a
  bare icon-only button; row actions always carry a text label.

## What pages MUST share
- Navy navbar / table-head chrome, single accent blue for links + focus + CTA.
- The `.kpi-page-head` / `.kpi-panel` / `.kpi-card` / `.kpi-table-wrap`
  container language — hairline border + `--shadow-whisper`, never a
  thick coloured side-stripe (that pattern was removed from `.kpi-page-head`
  in this redesign; `.kpi-card`'s top-line status colour is a kept exception
  because it's a real data signal, not decoration).
- Mono figures for numbers (`kpi-actual`, `kpi-stat-value`, `kpi-code`).

## What pages MAY differ on
- Workbench vs. Letter macrostructure, per the family above.
- Card/grid density on app pages (Dashboard is denser than Users/ReportSubscriptions).

## Known follow-ups (not done in this pass)
- Auth views (`Views/Account/*`, `Views/Manage/*`) still use `.kpi-auth` /
  `.kpi-panel` — those classes now pull from `tokens.css` automatically, but
  their markup wasn't restructured; they didn't carry a slop tell worth
  rewriting (a centered single-task form is the right shape for a login page).
- `Views/Users/*` and `Views/ReportSubscriptions/*` tables inherit the new
  `.kpi-table` / `.kpi-page-head` tokens automatically — not individually
  audited for markup-level tells in this pass.

## Exports

### tokens.css
See `Content/tokens.css` — the canonical file the app actually loads
(via `App_Start/BundleConfig.cs`). Reproduced here for portability:

```css
:root {
  --color-paper:   oklch(97.5% 0.006 258);
  --color-surface: oklch(99% 0.003 258);
  --color-ink:     oklch(22% 0.020 258);
  --color-accent:  oklch(48% 0.135 255);
  --color-focus:   oklch(58% 0.160 255);

  --font-display: ui-serif, Cambria, Georgia, serif;
  --font-body:    "Segoe UI", "Sarabun", system-ui, sans-serif;
  --font-mono:    ui-monospace, Consolas, monospace;

  --space-md: 1rem; --space-lg: 1.5rem; --space-xl: 2.5rem;
  --text-md: 1.25rem; --text-display: clamp(2rem, 2.4vw + 1.2rem, 2.75rem);

  --ease-out: cubic-bezier(0.16, 1, 0.3, 1);
  --dur-short: 150ms;
  --radius-sm: 4px; --radius-md: 8px; --radius-pill: 999px;
}
```
