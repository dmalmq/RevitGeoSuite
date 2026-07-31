using System;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop;

namespace RevitGeoSuite.RevitInterop.Storage;

public sealed class MeshCodePersistenceService
{
    private readonly IGeoProjectInfoStore geoProjectInfoStore;

    public MeshCodePersistenceService(IGeoProjectInfoStore? geoProjectInfoStore = null)
    {
        this.geoProjectInfoStore = geoProjectInfoStore ?? new GeoProjectInfoStorage();
    }

    public void SavePrimaryMeshCode(IDocumentHandle document, MeshCode meshCode)
    {
        if (meshCode is null)
        {
            throw new ArgumentNullException(nameof(meshCode));
        }

        RevitDocumentHandle handle = document as RevitDocumentHandle
            ?? throw new InvalidOperationException("Mesh code persistence requires a RevitDocumentHandle.");
        Document revitDocument = handle.Document;

        if (revitDocument.IsFamilyDocument)
        {
            throw new InvalidOperationException("Mesh code persistence is not supported in family documents.");
        }

        if (revitDocument.IsReadOnly)
        {
            throw new InvalidOperationException("This Revit document is read-only. Saving the primary mesh code requires an editable project.");
        }

        if (revitDocument.IsModifiable)
        {
            throw new InvalidOperationException("Another Revit transaction is already active. Finish it before saving the primary mesh code.");
        }

        var info = geoProjectInfoStore.Load(handle)
            ?? throw new InvalidOperationException("Shared geo metadata is missing. Run Georeference Setup before saving a primary mesh code.");

        using Transaction transaction = new Transaction(revitDocument, "Save Primary Mesh Code");
        transaction.Start();
        try
        {
            info.PrimaryMeshCode = new MeshCode { Value = meshCode.Value };
            geoProjectInfoStore.Save(handle, info);
            TransactionStatus status = transaction.Commit();
            if (status != TransactionStatus.Committed)
            {
                throw new InvalidOperationException("Revit did not commit the primary mesh code transaction.");
            }
        }
        catch (Exception ex)
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw new InvalidOperationException("Saving the primary mesh code failed. Revit rolled back the transaction. " + ex.Message, ex);
        }
    }
}
