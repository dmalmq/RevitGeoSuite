using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.PlateauImport.Online;

public enum PlateauOnlineImportPhase
{
    LoadingCatalog,
    PickingArea,
    Downloading,
    Importing,
    Done,
    Failed,
}

public sealed class PlateauOnlineImportViewModel : INotifyPropertyChanged
{
    private readonly PlateauApiClient apiClient;
    private readonly PlateauAreaGeometryService geometryService;
    private readonly Func<PlateauTilesetDownloader> downloaderFactory;
    private readonly Dictionary<string, PlateauAreaBounds> areaBoundsByCode = new(StringComparer.Ordinal);
    // In-memory cache of fully-decoded tileset models, keyed by the dataset entry's
    // PreferredUrl. Lifetime = this VM = the importer window. Lets repeated clicks for
    // the same area + same texture variant skip the b3dm parse / Draco decode /
    // ECEF -> project transform pass, not just the network round-trip.
    private readonly Dictionary<string, PlateauTilesetModel> cachedBuildingsByUrl = new(StringComparer.Ordinal);
    private readonly List<AreaSearchOption> allSearchOptions = new();
    private PlateauCatalog? catalog;
    private string? selectedPrefecture;
    private PlateauAreaOption? selectedArea;
    private AreaSearchOption? selectedSearchOption;
    private string searchText = string.Empty;
    private PlateauOnlineImportPhase phase = PlateauOnlineImportPhase.LoadingCatalog;
    private PlateauOnlineGeometryMode geometryMode = PlateauOnlineGeometryMode.Lod2Untextured;
    private double progressFraction;
    private string statusMessage = string.Empty;
    private bool dracoAvailable;
    private string? areasGeoJson;
    private MapFocus? pendingMapFocus;
    private CancellationTokenSource? areaLoadCts;

