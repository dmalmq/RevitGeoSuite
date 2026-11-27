using System;

namespace RevitGeoSuite.Core.Text;

/// <summary>
/// The normalized level key that groups 3D Tiles content by level (<c>Tiles3DLevelGrouper</c>) and that
/// the federation checker compares across models, so both agree on which levels collide.
/// </summary>
public static class LevelKey
{
    public const string Unassigned = "unassigned";
    public const string UnassignedName = "Unassigned";

    public static string Build(string? levelName)
    {
        string trimmed = levelName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0
            || string.Equals(trimmed, UnassignedName, StringComparison.OrdinalIgnoreCase))
        {
            return Unassigned;
        }

        char[] chars = trimmed.ToLowerInvariant().ToCharArray();
        bool lastWasSeparator = false;
        int writeIndex = 0;
        foreach (char c in chars)
        {
            if (char.IsLetterOrDigit(c))
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
            ? Unassigned
            : new string(chars, 0, writeIndex);
    }
}
