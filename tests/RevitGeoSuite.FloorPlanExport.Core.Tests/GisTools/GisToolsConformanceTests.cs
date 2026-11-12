using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;
using static RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools.GisToolsFixtures;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

/// <summary>
/// The shared cases in tests/Fixtures/Conformance/gis-tools/cases.json. The pyRevit extension's
/// test_conformance.py reads the same file, so until the extension is retired a rule changed in
/// one implementation and not the other fails a test.
/// </summary>
public sealed class GisToolsConformanceTests
{
    private static readonly JObject Cases = LoadCases();

    [Fact]
    public void Audit()
    {
        foreach (JObject testCase in Cases["audit"]!.Cast<JObject>())
        {
            IReadOnlyList<GisAuditFinding> findings = GisAudit.Run(BuildModel((JObject)testCase["model"]!), Standard);

            Assert.True(
                Format(testCase["expected"]!.Select(entry => $"{entry[0]}|{entry[1]}|{string.Join(",", entry[2]!.Values<long>())}")) ==
                Format(findings.Select(finding => $"{SeverityText(finding.Severity)}|{finding.Topic}|{string.Join(",", finding.ElementIds)}")),
                $"{testCase["name"]}: expected {Format(testCase["expected"]!.Select(entry => entry.ToString(Newtonsoft.Json.Formatting.None)))}, got {Format(findings.Select(finding => $"{SeverityText(finding.Severity)} {finding.Topic} [{string.Join(",", finding.ElementIds)}]"))}");
        }
    }

    [Fact]
    public void FloorTypeMatch()
    {
        foreach (JObject testCase in Cases["floor_type_match"]!.Cast<JObject>())
        {
            string name = (string)testCase["name"]!;
            FloorTypeProposal? proposal = FloorTypeMatcher.ProposeOne(new FloorTypeInfo(900, name, floorIds: new long[] { 1, 2, 3 }), Standard);
            string? expected = testCase["expected"]!.Type == JTokenType.Null ? null : string.Join("|", testCase["expected"]!.Values<string>());
            string? actual = proposal is null ? null : string.Join("|", proposal.StandardType.Name, proposal.GisRestriction, proposal.Reason);

            Assert.True(expected == actual, $"{name}: expected {expected ?? "no match"}, got {actual ?? "no match"}");
        }
    }

    [Fact]
    public void SetupPlan()
    {
        foreach (JObject testCase in Cases["setup_plan"]!.Cast<JObject>())
        {
            IReadOnlyList<string> lines = SetupPlanner.Describe(SetupPlanner.Plan(BuildModel((JObject)testCase["model"]!), Standard));

            Assert.Equal(testCase["expected_lines"]!.Values<string>().ToList(), lines.ToList());
        }
    }

    [Fact]
    public void WholeNumber()
    {
        foreach (JObject testCase in Cases["whole_number"]!.Cast<JObject>())
        {
            Assert.True(
                (int?)testCase["expected"] == GisToolStandard.ParseWholeNumber((string)testCase["text"]!),
                $"'{testCase["text"]}': expected {testCase["expected"]}");
        }
    }

    [Fact]
    public void DriftPlan()
    {
        foreach (JObject testCase in Cases["drift_plan"]!.Cast<JObject>())
        {
            Dictionary<(string, string, string), string> values = testCase["model_values"]!
                .ToDictionary(entry => ((string)entry[0]!, (string)entry[1]!, (string)entry[2]!), entry => (string)entry[3]!);

            var (changes, problems) = GisDrift.Plan(
                GisDrift.Parse(testCase["drift"]!.ToString()),
                (uniqueId, parameter, binding) => values.TryGetValue((uniqueId, parameter, binding), out string? value) ? value : null,
                Standard);

            Assert.Equal(
                testCase["expected_changes"]!.Select(entry => string.Join("|", entry.Take(5).Values<string>()) + "|" + ((bool)entry[5]! ? "conflict" : "-")).ToList(),
                changes.Select(change => string.Join("|", change.UniqueId, change.Parameter, change.Binding, change.OldValue, change.NewValue) + "|" + (change.Conflict ? "conflict" : "-")).ToList());
            Assert.Equal((int)testCase["expected_problem_count"]!, problems.Count);
        }
    }

