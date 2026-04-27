using System;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DObjectMetadata
{
    public string RevitElementId { get; set; } = string.Empty;

    public string RevitUniqueId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string FamilyName { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public string LevelName { get; set; } = "Unassigned";

    public string LevelKey { get; set; } = Tiles3DLevelMetadata.UnassignedLevelKey;

    public double LevelElevationMeters { get; set; }

    public double HeightMeters { get; set; }

    public double MinZMeters { get; set; }

    public double MaxZMeters { get; set; }

    public string SourceDocument { get; set; } = string.Empty;

    public string SourceLinkName { get; set; } = string.Empty;

    public Tiles3DObjectMetadata Clone()
    {
        return new Tiles3DObjectMetadata
        {
            RevitElementId = RevitElementId,
            RevitUniqueId = RevitUniqueId,
            Name = Name,
            Category = Category,
            FamilyName = FamilyName,
            TypeName = TypeName,
            LevelName = LevelName,
            LevelKey = LevelKey,
            LevelElevationMeters = LevelElevationMeters,
            HeightMeters = HeightMeters,
            MinZMeters = MinZMeters,
            MaxZMeters = MaxZMeters,
            SourceDocument = SourceDocument,
            SourceLinkName = SourceLinkName
        };
    }
}

public static class Tiles3DLevelMetadata
{
    public const string UnassignedLevelKey = "unassigned";
    public const string UnassignedLevelName = "Unassigned";

    public static string BuildLevelKey(string? levelName)
    {
        string trimmed = levelName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0
            || string.Equals(trimmed, UnassignedLevelName, StringComparison.OrdinalIgnoreCase))
        {
            return UnassignedLevelKey;
        }

        char[] chars = trimmed.ToLowerInvariant().ToCharArray();
        bool lastWasSeparator = false;
        int writeIndex = 0;
        foreach (char c in chars)
        {
            bool isIdentifierChar = char.IsLetterOrDigit(c);
            if (isIdentifierChar)
            {
                chars[writeIndex++] = c;
                lastWasSeparator = false;
                continue;
            }

            if (!lastWasSeparator && writeIndex > 0)
            {
                chars[writeIndex++] = '_';
                lastWasSeparator = true;
            }
        }

        while (writeIndex > 0 && chars[writeIndex - 1] == '_')
        {
            writeIndex--;
        }

        return writeIndex == 0
            ? UnassignedLevelKey
            : new string(chars, 0, writeIndex);
    }
}
