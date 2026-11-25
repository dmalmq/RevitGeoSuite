using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>Reason a <see cref="HandoverReceiptVerifier"/> run failed.</summary>
public enum HandoverVerificationStatus
{
    Valid,
    HashMismatch,
    DocumentVersionGuidMismatch,
    NumberOfSavesMismatch,
    MalformedJson,
    SchemaMismatch,
}

/// <summary>Result of a <see cref="HandoverReceiptVerifier"/> run.</summary>
public sealed class HandoverVerificationResult
{
    public HandoverVerificationResult(HandoverVerificationStatus status, string reason)
    {
        Status = status;
        Reason = reason ?? string.Empty;
    }

    public HandoverVerificationStatus Status { get; }

    public string Reason { get; }

    public bool IsValid => Status == HandoverVerificationStatus.Valid;

    public static HandoverVerificationResult Valid() =>
        new(HandoverVerificationStatus.Valid, "Hash, document version GUID and save count match.");
}

/// <summary>
/// Verifies a <see cref="HandoverReceipt"/> JSON blob against the live .rvt it describes, without
/// opening the .rvt. Callers supply <c>(documentVersionGuid, numberOfSaves)</c> from
/// <c>Autodesk.Revit.DB.BasicFileInfo.Extract(path).GetDocumentVersion()</c>.
/// </summary>
public static class HandoverReceiptVerifier
{
    public static HandoverVerificationResult Verify(string receiptJson, Guid documentVersionGuid, int numberOfSaves)
    {
        if (string.IsNullOrWhiteSpace(receiptJson))
        {
            return new HandoverVerificationResult(HandoverVerificationStatus.MalformedJson, "Receipt JSON was empty.");
        }

        JObject root;
        try
        {
            using StringReader input = new(receiptJson);
            using JsonTextReader reader = new(input) { DateParseHandling = DateParseHandling.None };
            root = JObject.Load(reader, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
            });
            if (reader.Read())
            {
                return Malformed("Unexpected content after the receipt object.");
            }
        }
        catch (JsonException)
        {
            return Malformed("Receipt JSON is malformed.");
        }

        if (!string.Equals(StringValue(root["schema"]), HandoverReceipt.SchemaName, StringComparison.Ordinal)
            || !TryReadNonNegativeInteger(root["version"], out int version) || version != HandoverReceipt.SchemaVersion)
        {
            return new HandoverVerificationResult(
                HandoverVerificationStatus.SchemaMismatch, "Expected kanizu.handover-receipt v1.");
        }

        string? declaredHash = StringValue(root["sha256"]);
        root.Remove("sha256");
        string computedHash = HandoverReceiptHashing.Sha256Hex(CanonicalJson.Serialize(root));
        if (!string.Equals(declaredHash, computedHash, StringComparison.Ordinal))
        {
            return new HandoverVerificationResult(
                HandoverVerificationStatus.HashMismatch, "Receipt sha256 is missing or does not match the content.");
        }

        if (root["model"] is not JObject model
            || !Guid.TryParseExact(StringValue(model["documentVersionGuid"]), "D", out Guid expectedGuid)
            || expectedGuid == Guid.Empty
            || !TryReadNonNegativeInteger(model["numberOfSaves"], out int expectedSaves))
        {
            return Malformed("Receipt model requires a document version GUID and a non-negative save count.");
        }

        if (expectedGuid != documentVersionGuid)
        {
            return new HandoverVerificationResult(
                HandoverVerificationStatus.DocumentVersionGuidMismatch,
                $"Document version GUID mismatch. Receipt was generated for {expectedGuid:D} but the .rvt now reports {documentVersionGuid:D}.");
        }

        if (expectedSaves != numberOfSaves)
        {
            return new HandoverVerificationResult(
                HandoverVerificationStatus.NumberOfSavesMismatch,
                $"Save-count mismatch. Receipt was generated at save {expectedSaves} but the .rvt now reports save {numberOfSaves}.");
        }

        return HandoverVerificationResult.Valid();
    }

    private static string? StringValue(JToken? token) =>
        token?.Type == JTokenType.String ? (string?)token : null;

    private static bool TryReadNonNegativeInteger(JToken? token, out int value)
    {
        value = 0;
        return token?.Type == JTokenType.Integer
            && int.TryParse(token.ToString(Formatting.None), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static HandoverVerificationResult Malformed(string reason) =>
        new(HandoverVerificationStatus.MalformedJson, reason);
}
