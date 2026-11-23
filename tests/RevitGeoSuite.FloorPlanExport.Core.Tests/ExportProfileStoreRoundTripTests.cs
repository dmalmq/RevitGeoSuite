using System;
using System.IO;
using System.Linq;
using System.Reflection;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.UI;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests;

public sealed class ExportProfileStoreRoundTripTests
{
    // Identity, or names the store resolves against its profile lists.
    private static readonly string[] NotPlainValues =
    {
        nameof(ExportProfile.Name),
        nameof(ExportProfile.Scope),
        nameof(ExportProfile.ActiveSchemaProfileName),
        nameof(ExportProfile.ActiveValidationPolicyProfileName),
    };

    [Fact]
    public void SaveAndLoad_KeepsTheSevenExportSettings()
    {
        ExportProfile loaded = RoundTrip(new ExportProfile
        {
            Name = "Station",
            Scope = ExportProfileScope.Project,
            OutputFormat = ExportFormat.Shapefile,
            SimplifyStairUnits = true,
            SimplifyEscalatorUnits = true,
            Use3DSectionBoxExport = true,
            SectionBoxAboveFloorMeters = 2.5,
            SectionBoxBelowFloorMeters = 0.75,
            Keep3DTempViewsForDebug = true,
        });

        Assert.Equal(ExportFormat.Shapefile, loaded.OutputFormat);
        Assert.True(loaded.SimplifyStairUnits);
        Assert.True(loaded.SimplifyEscalatorUnits);
        Assert.True(loaded.Use3DSectionBoxExport);
        Assert.Equal(2.5, loaded.SectionBoxAboveFloorMeters);
        Assert.Equal(0.75, loaded.SectionBoxBelowFloorMeters);
        Assert.True(loaded.Keep3DTempViewsForDebug);
    }

    [Fact]
    public void SaveAndLoad_KeepsEveryPlainProperty()
    {
        ExportProfile profile = new() { Name = "Station", Scope = ExportProfileScope.Project };
        PropertyInfo[] plainProperties = typeof(ExportProfile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite && !NotPlainValues.Contains(property.Name))
            .Where(property => property.PropertyType.IsEnum ||
                               property.PropertyType == typeof(bool) ||
                               property.PropertyType == typeof(int) ||
                               property.PropertyType == typeof(double) ||
                               property.PropertyType == typeof(string))
            .ToArray();
        foreach (PropertyInfo property in plainProperties)
        {
            property.SetValue(profile, ChangedValue(property.PropertyType, property.GetValue(profile)));
        }

        ExportProfile loaded = RoundTrip(profile);

        foreach (PropertyInfo property in plainProperties)
        {
            Assert.True(
                Equals(property.GetValue(profile), property.GetValue(loaded)),
                $"{property.Name} was not kept.");
        }
    }

    [Fact]
    public void LoadAndSave_NormalizesLegacyJgd2024EpsgCodes10267To10285()
    {
        string directory = Path.Combine(Path.GetTempPath(), "export-profile-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "profiles.json");
            File.WriteAllText(
                path,
                @"{
  ""GlobalProfiles"": [
    { ""Name"": ""Legacy Global Zone 1"", ""Scope"": 0, ""TargetEpsg"": 10267 }
  ],
  ""ProjectProfiles"": {
    ""project-a"": [
      { ""Name"": ""Legacy Project Zone 9"", ""Scope"": 1, ""TargetEpsg"": 10275 },
      { ""Name"": ""Legacy Project Zone 19"", ""Scope"": 1, ""TargetEpsg"": 10285 }
    ]
  }
}");

            ExportProfileStore store = new(path);
            var loaded = store.LoadWithDiagnostics("project-a").Value;

            Assert.Equal(6669, loaded.Single(profile => profile.Name == "Legacy Global Zone 1").TargetEpsg);
            Assert.Equal(6677, loaded.Single(profile => profile.Name == "Legacy Project Zone 9").TargetEpsg);
            Assert.Equal(6687, loaded.Single(profile => profile.Name == "Legacy Project Zone 19").TargetEpsg);

            store.SaveProfile("project-a", new ExportProfile
            {
                Name = "Saved Legacy Zone 1",
                Scope = ExportProfileScope.Project,
                TargetEpsg = 10267,
            });

            Assert.Equal(6669, store.LoadWithDiagnostics("project-a").Value.Single(profile => profile.Name == "Saved Legacy Zone 1").TargetEpsg);
            Assert.DoesNotContain("10267", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static object ChangedValue(Type type, object? current)
    {
        if (type.IsEnum)
        {
            // The store reads ExportFeatureType.None as All, so None cannot round-trip.
            return Enum.GetValues(type)
                .Cast<object>()
                .First(value => !Equals(value, current) && !Equals(value, ExportFeatureType.None));
        }

        if (type == typeof(bool))
        {
            return !(bool)current!;
        }

        if (type == typeof(int))
        {
            return (int)current! + 1;
        }

        if (type == typeof(double))
        {
            return (double)current! + 1.5;
        }

        return "changed";
    }

    private static ExportProfile RoundTrip(ExportProfile profile)
    {
        string directory = Path.Combine(Path.GetTempPath(), "export-profile-tests", Guid.NewGuid().ToString("N"));
        try
        {
            ExportProfileStore store = new(Path.Combine(directory, "profiles.json"));
            store.SaveProfile("project-a", profile);
            return store.LoadWithDiagnostics("project-a").Value.Single();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
