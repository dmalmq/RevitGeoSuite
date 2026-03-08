# UI Flow

## Primary Workflow

The first module guides the user through a step-by-step georeferencing process while fitting into a broader suite architecture.

## Entry Point

User clicks a ribbon button such as:

- `Georeference Setup`
- `Project Coordinate Setup`
- similar final name TBD

This button is hosted inside the suite shell, not as a standalone independent add-in.

## Step 1 - Current State

Show:

- current project coordinate-related status summary
- whether prior geo setup data exists
- warnings if existing setup is detected
- confidence/source summary if setup already exists
- clear note that V1 works with project location and true north, not building geometry rotation

User chooses to:

- continue setup
- cancel

## Step 2 - Choose CRS

Show:

- searchable CRS picker
- recommended Japanese CRS values
- descriptive details for selected CRS

User action:

- pick CRS
- continue

## Step 3 - Open Map View

Show:

- OSM map
- selected CRS summary
- current cursor coordinates
- search box if available

User action:

- pan/zoom to site
- click target point

## Step 4 - Review Selected Point

Show:

- latitude/longitude
- projected coordinates in chosen CRS
- optional label/name for the point
- source note such as `selected from OSM map`
- confidence guidance when the point is based only on map context

User action:

- accept point
- re-pick point

## Step 5 - Choose Setup Intent

Possible actions:

- save canonical geo metadata only
- prepare project location update from the selected point
- prepare project location update plus true north angle adjustment

The UI should explain each option in plain language.

The UI should also state explicitly that V1:

- does not rotate building geometry
- does not expose arbitrary direct base point editing as a separate operation
- uses Revit's project location workflow for the apply step

## Step 6 - Preview Changes

Show:

- current known values
- proposed values
- selected CRS
- selected point
- warnings or validation issues
- confidence/source note
- what will be persisted to shared project metadata
- exact statement of what the apply step will change

User action:

- apply
- go back
- cancel

For milestone B internal builds, this screen may be the end of the workflow and show that apply is not enabled yet.

## Step 7 - Apply Changes

Show:

- confirmation prompt before commit
- progress indication
- success or failure result
- summary of what changed
- confirmation that shared geo metadata was saved
- audit/log saved confirmation

## Step 8 - Completion

Offer:

- save summary
- copy summary to clipboard
- close
- start another setup action

## UX Rules

- Avoid technical overload on the first screen
- Default to more explanation for beginner users
- Use consistent terminology throughout
- Never hide high-impact actions
- Make preview mandatory before any change is committed
- Make provenance and confidence visible when helpful
- Keep shared UI controls reusable and presentation-focused
