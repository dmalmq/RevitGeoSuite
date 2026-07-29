# RevitGeoSuite Shared UI Web Design System

## 1. Atmosphere & Identity

RevitGeoSuite web dialogs are compact production tools for GIS/Revit workflows. The signature is a quiet command surface: dense enough for repeated expert use, with teal accents reserved for action, selection, and focus.

## 2. Color

### Palette

| Role | Token | Light | Dark | Usage |
|---|---|---|---|---|
| Surface/primary | neutral-50 / neutral-950 | #fafafa | #0a0a0a | App background |
| Surface/panel | white / neutral-900 | #ffffff | #171717 | Panels and disclosures |
| Surface/muted | neutral-100 / neutral-800 | #f5f5f5 | #262626 | Secondary controls |
| Text/primary | neutral-900 / neutral-100 | #171717 | #f5f5f5 | Main copy |
| Text/secondary | neutral-600 / neutral-400 | #525252 | #a3a3a3 | Hints and labels |
| Border/default | neutral-200 / neutral-800 | #e5e5e5 | #262626 | Panels and fields |
| Accent/primary | teal-600 / teal-400 | #0d9488 | #2dd4bf | Buttons, active chips, focus |
| Status/error | red-600 / red-400 | #dc2626 | #f87171 | Validation |
| Status/warning | amber-600 / amber-400 | #d97706 | #fbbf24 | Warnings |

### Rules

- Accent color is functional only: selected state, focus, primary action, or active workflow state.
- Use neutral ramps for hierarchy before adding color.
- New semantic colors must be added here before use.

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

- Primary: system UI stack via Tailwind defaults.
- Mono: system monospace for CRS definitions, IDs, and file-like output.

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
- **States**: default neutral, active teal, disabled reduced opacity with not-allowed cursor.
- **Accessibility**: the checkbox remains the semantic control even when visually hidden.
- **Motion**: color and border transition over 120ms.

### Field

- **Structure**: label text above `input` or `select`.
- **Variants**: default, compact, disabled, error.
- **Spacing**: 8-12px within form groups.
- **States**: default, focus, disabled, error.
- **Accessibility**: visible label or adjacent field label for every input.
- **Motion**: none beyond focus color transition.

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
| Border/default | 1px solid neutral-200 / neutral-800 | Panels, fields, chips |
| Radius/field | 4px | Fields and compact controls |
| Radius/panel | 8px max | Repeated cards and panels |
| Radius/pill | 9999px | Chips and segmented pills only |

### Rules

- Do not nest decorative cards inside cards.
- Keep operational sections full-width inside the dialog rather than adding ornamental frames.
