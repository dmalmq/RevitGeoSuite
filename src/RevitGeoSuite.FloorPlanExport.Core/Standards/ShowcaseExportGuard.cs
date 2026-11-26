using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>Revit-API-free parameter storage kind mirror for populated-value checks.</summary>
public enum ShowcaseParameterStorageKind
{
    None = 0,
    Integer = 1,
    Double = 2,
    String = 3,
    ElementId = 4,
}

/// <summary>Reason an element or document was classified as Forma Companion showcase content.</summary>
public enum ShowcaseElementReason
{
    None = 0,
    LinkedShowcaseDocument,
    ShowcaseApplicationId,
    ShowcaseFamilyName,
    ShowcaseTypeName,
    ShowcaseCategoryName,
    ShowcaseParameter,
}

/// <summary>Revit-API-free descriptor of a model element checked by <see cref="ShowcaseExportGuard"/>.</summary>
public sealed class ShowcaseElementDescriptor
{
    public ShowcaseElementDescriptor(
        string? applicationId = null,
        string? familyName = null,
        string? typeName = null,
        IEnumerable<string>? parameterNames = null,
        string? sourceDocumentModelRole = null,
        string? categoryName = null)
    {
        ApplicationId = applicationId;
        FamilyName = familyName;
        TypeName = typeName;
        CategoryName = categoryName;
        ParameterNames = parameterNames == null
            ? Array.Empty<string>()
            : Array.AsReadOnly(parameterNames.Where(name => name != null).ToArray());
        SourceDocumentModelRole = sourceDocumentModelRole;
    }

    public string? ApplicationId { get; }

    public string? FamilyName { get; }

    public string? TypeName { get; }

    public string? CategoryName { get; }

    public IReadOnlyList<string> ParameterNames { get; }

    public string? SourceDocumentModelRole { get; }
}

/// <summary>Classification outcome for a single element checked by <see cref="ShowcaseExportGuard"/>.</summary>
public sealed class ShowcaseElementCheckResult
{
    public static ShowcaseElementCheckResult NotShowcase { get; } =
        new(isShowcase: false, ShowcaseElementReason.None, string.Empty, string.Empty);

    public ShowcaseElementCheckResult(
        bool isShowcase,
        ShowcaseElementReason reason,
        string matchedMarker,
        string detail)
    {
        IsShowcase = isShowcase;
        Reason = reason;
        MatchedMarker = matchedMarker ?? string.Empty;
        Detail = detail ?? string.Empty;
    }

    public bool IsShowcase { get; }

    public ShowcaseElementReason Reason { get; }

    public string MatchedMarker { get; }

    public string Detail { get; }
}

/// <summary>Gate decision for whether an authoring 2D or 3D export may run on a model.</summary>
public sealed class ShowcaseExportGateResult
{
    public ShowcaseExportGateResult(
        bool isAllowed,
        string? normalizedModelRole,
        string message,
        string messageJa)
    {
        IsAllowed = isAllowed;
        NormalizedModelRole = normalizedModelRole;
        Message = message ?? string.Empty;
        MessageJa = messageJa ?? string.Empty;
    }

    public bool IsAllowed { get; }

    public bool IsBlocked => !IsAllowed;

    public string? NormalizedModelRole { get; }

    public string Message { get; }

    public string MessageJa { get; }
}

/// <summary>A showcase element detected inside an authoring model or template (for <c>KZ-ELEM-005</c>).</summary>
public sealed class ShowcaseElementFinding
{
    public ShowcaseElementFinding(
        long elementId,
        string label,
        ShowcaseElementReason reason,
        string marker)
    {
        ElementId = elementId;
        Label = label ?? string.Empty;
        Reason = reason;
        Marker = marker ?? string.Empty;
    }

    public long ElementId { get; }

    public string Label { get; }

    public ShowcaseElementReason Reason { get; }

    public string Marker { get; }
}

