<script lang="ts">
  // Dev-only page (vite dev, #/kit): every kit component in each state, for review and captures.
  import {
    Banner, Button, Card, Checkbox, Chip, Dialog, Field, Icon, Input, Progress, Result, Segmented,
    Select, SummaryStrip, Table, Toaster, ICONS, toasts, type IconName
  } from '$lib/kit'

  let format = $state<'gpkg' | 'shp' | 'dxf'>('gpkg')
  let crs = $state('6677')
  let epsg = $state('99999')
  let checked = $state(true)
  let features = $state({ unit: true, opening: false, level: true })
  let dialogOpen = $state(false)
  const iconNames = Object.keys(ICONS) as IconName[]
</script>

<div class="h-full overflow-auto bg-background p-6 text-foreground">
  <div class="mx-auto flex max-w-[960px] flex-col gap-4">
    <h1 class="text-heading font-semibold">Component kit</h1>

    <Card title="Buttons" description="primary, secondary, ghost, danger; sm, md, lg; icon, busy, disabled">
      <div class="flex flex-wrap items-center gap-2">
        <Button variant="primary" iconAfter="arrow-right">Continue</Button>
        <Button icon="folder-open">Browse...</Button>
        <Button variant="ghost">Cancel</Button>
        <Button variant="danger" icon="trash-2">Delete</Button>
        <Button size="sm">Select all</Button>
        <Button size="lg" variant="primary">Export</Button>
        <Button busy variant="primary">Exporting</Button>
        <Button disabled>Disabled</Button>
        <Button icon="settings" aria-label="Settings" />
      </div>
    </Card>

    <div class="grid gap-4 md:grid-cols-2">
      <Card title="Fields">
        <div class="flex flex-col gap-3">
          <Field label="Coordinate system" id="kit-crs" hint="Japan Plane Rectangular CS IX">
            <Input id="kit-crs" mono bind:value={crs} />
          </Field>
          <Field label="Target EPSG" id="kit-epsg" error="Unknown EPSG code" required>
            <Input id="kit-epsg" mono invalid bind:value={epsg} aria-describedby="kit-epsg-error" />
          </Field>
          <Field label="Packaging" id="kit-pack">
            <Select
              id="kit-pack"
              value="perView"
              options={[
                { value: 'perFeature', label: 'Per view / feature files' },
                { value: 'perView', label: 'Per view GeoPackage' }
              ]}
            />
          </Field>
          <Field label="Output format">
            <Segmented
              label="Output format"
              bind:value={format}
              options={[
                { value: 'gpkg', label: 'GeoPackage' },
                { value: 'shp', label: 'Shapefile' },
                { value: 'dxf', label: 'DXF', disabled: true }
              ]}
            />
          </Field>
        </div>
      </Card>

      <Card title="Choices">
        <Checkbox bind:checked label="Validate after write" hint="Runs the schema checks on the written file." />
        <Checkbox label="Launch QGIS" />
        <Checkbox disabled label="Keep temporary 3D views" />
        <div class="mt-3 flex flex-wrap gap-1">
          <Chip selected={features.unit} onclick={() => (features.unit = !features.unit)}>unit</Chip>
          <Chip selected={features.opening} onclick={() => (features.opening = !features.opening)}>opening</Chip>
          <Chip selected={features.level} onclick={() => (features.level = !features.level)}>level</Chip>
        </div>
        <div class="mt-3 flex flex-wrap gap-1">
          <Chip>neutral</Chip>
          <Chip tone="info">info</Chip>
          <Chip tone="success" icon="check">ready</Chip>
          <Chip tone="warning">3 warnings</Chip>
          <Chip tone="danger">failed</Chip>
        </div>
      </Card>
    </div>

    <SummaryStrip
      items={[
        { label: 'Views', value: 4 },
        { label: 'Features', value: 1284 },
        { label: 'Warnings', value: 3, tone: 'warning' },
        { label: 'CRS', value: 'EPSG:6677', mono: true }
      ]}
    />

    <div class="flex flex-col gap-2">
      <Banner tone="info" title="Existing coordinate setup detected" onclose={() => {}}>
        The Survey Point and Project Base Point both have readable shared coordinates.
      </Banner>
      <Banner tone="success" title="Georeference saved" />
      <Banner tone="warning" title="True north unset">
        Angle to true north is 0.
        {#snippet actions()}<Button size="sm">Set it</Button>{/snippet}
      </Banner>
      <Banner tone="danger" title="Export failed">The output folder is read-only.</Banner>
    </div>

    <Card title="Progress">
      <div class="flex flex-col gap-3">
        <Progress value={0.42} label="Writing features" detail="540 / 1284" />
        <Progress value={null} label="Scanning PLATEAU folder" />
        <Progress value={0.8} tone="danger" label="Stopped" />
      </div>
    </Card>

    <div class="grid gap-4 md:grid-cols-2">
      <Result title="Export finished">
        4 views written to C:\Exports\TokyoStation.
        {#snippet actions()}
          <Button icon="folder-open">Open folder</Button>
          <Button variant="primary">Done</Button>
        {/snippet}
      </Result>
      <Result tone="danger" title="Export failed">The output folder is read-only.</Result>
    </div>

    <Table maxHeight="180px">
      <thead><tr><th>Floor type</th><th>Category</th><th class="num">Count</th></tr></thead>
      <tbody>
        <tr><td>Concourse_Finish</td><td>walkway</td><td class="num">128</td></tr>
        <tr aria-selected="true"><td>Retail_Floor</td><td>retail</td><td class="num">42</td></tr>
        <tr><td>Platform_Edge</td><td>platform</td><td class="num">8</td></tr>
      </tbody>
    </Table>

    <Card title="Dialog and toasts">
      <div class="flex flex-wrap gap-2">
        <Button onclick={() => (dialogOpen = true)}>Open dialog</Button>
        <Button onclick={() => toasts.show({ tone: 'success', title: 'Profile saved' })}>Toast</Button>
        <Button onclick={() => toasts.show({ tone: 'danger', title: 'Export failed', message: 'The output folder is read-only.' })}>Error toast</Button>
      </div>
    </Card>

    <Card title="Shared icons ({iconNames.length})">
      <div class="grid grid-cols-[repeat(auto-fill,minmax(120px,1fr))] gap-2">
        {#each iconNames as name (name)}
          <div class="flex items-center gap-2 text-small text-muted-foreground">
            <Icon {name} class="text-foreground" />{name}
          </div>
        {/each}
      </div>
    </Card>
  </div>
</div>

<Dialog bind:open={dialogOpen} title="Delete profile?" description="Station default will be removed for everyone on this PC." size="sm">
  This cannot be undone.
  {#snippet footer()}
    <Button variant="ghost" onclick={() => (dialogOpen = false)}>Cancel</Button>
    <Button variant="danger" onclick={() => (dialogOpen = false)}>Delete</Button>
  {/snippet}
</Dialog>
<Toaster />
