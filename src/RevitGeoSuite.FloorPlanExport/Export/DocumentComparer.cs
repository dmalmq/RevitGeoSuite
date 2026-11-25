using Autodesk.Revit.DB;

namespace RevitGeoSuite.FloorPlanExport.Export;

/// <summary>
/// Tells whether two <see cref="Document"/> references are the same Revit document. Revit can hand
/// out a distinct managed wrapper for one document, so a reference comparison would take a host
/// element for a linked one; <see cref="Document.Equals(object)"/> compares the document itself.
/// </summary>
internal static class DocumentComparer
{
    /// <summary>False when either is null or no longer valid (a closed document).</summary>
    public static bool IsSameDocument(Document? left, Document? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        if (ReferenceEquals(left, right))
        {
            return left.IsValidObject;
        }

        try
        {
            return left.IsValidObject && right.IsValidObject && left.Equals(right);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return false;
        }
    }
}