    private static ModelSnapshot BuildModel(JObject spec)
    {
        string? standardVersion = (string?)spec["standard_version"];
        if ((string?)spec["base"] == "empty")
        {
            return new ModelSnapshot(standardVersion: standardVersion ?? string.Empty);
        }

        string[] skip = spec["bound_skip"]?.Values<string>().ToArray()! ?? Array.Empty<string>();
        List<BoundParameterInfo> bound = BoundAsStandard(skip);
        foreach (JToken entry in spec["bound_extra"] ?? new JArray())
        {
            string name = (string)entry["name"]!;
            string? guidText = (string?)entry["guid"];
            Guid? guid = guidText is null ? null : guidText == "standard" ? Standard.Parameter(name)!.Guid : new Guid(guidText);
            GisParameterBinding binding = (string)entry["binding"]! == "type" ? GisParameterBinding.Type : GisParameterBinding.Instance;
            bound.Add(new BoundParameterInfo(name, guid, binding, entry["categories"]!.Values<string>()!));
        }

        List<FloorTypeInfo> floorTypes = FloorTypesAsStandard();
        foreach (JToken edit in spec["standard_type_edits"] ?? new JArray())
        {
            FloorTypeInfo target = floorTypes[(int)edit["index"]!];
            target.GisCategory = (string?)edit["gis_category"] ?? target.GisCategory;
            target.GisRestriction = (string?)edit["gis_restriction"] ?? target.GisRestriction;
            target.Material = (string?)edit["material"] ?? target.Material;
        }

        foreach (JToken entry in spec["floor_types_extra"] ?? new JArray())
        {
            floorTypes.Add(new FloorTypeInfo(
                (long)entry["id"]!,
                (string)entry["name"]!,
                (string?)entry["gis_category"],
                (string?)entry["gis_restriction"],
                (string?)entry["material"],
                entry["floor_ids"]?.Values<long>()));
        }

        List<MaterialInfo> materials = MaterialsAsStandard();
        foreach (JToken edit in spec["material_edits"] ?? new JArray())
        {
            materials[(int)edit["index"]!].Color = (string)edit["color"]!;
        }

        IEnumerable<FloorInfo> floors = (spec["floors"] ?? new JArray())
            .Select(entry => new FloorInfo((long)entry["id"]!, (long)entry["type_id"]!, (string?)entry["category"]))
            .ToList();
        IEnumerable<LevelInfo>? levels = spec["levels"]?
            .Select(entry => new LevelInfo(
                (long)entry["id"]!,
                (string)entry["name"]!,
                (int?)entry["ordinal"],
                (string?)entry["short_name"],
                (bool?)entry["has_floors"] ?? true))
            .ToList();
        IEnumerable<FamilyTypeInfo> familyTypes = (spec["family_types"] ?? new JArray())
            .Select(entry => new FamilyTypeInfo(
                (long)entry["id"]!,
                (string)entry["family"]!,
                (string)entry["type"]!,
                (string?)entry["gis_kind"],
                entry["instance_ids"]?.Values<long>(),
                (string?)entry["gis_category"]))
            .ToList();

        return ModelOnStandard(bound, floorTypes, floors, materials, levels, familyTypes, standardVersion);
    }

    private static string SeverityText(GisAuditSeverity severity) => severity switch
    {
        GisAuditSeverity.Error => "error",
        GisAuditSeverity.Warning => "warning",
        _ => "info",
    };

    private static string Format(IEnumerable<string> lines) => string.Join("; ", lines);

    private static JObject LoadCases([CallerFilePath] string sourceFile = "")
    {
        string path = Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", "Fixtures", "Conformance", "gis-tools", "cases.json");
        return JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
    }
}
