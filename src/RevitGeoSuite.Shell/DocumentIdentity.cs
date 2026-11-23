using System;
using System.IO;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.Shell;

/// <summary>
/// Builds and validates a stable identity token for a Revit <see cref="Document"/> from its
/// <see cref="Document.CreationGUID"/>, normalized <see cref="Document.PathName"/>, and
/// <see cref="Document.Title"/>. Used to prevent silent writes into a different model when the
/// user switches active documents in Revit while the modeless WebView2 shell is open.
/// </summary>
internal static class DocumentIdentity
{
    public static string Create(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        string creationGuid = document.CreationGUID != Guid.Empty
            ? document.CreationGUID.ToString("N")
            : string.Empty;

        string path = (document.PathName ?? string.Empty).Trim();
        if (path.Length > 0)
        {
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                // Preserve raw PathName when normalization fails (e.g. cloud model paths).
            }
        }

        string title = (document.Title ?? string.Empty).Trim();
        return $"{creationGuid}|{path.ToUpperInvariant()}|{title}";
    }

    public static bool Matches(Document document, string? expectedDocumentId)
    {
        if (document is null || string.IsNullOrWhiteSpace(expectedDocumentId))
        {
            return false;
        }

        return string.Equals(Create(document), expectedDocumentId!.Trim(), StringComparison.Ordinal);
    }

    public static void EnsureMatches(Document document, string? expectedDocumentId, string operationDescription)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (string.IsNullOrWhiteSpace(expectedDocumentId))
        {
            return;
        }

        if (!Matches(document, expectedDocumentId))
        {
            string activeTitle = string.IsNullOrWhiteSpace(document.Title) ? "another model" : document.Title;
            throw new InvalidOperationException(
                $"Active Revit model changed to '{activeTitle}' before {operationDescription} could be applied. " +
                "Switch back to the original model or reload the page to review the current model before retrying.");
        }
    }
}
