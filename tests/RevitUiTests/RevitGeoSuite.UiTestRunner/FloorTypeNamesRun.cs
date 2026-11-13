using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// Floor type standard 2 (RevitGeoSuite #52): Set up model leaves old-named types alone, the audit
/// names each old name, and Fix floor types renames them; a shop type picks one of two shop types.
/// Run with --script floor-types on a pristine copy that was never set up.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const string Shop = "j 店舗_床";
    private const string ShopOutside = "j ラチ外店舗_床";
    private const string ShopInside = "j ラチ内店舗_床";

    // Old name, new name, unit category. The shop type has two candidates and is handled apart.
    private static readonly (string Old, string New, string Category)[] Renames =
    {
        ("smoking room", "j 喫煙所_床", "smokingarea"),
        ("Baby room", "j ベビー休憩室_床", "mothersroom"),
        ("Roof", "j 屋外_床", "unenclosedarea"),
    };

    private List<FloorTypeRow> typesBefore = new();

    // An old shop type that already carries rachi_gai or rachi_nai is only offered its own side's
    // name; one without a restriction is offered both. Null: both sides.
    private string? shopSide;
    private int floorTypesApplied;

    private async Task FloorTypeSteps()
    {
        await Step("F1", "Set up model goes to standard 2 and leaves old-named types alone", FloorTypeSetup);
        await Step("F2", "The audit names each old floor type name", OldNameWarnings);
        await Step("F3", "Fix floor types offers the renames; a shop type takes one of two", FloorTypeProposals);
        await Step("F4", "The renamed types keep their ids and floors", RenamedTypes);
        await Step("F5", "Check again: no old names left; Set up offers the inside shop type", AfterRenames);
        await Step("F6", "Export: shop units are rachi_gai, roof units unenclosedarea", ExportCategories);
        await Step("F7", "pyRevit Fix floor types on a second copy", step =>
        {
            step.NotRun("Manual: the pyRevit tab's dialog is not driven by the runner yet.");
            return Task.CompletedTask;
        });
    }

    private async Task FloorTypeSetup(StepResult step)
    {
        typesBefore = FloorTypes();
        List<string> inUse = OldNamesInUse(typesBefore);
        FloorTypeRow? shopType = typesBefore.FirstOrDefault(type => type.Name == Shop && type.Floors > 0);
        string? restriction = shopType == null ? null : TypeParameter(shopType.Id, "gis_restriction");
        shopSide = restriction switch { "rachi_gai" => ShopOutside, "rachi_nai" => ShopInside, _ => null };
        if (shopType != null)
        {
            step.Note($"{Shop} has gis_restriction '{restriction ?? "(none)"}', so it is offered {shopSide ?? $"{ShopOutside} or {ShopInside}"}.");
        }

        step.Note($"Old-named types in use: {(inUse.Count == 0 ? "none" : string.Join(", ", inUse))}.");
        step.Check(inUse.Count > 0, "The model has at least one old-named floor type in use (otherwise this script checks nothing).");

        InspectReply before = await Inspect();
        if (before.SetupLines.Length == 0)
        {
            step.Check(false, "Set up model has nothing to do: use a pristine copy that was never set up.");
            return;
        }

        string[] blocked = BlockedNewNames(inUse, shopSide);
        foreach (string name in blocked)
        {
            step.Check(!before.SetupLines.Any(line => line.Contains(name, StringComparison.Ordinal)), $"Set up model does not plan to create {name} while its old-named type is in use.");
        }

        int seq = host.LastEvent();
        await Shell.Button($"Apply {before.SetupLines.Length} change(s)").ClickAsync();
        step.Check(await Shell.ShowsAsync(Shell.Text("Set up model applied"), 120000), "Banner 'Set up model applied'.");
        ExpectEvent(step, seq, "commit", SetupTransaction);
        InspectReply after = await Inspect();
        step.Check(after.StandardVersion == 2 && after.ModelVersion == "2", $"The model is on standard version 2 (model '{after.ModelVersion}', standard {after.StandardVersion}; was '{before.ModelVersion}').");

        List<FloorTypeRow> types = FloorTypes();
        foreach (string name in blocked)
        {
            step.Check(types.All(type => type.Name != name), $"{name} was not created.");
        }

        FloorTypeRow? other = types.FirstOrDefault(type => type.Name == "j その他_床");
        if (step.Check(other != null, "j その他_床 exists after Set up model."))
        {
            string? category = TypeParameter(other!.Id, "gis_category");
            string? otherRestriction = TypeParameter(other.Id, "gis_restriction");
            step.Check(category == "nonpublic", $"j その他_床 has gis_category nonpublic (has '{category ?? "(no parameter)"}').");
            step.Check(string.IsNullOrEmpty(otherRestriction), $"j その他_床 keeps an empty gis_restriction (has '{otherRestriction}').");
        }
    }

    private async Task OldNameWarnings(StepResult step)
    {
        await CheckAgain();
        InspectReply model = await Inspect();
        List<FindingItem> oldName = model.Findings.Where(finding => finding.Message.Contains("uses an old name", StringComparison.Ordinal)).ToList();
        List<string> inUse = OldNamesInUse(typesBefore);
        step.Check(oldName.Count == inUse.Count, $"One old-name warning per old-named type in use ({oldName.Count} warnings, {inUse.Count} types).");
        foreach ((string old, string renamed, _) in Renames.Where(rename => inUse.Contains(rename.Old)))
        {
            step.Check(
                oldName.Any(finding => finding.Severity == "warning" && finding.Message.Contains(old, StringComparison.Ordinal) && finding.Message.Contains($"rename it to {renamed}", StringComparison.Ordinal)),
                $"A warning says {old} uses an old name and to rename it to {renamed}.");
        }

        if (inUse.Contains(Shop))
        {
            FindingItem? shop = oldName.FirstOrDefault(finding => finding.Message.Contains(Shop, StringComparison.Ordinal));
            if (shopSide == null)
            {
                step.Check(
                    shop != null && shop.Message.Contains($"rename it to {ShopOutside} or {ShopInside}", StringComparison.Ordinal)
                        && Regex.IsMatch(shop.Message, @"All \d+ floors of this type"),
                    $"The {Shop} warning names both shop types and says all its floors get the name chosen (says: {shop?.Message ?? "nothing"}).");
            }
            else
            {
                step.Check(shop != null && shop.Message.Contains($"rename it to {shopSide}", StringComparison.Ordinal), $"The {Shop} warning names its own side, {shopSide} (says: {shop?.Message ?? "nothing"}).");
            }
        }

        foreach (FindingItem finding in oldName)
        {
            step.Note(finding.Message);
        }
    }

    private async Task FloorTypeProposals(StepResult step)
    {
        InspectReply model = await Inspect();
        List<string> inUse = OldNamesInUse(typesBefore);
        foreach ((string old, string renamed, string category) in Renames.Where(rename => inUse.Contains(rename.Old)))
        {
            ProposalItem? proposal = model.Proposals.FirstOrDefault(item => item.FloorTypeName == old);
            step.Check(
                proposal != null && proposal.Reason == "oldName" && proposal.NewName == renamed && proposal.GisCategory == category,
                $"{old} is proposed as an old name that renames to {renamed} ({category}); got {Describe(proposal)}.");
            if (proposal != null)
            {
                step.Check(await ProposalCheckbox(old, proposal.StandardTypeName).IsCheckedAsync(), $"{old} starts ticked (one candidate).");
            }
        }

        step.Check(await Shell.ShowsAsync(Shell.Text("old name")), "Rows carry the 'old name' chip.");
        step.Check(await Shell.ShowsAsync(Shell.Text(new Regex("^renames to "))), "Rows say 'renames to …'.");

        if (inUse.Contains(Shop))
        {
            List<ProposalItem> shop = model.Proposals.Where(item => item.FloorTypeName == Shop).ToList();
            ProposalItem? outside = shop.FirstOrDefault(item => item.StandardTypeName == ShopOutside);
            ProposalItem? inside = shop.FirstOrDefault(item => item.StandardTypeName == ShopInside);
            if (shopSide != null)
            {
                step.Check(shop.Count == 1 && shop[0].StandardTypeName == shopSide, $"{Shop} has one row, {shopSide} (has {string.Join(", ", shop.Select(item => item.StandardTypeName))}).");
                if (shop.Count == 1)
                {
                    step.Check(await ProposalCheckbox(Shop, shopSide).IsCheckedAsync(), $"The single {Shop} row starts ticked.");
                }
            }
            else if (step.Check(shop.Count == 2 && outside != null && inside != null, $"{Shop} has two rows, {ShopOutside} and {ShopInside} (has {shop.Count})."))
            {
                ILocator outsideBox = ProposalCheckbox(Shop, ShopOutside);
                ILocator insideBox = ProposalCheckbox(Shop, ShopInside);
                step.Check(!await outsideBox.IsCheckedAsync() && !await insideBox.IsCheckedAsync(), "Neither shop row starts ticked.");
                await insideBox.CheckAsync();
                await outsideBox.CheckAsync();
                step.Check(await outsideBox.IsCheckedAsync() && !await insideBox.IsCheckedAsync(), "Ticking one shop row unticks the other.");
                step.Check(await Shell.ShowsAsync(Shell.Text(new Regex(@"All \d+ floors of this type become"))), "The shop rows say all floors of the type follow the choice.");
            }
        }

        await Shot(step, "F3-fix-floor-types");
        // Single-candidate types start ticked; a two-candidate shop type counts once it is ticked above.
        int types = model.Proposals.GroupBy(item => item.FloorTypeId).Count(group => group.Count() == 1) + (inUse.Contains(Shop) && shopSide == null ? 1 : 0);
        int seq = host.LastEvent();
        await Shell.Button($"Apply to {types} type(s)").ClickAsync();
        step.Check(await Shell.ShowsAsync(Shell.Text($"{types} floor type(s) fixed"), 120000), $"Banner '{types} floor type(s) fixed'.");
        ExpectEvent(step, seq, "commit", FixTransaction);
        floorTypesApplied = types;

        ILocator changes = Shell.Text(new Regex(@"^\d+ change\(s\)$"));
        if (await changes.CountAsync() > 0)
        {
            await changes.First.ClickAsync();
        }

        string banner = string.Join("\n", await Shell.Page.GetByRole(AriaRole.Status).AllInnerTextsAsync());
        foreach ((string old, string renamed, _) in Renames.Where(rename => inUse.Contains(rename.Old)))
        {
            step.Check(banner.Contains($"Renamed {old} to {renamed}", StringComparison.Ordinal), $"The change list says 'Renamed {old} to {renamed}'.");
        }

        if (inUse.Contains(Shop))
        {
            string target = shopSide ?? ShopOutside;
            step.Check(banner.Contains($"Renamed {Shop} to {target}", StringComparison.Ordinal), $"The change list says 'Renamed {Shop} to {target}'.");
        }

        await Shot(step, "F3-applied");
    }

    private Task RenamedTypes(StepResult step)
    {
        if (floorTypesApplied == 0)
        {
            step.NotRun("Fix floor types was not applied.");
            return Task.CompletedTask;
        }

        Dictionary<long, FloorTypeRow> after = FloorTypes().ToDictionary(type => type.Id);
        IEnumerable<(string Old, string New)> expected = Renames.Select(rename => (rename.Old, rename.New)).Append((Shop, shopSide ?? ShopOutside));
        foreach ((string old, string renamed) in expected)
        {
            FloorTypeRow? before = typesBefore.FirstOrDefault(type => type.Name == old && type.Floors > 0);
            if (before == null)
            {
                continue;
            }

            FloorTypeRow? now = after.GetValueOrDefault(before.Id);
            step.Check(now?.Name == renamed, $"Type {before.Id} '{old}' is now named '{renamed}' in Revit (is '{now?.Name ?? "gone"}').");
            step.Check(now?.Floors == before.Floors, $"Type {before.Id} keeps its {before.Floors} floor(s) (has {now?.Floors ?? 0}).");
        }

        return Task.CompletedTask;
    }

    private async Task AfterRenames(StepResult step)
    {
        await CheckAgain();
        InspectReply model = await Inspect();
        List<FindingItem> oldName = model.Findings.Where(finding => finding.Message.Contains("uses an old name", StringComparison.Ordinal)).ToList();
        step.Check(oldName.Count == 0, $"No old-name warnings are left ({string.Join(" / ", oldName.Select(finding => finding.Message))}).");
        HashSet<long> renamedIds = typesBefore.Where(type => type.Floors > 0 && (type.Name == Shop || Renames.Any(rename => rename.Old == type.Name))).Select(type => type.Id).ToHashSet();
        step.Check(!model.Proposals.Any(proposal => renamedIds.Contains(proposal.FloorTypeId)), "The card proposes nothing more for the renamed types.");
        // Only a shop type offered both sides holds back both names; after it becomes the outside
        // type, Set up model may create the inside one.
        if (OldNamesInUse(typesBefore).Contains(Shop) && shopSide == null)
        {
            string otherSide = ShopInside;
            step.Check(model.SetupLines.Any(line => line.Contains(otherSide, StringComparison.Ordinal)), $"Set up model now offers to create {otherSide} (lines: {string.Join(" / ", model.SetupLines)}).");
        }
    }

    private async Task ExportCategories(StepResult step)
    {
        string output = await OpenExport(step, options.ExportView, "export-floor-types");
        ILocator export = Shell.Button(new Regex(@"^Export (1 view|\d+ views)$"));
        if (!step.Check(await Shell.ShowsAsync(export, 600000), "The preview loads and offers to export."))
        {
            return;
        }

        await export.ClickAsync();
        ILocator dialog = Shell.Page.GetByRole(AriaRole.Dialog);
        if (await Shell.ShowsAsync(dialog.GetByRole(AriaRole.Button, new() { Name = "Export anyway", Exact = true }), 5000))
        {
            step.Note("The audit has errors; exported anyway.");
            await dialog.GetByRole(AriaRole.Checkbox).CheckAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Export anyway", Exact = true }).ClickAsync();
        }

        if (!await FinishExport(step))
        {
            return;
        }

        await Shot(step, "F6-export-done");
        UnitCounts units = GeoPackageUnits.Count(output);
        step.Note($"Unit tables: {string.Join(", ", units.Tables)}. Categories: {Format(units.Categories)}. Restrictions: {Format(units.Restrictions)}.");
        List<string> inUse = OldNamesInUse(typesBefore);
        if (inUse.Contains(Shop))
        {
            step.Check(units.Restriction("rachi_gai") > 0, $"Shop units carry restriction rachi_gai ({units.Restriction("rachi_gai")} unit(s)).");
        }

        if (inUse.Contains("Roof"))
        {
            step.Check(units.Category("unenclosedarea") > 0, $"Roof units carry category unenclosedarea ({units.Category("unenclosedarea")} unit(s)).");
        }

        await Shell.GoToAsync("/gis-standard");
    }

    private List<FloorTypeRow> FloorTypes() =>
        host.Call("floorTypes")["floorTypes"]!.AsArray()
            .Select(node => new FloorTypeRow(node!["id"]!.GetValue<long>(), node["name"]!.GetValue<string>(), node["floors"]!.GetValue<int>()))
            .ToList();

    private static List<string> OldNamesInUse(List<FloorTypeRow> types) =>
        Renames.Select(rename => rename.Old).Append(Shop).Where(old => types.Any(type => type.Name == old && type.Floors > 0)).ToList();

    private static string[] BlockedNewNames(List<string> inUse, string? shopSide) =>
        Renames.Where(rename => inUse.Contains(rename.Old)).Select(rename => rename.New)
            .Concat(!inUse.Contains(Shop) ? Array.Empty<string>() : shopSide != null ? new[] { shopSide } : new[] { ShopOutside, ShopInside })
            .ToArray();

    private string? TypeParameter(long typeId, string name)
    {
        JsonObject elements = host.Call("params", new { ids = new[] { typeId }, names = new[] { name } })["elements"]!.AsObject();
        return elements[typeId.ToString()]?[name]?.GetValue<string>();
    }

    private static string Describe(ProposalItem? proposal) =>
        proposal == null ? "no proposal" : $"reason '{proposal.Reason}', renames to '{proposal.NewName}', category '{proposal.GisCategory}'";

    private static string Format(IReadOnlyDictionary<string, int> counts) =>
        string.Join(", ", counts.OrderByDescending(pair => pair.Value).Select(pair => $"{(pair.Key.Length == 0 ? "(empty)" : pair.Key)} {pair.Value}"));
}

internal sealed record FloorTypeRow(long Id, string Name, int Floors);
