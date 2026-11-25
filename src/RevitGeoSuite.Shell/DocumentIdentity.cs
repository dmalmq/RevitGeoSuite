using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.Shell;

/// <summary>
/// Assigns and validates an opaque per-session identity token for each live Revit
/// <see cref="Document"/> using <see cref="Document.Equals(object)"/> (Autodesk's supported
/// identity check across managed wrappers). Because the token is tied to the live open document
/// instance rather than mutable <see cref="Document.PathName"/> or <see cref="Document.Title"/>,
/// it stays stable across first Save and Save As while still distinguishing two simultaneously
/// open documents that share a title or <see cref="Document.CreationGUID"/>.
/// </summary>
internal static class DocumentIdentity
{
    private static readonly object Gate = new();
    private static readonly List<Entry> Entries = new();

    public static bool IsSameDocument(Document? left, Document? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        try
        {
            return left.IsValidObject && right.IsValidObject && left.Equals(right);
        }
        catch
        {
            return false;
        }
    }

    public static string Create(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!document.IsValidObject)
        {
            throw new InvalidOperationException("The active Revit document is no longer valid.");
        }

        lock (Gate)
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                Document candidate = Entries[i].Document;
                bool valid;
                try
                {
                    valid = candidate.IsValidObject;
                }
                catch
                {
                    valid = false;
                }

                if (!valid)
                {
                    Entries.RemoveAt(i);
                    continue;
                }

                try
                {
                    if (candidate.Equals(document))
                    {
                        return Entries[i].Token;
                    }
                }
                catch
                {
                    Entries.RemoveAt(i);
                }
            }

            string token = Guid.NewGuid().ToString("N");
            Entries.Add(new Entry(document, token));
            return token;
        }
    }

    public static bool Matches(Document document, string? expectedDocumentId)
    {
        if (document is null || string.IsNullOrWhiteSpace(expectedDocumentId))
        {
            return false;
        }

        if (!document.IsValidObject)
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
            string activeTitle = document.IsValidObject && !string.IsNullOrWhiteSpace(document.Title)
                ? document.Title
                : "another model";
            throw new InvalidOperationException(
                $"Active Revit model changed to '{activeTitle}' before {operationDescription} could be applied. " +
                "Switch back to the original model or reload the page to review the current model before retrying.");
        }
    }

    private sealed class Entry
    {
        public Entry(Document document, string token)
        {
            Document = document;
            Token = token;
        }

        public Document Document { get; }

        public string Token { get; }
    }
}
