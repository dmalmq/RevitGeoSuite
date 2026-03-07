# UI Flow

## Primary Workflow

The first module should guide the user through a step-by-step georeferencing process while fitting into a broader suite architecture.

## Entry Point

User clicks a ribbon button such as:

- `Georeference Setup`
- `Project Coordinate Setup`
- similar final name TBD

This button is hosted inside the suite shell, not as a standalone independent add-in.

## Step 1 — Welcome / Current State

Show:

- current project coordinate-related status summary
- whether prior geo setup data exists
- warnings if existing setup is detected
- confidence/source summary if setup already exists

User chooses to:

- start new setup
- review existing setup
- cancel

## Step 2 — Choose CRS

Show:

- searchable CRS picker
- recent or recommended Japanese CRS values
- descriptive details for selected CRS

User action:

- pick CRS
- continue

## Step 3 — Open Map View

Show:

- OSM map
- selected CRS summary
- current cursor coordinates
- search box if available

User action:

- pan/zoom to site
- click target point

## Step 4 — Review Selected Point

Show:

- latitude/longitude
- projected coordinates in chosen CRS
- optional label/name for the point
- source note such as “selected from OSM map”
- confidence guidance if the point is based only on map context

User action:

- accept point
- re-pick point

## Step 5 — Choose Placement Operation

Possible actions:

- prepare survey point related setup
- prepare project base point related setup
- prepare orientation/rotation adjustment
- save canonical geo setup without additional operations yet

The UI should explain each option in plain language.

## Step 6 — Preview Changes

Show:

- current known values
- proposed values
- selected CRS
- selected point
- warnings or validation issues
- confidence/source note
- what will be persisted to shared project metadata

User action:

- apply
- go back
- cancel

## Step 7 — Apply Changes

Show:

- progress indication
- success or failure result
- summary of what changed
- confirmation that shared geo metadata was saved
- audit/log saved confirmation

## Step 8 — Completion

Offer:

- save summary
- copy summary to clipboard
- close
- start another setup action

## UX Rules

- Avoid technical overload on the first screen
- Provide “more details” for advanced users
- Use consistent terminology throughout
- Never hide high-impact actions
- Make preview mandatory before any change is committed
- Make provenance/confidence visible when helpful
- Keep shared UI controls reusable and presentation-focused