/// <summary>Pure-C# evaluation result for a showcase isolation readiness rule (<c>KZ-FED-009</c> or <c>KZ-ELEM-005</c>).</summary>
public sealed class ShowcaseRuleEvaluationResult
{
    public ShowcaseRuleEvaluationResult(
        string ruleId,
        string itemId,
        RuleSeverity severity,
        ReadinessCheckStatus status,
        string title,
        string titleJa,
        string messageEn,
        string messageJa,
        IEnumerable<long>? elementIds = null)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        ItemId = itemId ?? throw new ArgumentNullException(nameof(itemId));
        Severity = severity;
        Status = status;
        Title = title ?? string.Empty;
        TitleJa = titleJa ?? string.Empty;
        MessageEn = messageEn ?? string.Empty;
        MessageJa = messageJa ?? string.Empty;
        ElementIds = Array.AsReadOnly((elementIds ?? Array.Empty<long>()).Distinct().ToArray());
    }

    public string RuleId { get; }

    public string ItemId { get; }

    public RuleSeverity Severity { get; }

    public ReadinessCheckStatus Status { get; }

    public string Title { get; }

    public string TitleJa { get; }

    public string MessageEn { get; }

    public string MessageJa { get; }

    public IReadOnlyList<long> ElementIds { get; }

    public ReadinessCheckItem ToReadinessCheckItem() =>
        new(
            ItemId,
            Title,
            Status,
            MessageEn,
            ReadinessActionKind.None,
            ElementIds);
}

/// <summary>
/// Layer 1 pure-C# export-loop guard isolating Forma Companion showcase models and elements
/// (<c>gis_model_role = "showcase"</c>, <c>ApplicationId == "Kanizu.Showcase"</c>, <c>KNZ_FRM_*</c>
/// family/type/category names, and <c>KS_*</c>/<c>ks_*</c> companion parameters) from authoring 2D
/// GeoPackage and 3D Tiles exports.
/// </summary>
public static class ShowcaseExportGuard
{
    public const string AuthoringModelRole = "authoring";
    public const string ShowcaseModelRole = "showcase";
    public const string ShowcaseApplicationId = "Kanizu.Showcase";
    public const string ShowcaseFamilyNamePrefix = "KNZ_FRM_";
    public const string ShowcaseParameterPrefix = "KS_";
    public const string ShowcaseViewFilterPrefix = "[KANIZU_FORMA_RO]";

    /// <summary>Frozen GUID of the <c>gis_model_role</c> shared parameter (Kanizu GIS Standard v3).</summary>
    public static readonly Guid GisModelRoleParameterGuid = new("90086db8-5f21-46cd-b4d5-5be2728a0e18");

    /// <summary>
    /// Normalizes a <c>gis_model_role</c> value to canonical <c>"authoring"</c> or <c>"showcase"</c>,
    /// or <c>null</c> when blank, absent (v1/v2 authoring models), or unrecognized.
    /// </summary>
    public static string? NormalizeModelRole(string? gisModelRole) =>
        GisStandardDomains.TryNormalizeModelRole(gisModelRole, out string canonical)
            ? canonical
            : null;

    /// <summary>
    /// Effective model role for export decisions. Legacy v1/v2 models without <c>gis_model_role</c>
    /// default cleanly to <c>"authoring"</c>.
    /// </summary>
    public static string ResolveEffectiveModelRole(string? gisModelRole) =>
        NormalizeModelRole(gisModelRole) ?? AuthoringModelRole;

    /// <summary>True when <paramref name="gisModelRole"/> identifies a Forma Companion showcase model.</summary>
    public static bool IsShowcaseModelRole(string? gisModelRole) =>
        string.Equals(NormalizeModelRole(gisModelRole), ShowcaseModelRole, StringComparison.Ordinal);

    /// <summary>True when a DirectShape <c>ApplicationId</c> belongs to the Forma Companion showcase builder.</summary>
    public static bool IsShowcaseApplicationId(string? applicationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return false;
        }