    public PlateauOnlineImportViewModel(
        PlateauApiClient apiClient,
        PlateauAreaGeometryService geometryService,
        Func<PlateauTilesetDownloader> downloaderFactory)
    {
        this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        this.geometryService = geometryService ?? throw new ArgumentNullException(nameof(geometryService));
        this.downloaderFactory = downloaderFactory ?? throw new ArgumentNullException(nameof(downloaderFactory));
        AreasInPrefecture = new ObservableCollection<PlateauAreaOption>();
        FilteredSearchResults = new ObservableCollection<AreaSearchOption>();
        Warnings = new ObservableCollection<string>();
        ImportedDatasets = new ObservableCollection<PlateauTilesetModel>();
        Warnings.CollectionChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasWarnings)));
        DracoAvailable = NativeDracoMeshDecoder.IsAvailable();
        StatusMessage = DracoAvailable
            ? string.Empty
            : MissingDracoMeshDecoder.MissingMessage;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PlateauAreaOption> AreasInPrefecture { get; }
    public ObservableCollection<AreaSearchOption> FilteredSearchResults { get; }
    public ObservableCollection<string> Warnings { get; }
    public ObservableCollection<PlateauTilesetModel> ImportedDatasets { get; }

    public PlateauOnlineImportPhase Phase
    {
        get => phase;
        private set => SetField(ref phase, value);
    }

    public PlateauCatalog? Catalog
    {
        get => catalog;
        private set => SetField(ref catalog, value);
    }

    public string? SelectedPrefecture
    {
        get => selectedPrefecture;
        set
        {
            if (SetField(ref selectedPrefecture, value))
            {
                RefreshAreas();
                if (SelectedArea is not null && !string.Equals(SelectedArea.Pref, selectedPrefecture, StringComparison.Ordinal))
                {
                    SelectedArea = null;
                }

                StartAreaPolygonLoad();
            }
        }
    }

    public PlateauAreaOption? SelectedArea
    {
        get => selectedArea;
        set
        {
            if (SetField(ref selectedArea, value))
            {
                RebuildAreasGeoJson();
                UpdateMapFocusForSelection();
                SyncSelectedSearchOptionToArea();
            }
        }
    }

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetField(ref searchText, value ?? string.Empty))
            {
                RefreshSearchResults();
            }
        }
    }

    public AreaSearchOption? SelectedSearchOption
    {
        get => selectedSearchOption;
        set
        {
            if (SetField(ref selectedSearchOption, value))
            {
                if (value is not null)
                {
                    if (!string.Equals(SelectedPrefecture, value.PrefectureJapaneseName, StringComparison.Ordinal))
                    {
                        SelectedPrefecture = value.PrefectureJapaneseName;
                    }
                    SelectedArea = value.Area;
                }
            }
        }
    }

    public MapFocus? PendingMapFocus
    {
        get => pendingMapFocus;
        private set => SetField(ref pendingMapFocus, value);
    }

    public string? AreasGeoJson
    {
        get => areasGeoJson;
        private set => SetField(ref areasGeoJson, value);
    }

    public PlateauOnlineGeometryMode GeometryMode
    {
        get => geometryMode;
        set => SetField(ref geometryMode, value);
    }

    public double ProgressFraction
    {
        get => progressFraction;
        private set => SetField(ref progressFraction, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public bool DracoAvailable
    {
        get => dracoAvailable;
        private set => SetField(ref dracoAvailable, value);
    }

    public bool CanStartImport => Catalog is not null && SelectedArea is not null && DracoAvailable && Phase is PlateauOnlineImportPhase.PickingArea or PlateauOnlineImportPhase.Done;

    public bool HasWarnings => Warnings.Count > 0;

    public async Task LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        Phase = PlateauOnlineImportPhase.LoadingCatalog;
        StatusMessage = "Loading PLATEAU catalog…";
        try
        {
            PlateauCatalog c = await apiClient.FetchCatalogAsync(cancellationToken);
            Catalog = c;
            allSearchOptions.Clear();
            foreach (PlateauAreaOption area in c.AreaOptions)
            {
                allSearchOptions.Add(BuildSearchOption(area));
            }
            allSearchOptions.Sort((a, b) => string.CompareOrdinal(a.DisplayLabel, b.DisplayLabel));
            RefreshSearchResults();
            Phase = PlateauOnlineImportPhase.PickingArea;
            StatusMessage = $"Catalog loaded. {c.Datasets.Count} 3D Tiles datasets across {c.AreaOptions.Count} areas.";
        }
        catch (Exception ex)
        {
            Phase = PlateauOnlineImportPhase.Failed;
            StatusMessage = $"Failed to load catalog: {ex.Message}";
        }
    }

    public async Task<bool> TryAutoDetectAreaAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        if (Catalog is null) return false;
        string? code = await apiClient.ReverseGeocodeMunicipalityCodeAsync(latitude, longitude, cancellationToken);
        if (code is null) return false;
        PlateauAreaOption? match = Catalog.AreaOptions.FirstOrDefault(a => a.MatchesCode(code));
        if (match is null) return false;
        SelectedPrefecture = match.Pref;
        SelectedArea = match;
        return true;
    }

    public bool SelectArea(string code)
    {
        if (Catalog is null || string.IsNullOrWhiteSpace(code)) return false;
        PlateauAreaOption? match = Catalog.AreaOptions.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.Ordinal));
        if (match is null) return false;

        if (!string.Equals(SelectedPrefecture, match.Pref, StringComparison.Ordinal))
        {
            SelectedPrefecture = match.Pref;
        }

        SelectedArea = match;
        return true;
    }

    public void CancelAreaPolygonLoading()
    {
        areaLoadCts?.Cancel();
        areaLoadCts = null;
    }

    public PlateauDatasetSelector BuildSelectorForCurrentMode()
    {
        PlateauDatasetSelector selector = new PlateauDatasetSelector
        {
            TexturePreference = GeometryMode == PlateauOnlineGeometryMode.Lod2Untextured
                ? PlateauTexturePreference.PreferUntextured
                : PlateauTexturePreference.PreferTextured
        };
        return selector;
    }

    public async Task<PlateauTilesetModel?> DownloadAsync(
        EcefToProjectTransformer ecefTransformer,
        IDracoMeshDecoder dracoDecoder,
        IProgress<PlateauTilesetDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (Catalog is null || SelectedArea is null) return null;

        CancelAreaPolygonLoading();
        Phase = PlateauOnlineImportPhase.Downloading;
        Warnings.Clear();
        PlateauDatasetSelector selector = BuildSelectorForCurrentMode();
        IReadOnlyList<PlateauDatasetEntry> picks = selector.SelectByTypes(Catalog, SelectedArea, new List<string> { "bldg" });
        PlateauDatasetEntry? bldgEntry = picks.FirstOrDefault(p => p.TypeEn == "bldg");

        if (bldgEntry is null)
        {
            Warnings.Add($"No bldg dataset found for area {SelectedArea.Label}.");
            Phase = PlateauOnlineImportPhase.Failed;
            return null;
        }

        GltfMeshDecoder meshDecoder = new GltfMeshDecoder(dracoDecoder);
        PlateauTilesetCache cache = new PlateauTilesetCache();
        Progress<PlateauTilesetDownloadProgress> uiProgress = new Progress<PlateauTilesetDownloadProgress>(p =>
        {
            ProgressFraction = p.Fraction;
            StatusMessage = $"Downloading {p.Completed}/{p.Total}: {p.CurrentItem}";
            progress?.Report(p);
        });

        PlateauTilesetModel buildings;
        if (bldgEntry.PreferredUrl is string bldgUrl && cachedBuildingsByUrl.TryGetValue(bldgUrl, out PlateauTilesetModel? cachedBuildings) && cachedBuildings is not null)
        {
            StatusMessage = $"Using cached buildings for {SelectedArea.Label}…";
            ProgressFraction = 1.0;
            buildings = cachedBuildings;
        }
        else
        {
            PlateauTilesetDownloader downloader = downloaderFactory();
            StatusMessage = $"Downloading buildings for {SelectedArea.Label}…";
            buildings = await downloader.DownloadAsync(bldgEntry, SelectedArea.Code, uiProgress, cancellationToken);
            foreach (string w in downloader.Warnings) Warnings.Add(w);
            if (bldgEntry.PreferredUrl is string newBldgUrl)
            {
                cachedBuildingsByUrl[newBldgUrl] = buildings;
            }
        }

        return buildings;
    }

    private void RefreshAreas()
    {
        AreasInPrefecture.Clear();
        if (Catalog is null) return;
        foreach (PlateauAreaOption area in Catalog.AreaOptions
            .Where(a => a.Pref == SelectedPrefecture)
            .OrderBy(a => a.Label))
        {
            AreasInPrefecture.Add(area);
        }
    }

    private void RefreshSearchResults()
    {
        FilteredSearchResults.Clear();
        foreach (AreaSearchOption option in FilterSearchOptions(allSearchOptions, SearchText))
        {
            FilteredSearchResults.Add(option);
        }
    }

    private void SyncSelectedSearchOptionToArea()
    {
        if (SelectedArea is null)
        {
            if (selectedSearchOption is not null)
            {
                SetField(ref selectedSearchOption, null, nameof(SelectedSearchOption));
            }
            return;
        }

        if (selectedSearchOption is not null && ReferenceEquals(selectedSearchOption.Area, SelectedArea))
        {
            return;
        }

        AreaSearchOption? match = allSearchOptions.Find(o =>
            string.Equals(o.Area.Code, SelectedArea.Code, StringComparison.Ordinal));
        if (match is null) return;
        SetField(ref selectedSearchOption, match, nameof(SelectedSearchOption));
    }

    internal static IEnumerable<AreaSearchOption> FilterSearchOptions(IReadOnlyList<AreaSearchOption> all, string? query)
    {
        if (all is null) return Array.Empty<AreaSearchOption>();
        string trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0) return Array.Empty<AreaSearchOption>();

        string[] tokens = trimmed
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();
        if (tokens.Length == 0) return Array.Empty<AreaSearchOption>();

        return all
            .Where(option =>
            {
                string haystack = option.SearchTokens;
                foreach (string token in tokens)
                {
                    if (haystack.IndexOf(token, StringComparison.Ordinal) < 0) return false;
                }
                return true;
            })
            .OrderBy(o => o.DisplayLabel, StringComparer.Ordinal);
    }

    internal static AreaSearchOption BuildSearchOption(PlateauAreaOption area)
    {
        string japanesePref = area.Pref ?? string.Empty;
        string? englishPref = JapanPrefectureNames.GetEnglishName(japanesePref);
        string japaneseLocal = !string.IsNullOrWhiteSpace(area.Ward)
            ? area.Ward!
            : !string.IsNullOrWhiteSpace(area.City) ? area.City! : (area.Label ?? area.Code);

        // Look up the romaji for this PLATEAU area code (5-digit JIS X 0402).
        string? romajiLiteral = null;
        if (MunicipalityRomajiNames.TryGet(area.Code, out string romajiHit)) romajiLiteral = romajiHit;
        string? romajiSimplified = romajiLiteral is null ? null : SimplifyRomaji(romajiLiteral);
        string? romajiPretty = romajiSimplified is null ? null : FormatRomajiForDisplay(romajiSimplified);

        // Format both sides as "{Japanese} ({English})" for visual symmetry, with the
        // English half omitted when no mapping exists.
        string prefDisplay = englishPref is null ? japanesePref : $"{japanesePref} ({englishPref})";
        string localDisplay = romajiPretty is null ? japaneseLocal : $"{japaneseLocal} ({romajiPretty})";

        string displayLabel = string.IsNullOrEmpty(prefDisplay)
            ? localDisplay
            : !string.IsNullOrEmpty(localDisplay)
                ? $"{prefDisplay} → {localDisplay}"
                : prefDisplay;

        // SearchTokens is pre-lowercased and concatenates everything we want to match
        // against. Using a non-space separator keeps multi-token matching predictable.
        string searchTokens = string.Join("",
            new[]
            {
                area.Pref ?? string.Empty,
                englishPref ?? string.Empty,
                area.City ?? string.Empty,
                area.Ward ?? string.Empty,
                area.Label ?? string.Empty,
                area.Code ?? string.Empty,
                romajiLiteral ?? string.Empty,
                romajiSimplified ?? string.Empty,
            }).ToLowerInvariant();

        return new AreaSearchOption(
            prefectureJapaneseName: area.Pref ?? string.Empty,
            area: area,
            displayLabel: displayLabel,
            codeLabel: area.Code,
            searchTokens: searchTokens);
    }

    private static readonly string[] RomajiMunicipalSuffixes =
    {
        "machi", "chou", "cho", "mura", "shi", "ku", "son", "gun",
    };

    /// <summary>
    /// Drops long-vowel doubles ("ou", "oo", "uu") which the literal kana spells
    /// but modern Hepburn-romanised place names elide. Lets the user type "Tokyo" /
    /// "Osaka" / "Kyoto" and hit entries whose literal romaji is "toukyou" /
    /// "oosaka" / "kyouto".
    /// </summary>
    internal static string SimplifyRomaji(string romaji)
    {
        if (string.IsNullOrEmpty(romaji)) return romaji;
        return romaji
            .Replace("oo", "o")
            .Replace("ou", "o")
            .Replace("uu", "u");
    }

    /// <summary>
    /// Turns a lowercase romaji string like "shinjukuku" into "Shinjuku-ku" by
    /// finding the longest matching municipal suffix and hyphenating it off.
    /// </summary>
    internal static string FormatRomajiForDisplay(string romaji)
    {
        if (string.IsNullOrEmpty(romaji)) return romaji;
        foreach (string suffix in RomajiMunicipalSuffixes)
        {
            if (romaji.Length > suffix.Length && romaji.EndsWith(suffix, StringComparison.Ordinal))
            {
                string root = romaji.Substring(0, romaji.Length - suffix.Length);
                return CapitalizeFirst(root) + "-" + suffix;
            }
        }
        return CapitalizeFirst(romaji);
    }

    private static string CapitalizeFirst(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    private void StartAreaPolygonLoad()
    {
        CancelAreaPolygonLoading();
        areaBoundsByCode.Clear();
        AreasGeoJson = null;

        if (Catalog is null || string.IsNullOrWhiteSpace(SelectedPrefecture) || AreasInPrefecture.Count == 0)
        {
            return;
        }

        areaLoadCts = new CancellationTokenSource();
        _ = LoadAreaPolygonsAsync(areaLoadCts.Token);
    }

    private async Task LoadAreaPolygonsAsync(CancellationToken cancellationToken)
    {
        PlateauCatalog? currentCatalog = Catalog;
        if (currentCatalog is null)
        {
            return;
        }

        string? prefecture = SelectedPrefecture;
        List<PlateauAreaOption> areas = AreasInPrefecture.ToList();
        if (areas.Count == 0)
        {
            return;
        }

        StatusMessage = $"Loading PLATEAU coverage for {prefecture}…";
        SemaphoreSlim semaphore = new SemaphoreSlim(4);
        List<Task<AreaBoundsResult>> tasks = areas
            .Select(area => LoadAreaBoundsResultAsync(area, currentCatalog, semaphore, cancellationToken))
            .ToList();

        int failureCount = 0;
        string? firstFailureMessage = null;
        try
        {
            while (tasks.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task<AreaBoundsResult> finished = await Task.WhenAny(tasks);
                tasks.Remove(finished);

                AreaBoundsResult result = await finished;
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!string.Equals(prefecture, SelectedPrefecture, StringComparison.Ordinal))
                {
                    return;
                }

                if (result.Bounds is not null)
                {
                    areaBoundsByCode[result.Area.Code] = result.Bounds;
                    RebuildAreasGeoJson();
                    if (string.Equals(SelectedArea?.Code, result.Area.Code, StringComparison.Ordinal))
                    {
                        UpdateMapFocusForSelection();
                    }
                }

                if (result.Error is not null)
                {
                    failureCount++;
                    firstFailureMessage ??= result.Error.Message;
                }
            }

            StatusMessage = failureCount == 0
                ? $"PLATEAU coverage loaded for {prefecture}. {areaBoundsByCode.Count}/{areas.Count} areas available on the map."
                : $"PLATEAU coverage loaded for {prefecture}. {areaBoundsByCode.Count}/{areas.Count} areas available on the map; {failureCount} request(s) failed. First error: {firstFailureMessage}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load PLATEAU coverage: {ex.Message}";
        }
    }

    private async Task<AreaBoundsResult> LoadAreaBoundsResultAsync(
        PlateauAreaOption area,
        PlateauCatalog currentCatalog,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            PlateauAreaBounds? bounds = await geometryService.GetBoundsAsync(area, currentCatalog, cancellationToken);
            return new AreaBoundsResult(area, bounds, error: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AreaBoundsResult(area, bounds: null, ex);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private void RebuildAreasGeoJson()
    {
        if (areaBoundsByCode.Count == 0)
        {
            AreasGeoJson = null;
            return;
        }

        var boundedAreas = AreasInPrefecture
            .Select(area => areaBoundsByCode.TryGetValue(area.Code, out PlateauAreaBounds? bounds)
                ? (Area: area, Bounds: bounds)
                : (Area: null, Bounds: null))
            .Where(pair => pair.Area is not null && pair.Bounds is not null)
            .Select(pair => (pair.Area!, pair.Bounds!));

        AreasGeoJson = PlateauAreaMapOverlayBuilder.Build(boundedAreas, SelectedArea?.Code);
    }

    private void UpdateMapFocusForSelection()
    {
        if (SelectedArea is null)
        {
            PendingMapFocus = null;
            return;
        }
        if (!areaBoundsByCode.TryGetValue(SelectedArea.Code, out PlateauAreaBounds? bounds) || bounds is null)
        {
            // Bounds for this area haven't loaded yet; defer until LoadAreaPolygonsAsync
            // reaches this area's result.
            PendingMapFocus = null;
            return;
        }
        double centerLat = (bounds.SouthDeg + bounds.NorthDeg) / 2.0;
        double centerLon = (bounds.WestDeg + bounds.EastDeg) / 2.0;
        PendingMapFocus = new MapFocus(centerLat, centerLon, PickZoomForBounds(bounds));
    }

    internal static int PickZoomForBounds(PlateauAreaBounds bounds)
    {
        double widthDeg = Math.Abs(bounds.EastDeg - bounds.WestDeg);
        double heightDeg = Math.Abs(bounds.NorthDeg - bounds.SouthDeg);
        double maxDeg = Math.Max(widthDeg, heightDeg);
        if (maxDeg > 1.0) return 9;
        if (maxDeg > 0.5) return 10;
        if (maxDeg > 0.25) return 11;
        if (maxDeg > 0.1) return 12;
        if (maxDeg > 0.05) return 13;
        return 14;
    }

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private sealed class AreaBoundsResult
    {
        public AreaBoundsResult(PlateauAreaOption area, PlateauAreaBounds? bounds, Exception? error)
        {
            Area = area;
            Bounds = bounds;
            Error = error;
        }

        public PlateauAreaOption Area { get; }

        public PlateauAreaBounds? Bounds { get; }

        public Exception? Error { get; }
    }
}

public sealed class MapFocus
{
    public MapFocus(double latitude, double longitude, int zoom)
    {
        Latitude = latitude;
        Longitude = longitude;
        Zoom = zoom;
    }

    public double Latitude { get; }
    public double Longitude { get; }
    public int Zoom { get; }
}
