# RevitGeoSuite Shared UI Web Design System

## 1. Atmosphere & Identity

RevitGeoSuite web dialogs are compact production tools for GIS/Revit workflows. They use Kanizu's "Framed sheet" design, so people who use both apps feel at home in each: a cool grey frame, white sheets for content, and pine green reserved for action, selection and focus.

## 2. Color

### Source of truth

The colours are Kanizu's, copied unchanged into `src/styles/kanizu-framed-sheet.css` (the header names the Kanizu commit). `src/lib/__tests__/tokens.test.ts` pins the copy with a hash, so change colours in Kanizu and copy the file again rather than editing it here. Values are HSL triplets; `app.css` maps each to a Tailwind utility (`bg-card`, `text-muted-foreground`, `border-border`, `bg-primary` ...). The `.dark` class swaps every value, so components never need a `dark:` colour.

### Palette

| Role | Utility | Light | Dark | Usage |
|---|---|---|---|---|
| Frame | `bg-background` | #E9EEEC | #121514 | App background around sheets |
| Sheet | `bg-card` | #FFFFFF | #1B1F1E | Panels, cards, fields |
| Muted surface | `bg-muted` | #F2F5F4 | #232826 | Secondary areas, table heads |
| Selected / hover | `bg-accent` | #E3EFEB | #1E3530 | Hover rows, selected chips |
| Text | `text-foreground` | #141716 | #ECF1EF | Main copy |
| Secondary text | `text-muted-foreground` | #56605C | #A3ADA9 | Hints and labels |
| Hairline | `border-border` | #DCE3E0 | #2A302E | Sheets and cards |
| Field border | `border-input` | #C9D2CE | #3A423F | Inputs and buttons |
| Pine | `bg-primary` / `text-primary` | #1D5E51 | #6CC2AC | Primary action, active state, focus ring |
| Error | `destructive` | #B42318 | #C4402E | Validation and failures |
| Warning | `warning` / `bg-warning-surface` | #E0A23A / #FCF0DC | #E9AE4E / #352812 | Warnings |
| Info | `info` | #2F6DB5 | #86B6EE | Neutral notices |

### Rules

- Pine is functional only: selected state, focus, primary action, or active workflow state.
- Use the surface tokens for hierarchy before adding colour.
- Never use raw Tailwind colour ramps (`neutral-*`, `teal-*`) or hex in components. A new semantic colour goes into Kanizu first.
- The theme follows Windows (light or dark apps setting) unless the user pins one in Settings. The WPF window frame reads the same setting so it matches before the page loads.

## 3. Typography

### Scale

| Level | Size | Weight | Line Height | Usage |
|---|---:|---:|---:|---|
| H1 | 20px | 600 | 1.35 | Dialog headings |
| H2 | 18px | 600 | 1.35 | Panel headings |
| H3 | 15px | 600 | 1.4 | Card and section headings |
| Body | 14px | 400 | 1.5 | Default UI text |
| Body/sm | 12px | 400-500 | 1.45 | Hints, chips, table cells |
| Caption | 11px | 600 | 1.3 | Metadata labels |

### Font Stack

- Primary: Yu Gothic UI for English and Japanese (Kanizu ui/tokens.rs), then Segoe UI and system fallbacks.
- Mono: Geist Mono (bundled, OFL-1.1, `src/styles/fonts/`) for numbers, coordinates, CRS codes and paths.

### Rules

- Keep operational controls compact, but do not render body text below 12px.
- Labels should be short and scannable.

## 4. Spacing & Layout

### Base Unit

All spacing follows a 4px grid.

| Token | Value | Usage |
|---|---:|---|
| --space-1 | 4px | Chip gaps, icon gaps |
| --space-2 | 8px | Compact row gaps |
| --space-3 | 12px | Field grouping |
| --space-4 | 16px | Panel padding |
| --space-6 | 24px | Major groups |

### Grid

- Dialog content uses responsive CSS grid with one column on narrow screens and two columns for forms at `md`.
- Repeated option sets wrap rather than overflow.

### Rules

- Fixed-format controls such as chips and segmented controls must not shift layout when toggled.
- Preserve existing Svelte route patterns before adding new primitives.

## 5. Components

Screens build from the kit in `src/lib/kit` (import from `$lib/kit`), not from raw styled elements: Button, Field, Input, Select, Segmented, Checkbox, Chip, Card, Table, SummaryStrip, Banner, Progress, Result, Dialog, Toaster/`toasts`, and Icon. Every status uses one of five tones (neutral, info, success, warning, danger) from `lib/kit/tone.ts`. Icons come only from the shared Lucide list in `lib/kit/icons.ts`, which uses Kanizu's names. Run `npm run dev` and open `?kit#/kit` to see every component in light and dark.

| Size | Value | Source |
|---|---:|---|
| Control height | 28px (Button md, Input, Select) | Kanizu CONTROL |
| Row height | 26px (Table, Checkbox) | Kanizu ROW |
| Type | caption 11, small 12, body 13, title 15, heading 20 (`text-caption` ...) | Kanizu ui/tokens.rs |


### Disclosure

- **Structure**: native `details` with a compact `summary` row.
- **Variants**: `advanced-section` for the outer group, `sub-disclosure` for nested option groups.
- **Spacing**: 14px outer padding, 6-12px inner gaps.
- **States**: closed, open, disabled content inside the disclosure.
- **Accessibility**: keep native `details` behavior and visible summary text.
- **Motion**: summary chevron rotates over 150ms.

### Chip Group

- **Structure**: `chip-group` wrapping `label.chip > input[type=checkbox] + text`.
- **Variants**: default, active, disabled.
- **Spacing**: 4px wrap gap, 2px by 8px chip padding.
- **States**: default muted, active pine, disabled reduced opacity with not-allowed cursor.
- **Accessibility**: the checkbox remains the semantic control even when visually hidden.
- **Motion**: color and border transition over 120ms.

### Field

- **Structure**: label text above `input` or `select`.
- **Variants**: default, compact, disabled, error.
- **Spacing**: 8-12px within form groups.
- **States**: default, focus, disabled, error.
- **Accessibility**: visible label or adjacent field label for every input.
- **Motion**: none beyond focus color transition. Keyboard focus shows a 2px pine ring on every control.

## 6. Motion & Interaction

| Type | Duration | Easing | Usage |
|---|---:|---|---|
| Micro | 120ms | ease | Chips and button state |
| Standard | 150ms | ease | Disclosure chevrons |

### Rules

- Motion is limited to state feedback.
- Animate color, opacity, and transform only.
- Disabled controls must look and behave disabled.

## 7. Depth & Surface

### Strategy

Mixed, but restrained: thin borders define tool panels, and tonal shifts define nested surfaces. Shadows are reserved for overlays and floating map details.

| Type | Value | Usage |
|---|---|---|
| Border/default | 1px solid `border-border` | Sheets, cards, chips |
| Radius/chip | 4px (`rounded-chip`) | Chips and badges |
| Radius/control | 6px (`rounded-control`) | Fields, buttons, segmented controls |
| Radius/card | 10px (`rounded-card`) | Cards and panels |
| Radius/sheet | 14px (`rounded-sheet`) | The main sheet and dialogs |

### Rules

- Do not nest decorative cards inside cards.
- Keep operational sections full-width inside the dialog rather than adding ornamental frames.