        return string.Equals(applicationId!.Trim(), ShowcaseApplicationId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when a family, type, category, or subcategory name carries the <c>KNZ_FRM_</c> showcase prefix.</summary>
    public static bool IsShowcaseFamilyOrTypeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name!.Trim().StartsWith(ShowcaseFamilyNamePrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when a parameter name carries the <c>KS_</c> / <c>ks_</c> companion showcase prefix.</summary>
    public static bool IsShowcaseParameterName(string? parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        return parameterName!.Trim().StartsWith(ShowcaseParameterPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when a <c>KS_*</c>/<c>ks_*</c> parameter carries a meaningful populated value rather than a
    /// category-wide default (<c>None</c>, blank text, <c>InvalidElementId</c>, <c>0</c>, or <c>0.0</c>)
    /// on an untouched authoring element.
    /// </summary>
    public static bool IsPopulatedParameterValue(
        ShowcaseParameterStorageKind storageKind,
        bool hasValue,
        string? stringValue = null,
        int integerValue = 0,
        double doubleValue = 0d,
        long? elementIdValue = null)
    {
        if (!hasValue)
        {
            return false;
        }

        switch (storageKind)
        {
            case ShowcaseParameterStorageKind.String:
                return !string.IsNullOrWhiteSpace(stringValue);
            case ShowcaseParameterStorageKind.Integer:
                return integerValue != 0;
            case ShowcaseParameterStorageKind.Double:
                return !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue) && doubleValue != 0d;
            case ShowcaseParameterStorageKind.ElementId:
                return elementIdValue.HasValue && elementIdValue.Value != -1L;
            case ShowcaseParameterStorageKind.None:
            default:
                return false;
        }
    }

    /// <summary>True when a view filter name uses the reserved <c>[KANIZU_FORMA_RO]</c> showcase-exclusion prefix.</summary>
    public static bool IsReservedShowcaseFilterName(string? filterName)
    {
        if (string.IsNullOrWhiteSpace(filterName))
        {
            return false;
        }

        return filterName!.Trim().StartsWith(ShowcaseViewFilterPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when any parameter name in <paramref name="parameterNames"/> carries the <c>KS_</c>/<c>ks_</c> prefix.</summary>
    public static bool HasShowcaseParameter(IEnumerable<string>? parameterNames) =>
        TryFindShowcaseParameter(parameterNames, out _);

    /// <summary>Finds the first <c>KS_</c>/<c>ks_</c> showcase parameter name in <paramref name="parameterNames"/>.</summary>
    public static bool TryFindShowcaseParameter(IEnumerable<string>? parameterNames, out string matchedParameterName)
    {
        if (parameterNames != null)
        {
            foreach (string? name in parameterNames)
            {
                if (IsShowcaseParameterName(name))
                {
                    matchedParameterName = name!.Trim();
                    return true;
                }
            }
        }

        matchedParameterName = string.Empty;
        return false;
    }

    /// <summary>Classifies an element using all 4 showcase markers and linked-document role.</summary>
    public static ShowcaseElementCheckResult ClassifyElement(ShowcaseElementDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return ShowcaseElementCheckResult.NotShowcase;
        }

        return ClassifyElement(
            descriptor.ApplicationId,
            descriptor.FamilyName,
            descriptor.TypeName,
            descriptor.ParameterNames,
            descriptor.SourceDocumentModelRole,
            descriptor.CategoryName);
    }

    /// <summary>Classifies an element using all showcase markers and returns the exact exclusion reason.</summary>
    public static ShowcaseElementCheckResult ClassifyElement(
        string? applicationId = null,
        string? familyName = null,
        string? typeName = null,
        IEnumerable<string>? parameterNames = null,
        string? sourceDocumentModelRole = null,
        string? categoryName = null)
    {
        if (IsShowcaseModelRole(sourceDocumentModelRole))
        {
            return new ShowcaseElementCheckResult(
                isShowcase: true,
                ShowcaseElementReason.LinkedShowcaseDocument,
                ShowcaseModelRole,
                "Element originates from a linked showcase document (gis_model_role = 'showcase').");
        }

        if (IsShowcaseApplicationId(applicationId))
        {
            string matched = applicationId!.Trim();
            return new ShowcaseElementCheckResult(
                isShowcase: true,
                ShowcaseElementReason.ShowcaseApplicationId,
                matched,
                $"DirectShape ApplicationId '{matched}' belongs to the Forma Companion showcase.");
        }

        if (IsShowcaseFamilyOrTypeName(familyName))
        {
            string matched = familyName!.Trim();
            return new ShowcaseElementCheckResult(
                isShowcase: true,
                ShowcaseElementReason.ShowcaseFamilyName,
                matched,
                $"Family name '{matched}' carries the reserved '{ShowcaseFamilyNamePrefix}' showcase prefix.");
        }

        if (IsShowcaseFamilyOrTypeName(typeName))
        {
            string matched = typeName!.Trim();
            return new ShowcaseElementCheckResult(
                isShowcase: true,
                ShowcaseElementReason.ShowcaseTypeName,
                matched,
                $"Type name '{matched}' carries the reserved '{ShowcaseFamilyNamePrefix}' showcase prefix.");
        }

        if (IsShowcaseFamilyOrTypeName(categoryName))
        {
            string matched = categoryName!.Trim();
            return new ShowcaseElementCheckResult(
                isShowcase: true,
                ShowcaseElementReason.ShowcaseCategoryName,
                matched,
                $"Category name '{matched}' carries the reserved '{ShowcaseFamilyNamePrefix}' showcase prefix.");
        }

        if (TryFindShowcaseParameter(parameterNames, out string matchedParameter))
        {
            return new ShowcaseElementCheckResult(
                isShowcase: true,
                ShowcaseElementReason.ShowcaseParameter,
                matchedParameter,
                $"Element carries showcase-only parameter '{matchedParameter}'.");
        }

        return ShowcaseElementCheckResult.NotShowcase;
    }

    /// <summary>True when the element carries any showcase marker or originates from a linked showcase document.</summary>
    public static bool IsShowcaseElement(ShowcaseElementDescriptor? descriptor) =>
        ClassifyElement(descriptor).IsShowcase;

    /// <summary>True when the element carries any showcase marker or originates from a linked showcase document.</summary>
    public static bool IsShowcaseElement(
        string? applicationId = null,
        string? familyName = null,
        string? typeName = null,
        IEnumerable<string>? parameterNames = null,
        string? sourceDocumentModelRole = null,
        string? categoryName = null) =>
        ClassifyElement(applicationId, familyName, typeName, parameterNames, sourceDocumentModelRole, categoryName).IsShowcase;

    /// <summary>True when the element is a showcase element, returning its reason and human-readable detail.</summary>
    public static bool IsShowcaseElement(
        string? applicationId,
        string? familyName,
        string? typeName,
        IEnumerable<string>? parameterNames,
        string? sourceDocumentModelRole,
        out ShowcaseElementReason reason,
        out string detail,
        string? categoryName = null)
    {
        ShowcaseElementCheckResult result = ClassifyElement(
            applicationId,
            familyName,
            typeName,
            parameterNames,
            sourceDocumentModelRole,
            categoryName);
        reason = result.Reason;
        detail = result.Detail;
        return result.IsShowcase;
    }

    /// <summary>
    /// Evaluates the authoring export gate for the active document's <c>gis_model_role</c>.
    /// Blocks authoring 2D/3D exports when <paramref name="modelRole"/> is <c>"showcase"</c>.
    /// Legacy models with null/empty <paramref name="modelRole"/> pass cleanly as authoring models.
    /// </summary>
    public static ShowcaseExportGateResult EvaluateAuthoringExportGate(string? modelRole, string? exportKind = null)
    {
        string? normalized = NormalizeModelRole(modelRole);
        if (!string.Equals(normalized, ShowcaseModelRole, StringComparison.Ordinal))
        {
            return new ShowcaseExportGateResult(
                isAllowed: true,
                normalized,
                string.Empty,
                string.Empty);
        }

        string scopeEn = string.IsNullOrWhiteSpace(exportKind) ? "authoring" : $"authoring {exportKind!.Trim()}";
        string scopeJa = string.IsNullOrWhiteSpace(exportKind) ? "オーサリング" : $"オーサリング {exportKind!.Trim()}";
        string messageEn =
            $"Blocked {scopeEn} export: this Revit model has gis_model_role = '{ShowcaseModelRole}'. " +
            "Forma Companion showcase models are read-only visualization artifacts and cannot be exported back into Kanizu authoring pipelines.";
        string messageJa =
            $"この Revit モデルは gis_model_role = '{ShowcaseModelRole}' に設定されているため、{scopeJa}エクスポートを中止しました。" +
            "Forma Companion ショーケースモデルは閲覧専用であり、Kanizu オーサリングパイプラインへ再出力できません。";

        return new ShowcaseExportGateResult(
            isAllowed: false,
            normalized,
            messageEn,
            messageJa);
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> when the active document is a showcase model.</summary>
    public static void ThrowIfAuthoringExportBlocked(string? modelRole, string? exportKind = null)
    {
        ShowcaseExportGateResult gate = EvaluateAuthoringExportGate(modelRole, exportKind);
        if (gate.IsBlocked)
        {
            throw new InvalidOperationException(gate.Message);
        }
    }

    /// <summary>
    /// Filters an authoring export collection, removing any item classified as showcase content while
    /// preserving the exact order of authoring items (authoring-export invariance).
    /// </summary>
    public static IReadOnlyList<T> FilterAuthoringItems<T>(
        IEnumerable<T> items,
        Func<T, ShowcaseElementDescriptor> describe,
        ICollection<string>? skippedDiagnostics = null)
    {
        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (describe is null)
        {
            throw new ArgumentNullException(nameof(describe));
        }

        List<T> kept = new();
        foreach (T item in items)
        {
            ShowcaseElementCheckResult check = ClassifyElement(describe(item));
            if (check.IsShowcase)
            {
                skippedDiagnostics?.Add(check.Detail);
                continue;
            }

            kept.Add(item);
        }

        return kept;
    }

    /// <summary>
    /// Filters linked documents for authoring export, skipping any link whose <c>gis_model_role</c>
    /// is <c>"showcase"</c> while preserving the exact order of authoring links.
    /// </summary>
    public static IReadOnlyList<T> FilterAuthoringLinkedDocuments<T>(
        IEnumerable<T> linkedDocuments,
        Func<T, string?> getModelRole,
        Func<T, string?>? getName = null,
        ICollection<string>? skippedDiagnostics = null)
    {
        if (linkedDocuments is null)
        {
            throw new ArgumentNullException(nameof(linkedDocuments));
        }

        if (getModelRole is null)
        {
            throw new ArgumentNullException(nameof(getModelRole));
        }

        List<T> kept = new();
        foreach (T link in linkedDocuments)
        {
            string? role = getModelRole(link);
            if (IsShowcaseModelRole(role))
            {
                if (skippedDiagnostics != null)
                {
                    string name = getName?.Invoke(link)?.Trim() ?? string.Empty;
                    skippedDiagnostics.Add(name.Length > 0
                        ? $"Skipped linked showcase document '{name}' (gis_model_role = '{ShowcaseModelRole}')."
                        : $"Skipped linked showcase document (gis_model_role = '{ShowcaseModelRole}').");
                }

                continue;
            }

            kept.Add(link);
        }

        return kept;
    }
}

/// <summary>
/// Pure-C# readiness evaluators for Layer 3 showcase isolation rules:
/// <c>KZ-FED-009</c> (active document role / linked showcase companion model guard) and
/// <c>KZ-ELEM-005</c> (leaked showcase elements inside an authoring model).
/// </summary>
public static class ShowcaseReadinessEvaluator
{
    public const string FederationRuleId = "KZ-FED-009";
    public const string FederationItemId = "showcase-model-role";
    public const string ElementsRuleId = "KZ-ELEM-005";
    public const string ElementsItemId = "showcase-elements";

    public static ShowcaseRuleEvaluationResult EvaluateFederationGuard(ReadinessCheckSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return EvaluateFederationGuard(snapshot.HostModelRole, snapshot.LinkedModels);
    }

    public static ShowcaseRuleEvaluationResult EvaluateFederationGuard(
        string? hostModelRole,
        IEnumerable<LinkedModelReadinessInfo>? linkedModels)
    {
        RuleCatalogEntry rule = RuleCatalog.Lookup(FederationRuleId);
        List<string> problemsEn = new();
        List<string> problemsJa = new();

        if (ShowcaseExportGuard.IsShowcaseModelRole(hostModelRole))
        {
            problemsEn.Add("Active document has gis_model_role = 'showcase'; authoring 2D/3D exports are blocked on Forma Companion models.");
            problemsJa.Add("開いているモデルの gis_model_role が 'showcase' に設定されています。Forma Companion モデルからのオーサリング 2D/3D エクスポートはブロックされます。");
        }
        else if (GisStandardDomains.ValidateOptionalValue(GisSharedParameters.GisModelRole, hostModelRole) is string invalidRoleError)
        {
            problemsEn.Add(invalidRoleError);
            problemsJa.Add($"gis_model_role の値 '{hostModelRole!.Trim()}' は無効です。許可される値: authoring, showcase。");
        }

        List<string> showcaseLinks = (linkedModels ?? Array.Empty<LinkedModelReadinessInfo>())
            .Where(link => link != null && link.IsLoaded && ShowcaseExportGuard.IsShowcaseModelRole(link.ModelRole))
            .Select(link => string.IsNullOrWhiteSpace(link.Name) ? "(unnamed link)" : link.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (showcaseLinks.Count > 0)
        {
            string names = DescribeList(showcaseLinks);
            problemsEn.Add($"{showcaseLinks.Count} linked showcase companion model(s) detected ({names}); unlink or keep excluded from authoring exports.");
            problemsJa.Add($"ショーケース用リンクモデルが {showcaseLinks.Count} 件検出されました ({names})。オーサリング用フェデレーションからリンクを解除してください。");
        }

        if (problemsEn.Count > 0)
        {
            return new ShowcaseRuleEvaluationResult(
                FederationRuleId,
                FederationItemId,
                rule.Severity,
                ReadinessCheckStatus.Fail,
                rule.Title,
                rule.TitleJa,
                string.Join(" ", problemsEn),
                string.Join(" ", problemsJa));
        }

        string? normalizedRole = ShowcaseExportGuard.NormalizeModelRole(hostModelRole);
        string passEn = normalizedRole == ShowcaseExportGuard.AuthoringModelRole
            ? "Active model role is 'authoring' and no linked showcase companion models are present."
            : "Active model role defaults to authoring (unset) and no linked showcase companion models are present.";
        string passJa = normalizedRole == ShowcaseExportGuard.AuthoringModelRole
            ? "アクティブモデルの役割は 'authoring' で、ショーケース用リンクモデルは含まれていません。"
            : "アクティブモデルの役割は未設定（オーサリング扱い）で、ショーケース用リンクモデルは含まれていません。";

        return new ShowcaseRuleEvaluationResult(
            FederationRuleId,
            FederationItemId,
            rule.Severity,
            ReadinessCheckStatus.Pass,
            rule.Title,
            rule.TitleJa,
            passEn,
            passJa);
    }

    public static ShowcaseRuleEvaluationResult EvaluateShowcaseElements(ReadinessCheckSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return EvaluateShowcaseElements(snapshot.ShowcaseElements, snapshot.LoadedFamilies);
    }

    public static ShowcaseRuleEvaluationResult EvaluateShowcaseElements(
        IEnumerable<ShowcaseElementFinding>? findings,
        IEnumerable<LoadedFamilyCheckInfo>? loadedFamilies = null)
    {
        RuleCatalogEntry rule = RuleCatalog.Lookup(ElementsRuleId);
        List<ShowcaseElementFinding> collected = new();
        HashSet<long> seenElementIds = new();

        foreach (ShowcaseElementFinding? finding in findings ?? Array.Empty<ShowcaseElementFinding>())
        {
            if (finding is null)
            {
                continue;
            }

            if (finding.ElementId > 0)
            {
                seenElementIds.Add(finding.ElementId);
            }

            collected.Add(finding);
        }

        foreach (LoadedFamilyCheckInfo? family in loadedFamilies ?? Array.Empty<LoadedFamilyCheckInfo>())
        {
            if (family is null)
            {
                continue;
            }

            IEnumerable<string> paramNames = family.ParameterBindings.Keys.Concat(family.TypeParameters.Keys);
            ShowcaseElementCheckResult check = ShowcaseExportGuard.ClassifyElement(
                applicationId: null,
                familyName: family.FamilyName,
                typeName: family.TypeName,
                parameterNames: paramNames,
                sourceDocumentModelRole: null,
                categoryName: family.CategoryName);
            if (!check.IsShowcase)
            {
                continue;
            }

            string label = string.IsNullOrWhiteSpace(family.TypeName)
                ? family.FamilyName
                : $"{family.FamilyName} : {family.TypeName}";
            if (family.InstanceIds.Count > 0)
            {
                foreach (long instanceId in family.InstanceIds)
                {
                    if (instanceId <= 0 || seenElementIds.Add(instanceId))
                    {
                        collected.Add(new ShowcaseElementFinding(instanceId, label, check.Reason, check.MatchedMarker));
                    }
                }
            }
            else if (family.ElementId <= 0 || seenElementIds.Add(family.ElementId))
            {
                collected.Add(new ShowcaseElementFinding(family.ElementId, label, check.Reason, check.MatchedMarker));
            }
        }

        if (collected.Count > 0)
        {
            List<string> labels = collected
                .Select(item => string.IsNullOrWhiteSpace(item.Label) ? item.Marker : $"{item.Label} ({item.Marker})")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<long> elementIds = collected
                .Where(item => item.ElementId > 0)
                .Select(item => item.ElementId)
                .Distinct()
                .ToList();

            string summaryEn =
                $"{collected.Count} showcase element(s) leaked into the authoring model ({DescribeList(labels)}). " +
                "Remove Kanizu.Showcase DirectShapes, KNZ_FRM_* families/types, and KS_*/ks_* showcase markers from authoring models.";
            string summaryJa =
                $"オーサリングモデル内にショーケース要素が {collected.Count} 件混入しています ({DescribeList(labels)})。" +
                "Kanizu.Showcase の DirectShape、KNZ_FRM_* ファミリ／タイプ、および KS_*/ks_* マーカーを削除してください。";

            return new ShowcaseRuleEvaluationResult(
                ElementsRuleId,
                ElementsItemId,
                rule.Severity,
                ReadinessCheckStatus.Fail,
                rule.Title,
                rule.TitleJa,
                summaryEn,
                summaryJa,
                elementIds);
        }

        return new ShowcaseRuleEvaluationResult(
            ElementsRuleId,
            ElementsItemId,
            rule.Severity,
            ReadinessCheckStatus.Pass,
            rule.Title,
            rule.TitleJa,
            "No Forma Companion showcase elements (Kanizu.Showcase, KNZ_FRM_*, KS_*/ks_*) found in the model.",
            "モデル内に Forma Companion ショーケース要素（Kanizu.Showcase、KNZ_FRM_*、KS_*/ks_*）は検出されませんでした。");
    }

    private static string DescribeList(IReadOnlyList<string> names)
    {
        const int maxShown = 4;
        string shown = string.Join(", ", names.Take(maxShown));
        return names.Count > maxShown ? $"{shown} and {names.Count - maxShown} more" : shown;
    }
}
