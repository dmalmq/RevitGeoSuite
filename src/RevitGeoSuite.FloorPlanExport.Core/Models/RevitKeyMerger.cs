using System;
using System.Collections.Generic;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

/// <summary>
/// Joins the same layer from several views into one, keeping <see cref="RevitKey"/> unique per
/// floor. When two views of one floor both show an element, the copy from the later view gets the
/// next free part number, and the renames are returned so that the detail lines of that view can
/// point their <c>parent_key</c> at their own copy of the stair.
/// </summary>
public sealed class RevitKeyMerger
{
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);

    /// <summary>
    /// Adds the features of <paramref name="source"/> to <paramref name="target"/>. A
    /// <c>parent_key</c> named in <paramref name="parentRenames"/> (the renames of the parent
    /// layer from the same view) is rewritten to match. Returns this layer's renames, keyed by
    /// floor and old key.
    /// </summary>
    public IReadOnlyDictionary<string, string> Append(
        ExportLayer target,
        ExportLayer source,
        IReadOnlyDictionary<string, string>? parentRenames = null)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        Dictionary<string, string> renames = new(StringComparer.Ordinal);
        foreach (IExportFeature original in source.Features)
        {
            IExportFeature feature = original;
            string scope = RevitKey.ReadScope(feature);

            if (parentRenames != null &&
                parentRenames.Count > 0 &&
                feature.Attributes.TryGetValue(RevitKey.ParentAttributeName, out object? parentValue) &&
                parentValue is string parentKey &&
                parentKey.Length > 0 &&
                parentRenames.TryGetValue(RevitKey.Scoped(scope, parentKey), out string? renamedParent))
            {
                feature = RevitKey.WithAttribute(feature, RevitKey.ParentAttributeName, renamedParent);
            }

            string? key = RevitKey.ReadKey(feature);
            if (key != null && !_taken.Add(RevitKey.Scoped(scope, key)))
            {
                string next = RevitKey.NextFreeKey(key, scope, _taken);
                _taken.Add(RevitKey.Scoped(scope, next));
                renames[RevitKey.Scoped(scope, key)] = next;
                feature = RevitKey.WithAttribute(feature, RevitKey.AttributeName, next);
            }

            target.AddFeature(feature);
        }

        return renames;
    }
}
