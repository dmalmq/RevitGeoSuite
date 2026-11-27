<#
.SYNOPSIS
    Builds, verifies, and packages the Kanizu Revit Starter Kit into versioned .zip release archives
    for Revit 2024 and Revit 2027.
.DESCRIPTION
    1. Validates declarative specifications under standards/starter-kit/ (kit.json, rte.json,
       materials-3d.json, and families/*.family.json), asserting canonical "GIS Export" template
       and zero OST_Levels schedules.
    2. Invokes the Revit Starter Kit builder (StarterKitBuildCommand) into a clean staging
       directory or consumes pre-built artifacts when -SkipRevitLaunch and -ArtifactsDir are
       supplied (requiring per-year subdirectories when packaging multiple Revit years).
    3. Verifies manifest.json and manifest-diff.json parity (revitYear, kitVersion, rfaCount,
       diffCount == 0, rfaLintErrorCount == 0, zeroIdentityVerified, zeroWorksetsVerified,
       fileSizeBudgetPassed), and .rte/.rfa file-size budgets.
    4. Writes culture-invariant ordinal-sorted SHA256SUMS.txt and creates versioned zip archive(s):
       Kanizu-Revit-StarterKit-v<kitVersion>-Revit<year>.zip.
.EXAMPLE
    pwsh scripts/Build-StarterKit.ps1 -RevitYears 2024,2027 -SkipRevitLaunch -ArtifactsDir ./staging
#>
param(
    [ValidateSet("2024", "2027")]
    [string[]]$RevitYears = @("2024", "2027"),

    [string]$SpecDir = "",

    [string]$StandardsDir = "",

    [string]$OutputDir = "",

    [string]$ArtifactsDir = "",

    [switch]$SkipRevitLaunch,

    [string]$RevitExePath = "",

    [string]$JournalPath = "",

    [string]$FamilyTemplatesDir = "",

    [string]$ProjectTemplatesDir = "",

    [string]$ProjectSeedTemplatePath = "",

    [string]$SharedParametersFilePath = "",

    [switch]$ExcludeOptionalFamilies,

    [switch]$PreferJapaneseSeedTemplates,

    [ValidateSet("WithinBudget", "Exact")]
    [string]$TriangleComparisonMode = "WithinBudget"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot = Split-Path -Parent $scriptDir

if ([string]::IsNullOrWhiteSpace($SpecDir)) {
    $SpecDir = Join-Path $repoRoot "standards/starter-kit"
}
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "artifacts/starter-kit"
}

$specDirFull = [System.IO.Path]::GetFullPath($SpecDir)
$outputDirFull = [System.IO.Path]::GetFullPath($OutputDir)

function Resolve-StandardsDirectory {
    param(
        [string]$ExplicitStandardsDir,
        [Parameter(Mandatory = $true)][string]$KitSpecDirectory,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitStandardsDir)) {
        $fullExplicit = [System.IO.Path]::GetFullPath($ExplicitStandardsDir)
        $marker = Join-Path $fullExplicit "KanizuGIS.SharedParameters.txt"
        if (-not (Test-Path -LiteralPath $marker)) {
            throw "Configured -StandardsDir '$fullExplicit' does not contain KanizuGIS.SharedParameters.txt."
        }
        return $fullExplicit
    }

    # Walk up from SpecDir looking for KanizuGIS.SharedParameters.txt
    $current = Split-Path -Parent $KitSpecDirectory
    for ($depth = 0; $depth -lt 4 -and -not [string]::IsNullOrWhiteSpace($current); $depth++) {
        $directMarker = Join-Path $current "KanizuGIS.SharedParameters.txt"
        if (Test-Path -LiteralPath $directMarker) {
            return $current
        }
        $standardsChild = Join-Path $current "standards"
        if (Test-Path -LiteralPath (Join-Path $standardsChild "KanizuGIS.SharedParameters.txt")) {
            return $standardsChild
        }
        $parent = Split-Path -Parent $current
        if ($parent -eq $current) {
            break
        }
        $current = $parent
    }

    $repoStandards = Join-Path $RepositoryRoot "standards"
    if (Test-Path -LiteralPath (Join-Path $repoStandards "KanizuGIS.SharedParameters.txt")) {
        return $repoStandards
    }

    return ""
}

function Sort-StringsOrdinal {
    param([Parameter(Mandatory = $true)][string[]]$Items)

    $copy = [string[]]@($Items)
    [System.Array]::Sort($copy, [System.StringComparer]::Ordinal)
    return ,$copy
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required JSON file was not found: $Path"
    }

    $raw = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    return $raw | ConvertFrom-Json
}

function Write-Utf8NoBomLf {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $normalized = $Content.Replace("`r`n", "`n")
    $encoding = New-Object System.Text.UTF8Encoding($false)
    $parent = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $normalized, $encoding)
}

function Get-FileSha256Lower {
    param([Parameter(Mandatory = $true)][string]$Path)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            $bytes = $sha.ComputeHash($stream)
            return ([System.BitConverter]::ToString($bytes).Replace("-", "").ToLowerInvariant())
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $sha.Dispose()
    }
}

function Test-StarterKitSpecs {
    param([Parameter(Mandatory = $true)][string]$KitSpecDirectory)

    $kitPath = Join-Path $KitSpecDirectory "kit.json"
    $kit = Read-JsonFile -Path $kitPath

    if ($kit.schema -ne "kanizu.starter-kit") {
        throw "Invalid kit.json schema '$($kit.schema)'. Expected 'kanizu.starter-kit'."
    }
    if ($kit.kitVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') {
        throw "Invalid kit.json kitVersion '$($kit.kitVersion)'."
    }

    $rtePath = Join-Path $KitSpecDirectory $kit.rteSpec
    $materialsPath = Join-Path $KitSpecDirectory $kit.materialsSpec
    $rte = Read-JsonFile -Path $rtePath
    $materials = Read-JsonFile -Path $materialsPath

    if ($rte.schema -ne "kanizu.starter-kit.rte") {
        throw "Invalid rte.json schema '$($rte.schema)'."
    }
    if ($rte.canonicalExportViewTemplate -ne "GIS Export") {
        throw "Canonical 2D export view template must be 'GIS Export', found '$($rte.canonicalExportViewTemplate)'."
    }
    foreach ($sched in $rte.schedules) {
        if ($sched.category -eq "OST_Levels") {
            throw "Schedule '$($sched.name)' targets forbidden unschedulable category 'OST_Levels'."
        }
    }
    foreach ($filter in $rte.qaViewFilters) {
        if (-not $filter.visible) {
            throw "QA filter '$($filter.name)' must keep visible=true (KZ-VIEW-004)."
        }
    }
    if ($materials.schema -ne "kanizu.starter-kit.materials-3d") {
        throw "Invalid materials-3d.json schema '$($materials.schema)'."
    }

    $selectedFamilies = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $kit.families) {
        if ($ExcludeOptionalFamilies -and $entry.optional) {
            continue
        }
        $famPath = Join-Path $KitSpecDirectory $entry.specPath
        if (-not (Test-Path -LiteralPath $famPath)) {
            throw "Missing family spec '$famPath' for '$($entry.id)'."
        }
        $fam = Read-JsonFile -Path $famPath
        if ($fam.schema -ne "kanizu.starter-kit.family") {
            throw "Invalid family spec schema '$($fam.schema)' in '$famPath'."
        }
        $selectedFamilies.Add($fam) | Out-Null
    }

    return [PSCustomObject]@{
        Kit       = $kit
        Rte       = $rte
        Materials = $materials
        Families  = $selectedFamilies.ToArray()
    }
}

function Resolve-DefaultRevitExe {
    param([Parameter(Mandatory = $true)][string]$Year)

    if (-not [string]::IsNullOrWhiteSpace($RevitExePath)) {
        return $RevitExePath
    }

    $programFiles = $env:ProgramFiles
    if ([string]::IsNullOrWhiteSpace($programFiles)) {
        return ""
    }

    return Join-Path (Join-Path (Join-Path $programFiles "Autodesk") "Revit $Year") "Revit.exe"
}

function Resolve-DefaultTemplatesRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Year,
        [Parameter(Mandatory = $true)][string]$SubFolder
    )

    $programData = $env:ProgramData
    if ([string]::IsNullOrWhiteSpace($programData)) {
        return ""
    }

    return Join-Path (Join-Path (Join-Path $programData "Autodesk") "RVT $Year") $SubFolder
}

function Invoke-RevitStarterKitBuild {
    param(
        [Parameter(Mandatory = $true)][string]$Year,
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][string]$ResolvedStandardsDir
    )

    $revitExe = Resolve-DefaultRevitExe -Year $Year
    if ([string]::IsNullOrWhiteSpace($revitExe) -or -not (Test-Path -LiteralPath $revitExe)) {
        throw "Revit $Year executable not found at '$revitExe'. Pass -RevitExePath or use -SkipRevitLaunch -ArtifactsDir for pre-built artifact staging."
    }

    $famTplDir = if (-not [string]::IsNullOrWhiteSpace($FamilyTemplatesDir)) {
        $FamilyTemplatesDir
    } else {
        Resolve-DefaultTemplatesRoot -Year $Year -SubFolder "Family Templates"
    }

    $projTplDir = if (-not [string]::IsNullOrWhiteSpace($ProjectTemplatesDir)) {
        $ProjectTemplatesDir
    } else {
        Resolve-DefaultTemplatesRoot -Year $Year -SubFolder "Templates"
    }

    # Always clean the staging directory before launching Revit so stale passing artifacts from
    # an earlier build can never mask a silent Revit no-op or aborted command.
    if (Test-Path -LiteralPath $StagingDirectory) {
        Remove-Item -LiteralPath $StagingDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $StagingDirectory -Force | Out-Null

    $configPath = Join-Path $outputDirFull "starter-kit-build-config-Revit$Year.json"

    $configObj = [ordered]@{
        revitYear                     = $Year
        starterKitSpecDirectory       = $specDirFull
        standardsDirectory            = $ResolvedStandardsDir
        familySeedTemplatesDirectory  = $famTplDir
        projectSeedTemplatePath       = $ProjectSeedTemplatePath
        projectSeedTemplatesDirectory = $projTplDir
        outputDirectory               = $StagingDirectory
        sharedParametersFilePath      = $SharedParametersFilePath
        includeOptionalFamilies       = (-not $ExcludeOptionalFamilies)
        overwriteExisting             = $true
        preferJapaneseSeedTemplates   = [bool]$PreferJapaneseSeedTemplates
        triangleComparisonMode        = $TriangleComparisonMode
    }

    Write-Utf8NoBomLf -Path $configPath -Content ($configObj | ConvertTo-Json -Depth 6)

    $previousConfigEnv = $env:KANIZU_STARTER_KIT_BUILD_CONFIG
    try {
        $env:KANIZU_STARTER_KIT_BUILD_CONFIG = $configPath
        if (-not [string]::IsNullOrWhiteSpace($JournalPath)) {
            $journalFull = [System.IO.Path]::GetFullPath($JournalPath)
            if (-not (Test-Path -LiteralPath $journalFull)) {
                throw "Configured Revit journal file was not found at '$journalFull'."
            }
            Write-Host "Launching Revit $Year Starter Kit journal build (journal: $journalFull, config: $configPath)..." -ForegroundColor Cyan
            $proc = Start-Process -FilePath $revitExe -ArgumentList @("`"$journalFull`"") -PassThru -Wait
        }
        else {
            Write-Host "Launching Revit $Year Starter Kit build (config: $configPath). Invoke StarterKitBuildCommand or pass -JournalPath for unattended execution." -ForegroundColor Cyan
            $proc = Start-Process -FilePath $revitExe -PassThru -Wait
        }
        if ($proc.ExitCode -ne 0) {
            throw "Revit $Year process exited with non-zero code $($proc.ExitCode)."
        }
    }
    finally {
        $env:KANIZU_STARTER_KIT_BUILD_CONFIG = $previousConfigEnv
    }
}

function Copy-PrebuiltArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$Year,
        [Parameter(Mandatory = $true)][string]$SourceBase,
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][bool]$RequirePerYearSubdirectory
    )

    if ([string]::IsNullOrWhiteSpace($SourceBase)) {
        throw "-ArtifactsDir is required when -SkipRevitLaunch is set."
    }

    $sourceFull = [System.IO.Path]::GetFullPath($SourceBase)
    $perYearDir = Join-Path $sourceFull $Year

    if (Test-Path -LiteralPath $perYearDir) {
        $effectiveSource = $perYearDir
    }
    elseif ($RequirePerYearSubdirectory) {
        throw "Multi-year packaging requires per-year artifact subdirectory '$perYearDir'. Refusing to reuse flat '$sourceFull' across multiple Revit years."
    }
    else {
        $effectiveSource = $sourceFull
    }

    if (-not (Test-Path -LiteralPath $effectiveSource)) {
        throw "Pre-built artifacts directory was not found at '$effectiveSource'."
    }

    if (Test-Path -LiteralPath $StagingDirectory) {
        Remove-Item -LiteralPath $StagingDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $StagingDirectory -Force | Out-Null

    foreach ($file in Get-ChildItem -LiteralPath $effectiveSource -Recurse -File) {
        $rel = $file.FullName.Substring($effectiveSource.Length).TrimStart("\", "/")
        $dest = Join-Path $StagingDirectory $rel
        $destParent = Split-Path -Parent $dest
        if (-not (Test-Path -LiteralPath $destParent)) {
            New-Item -ItemType Directory -Path $destParent -Force | Out-Null
        }
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
    }
}

function Ensure-BilingualDocs {
    param(
        [Parameter(Mandatory = $true)]$SpecBundle,
        [Parameter(Mandatory = $true)][string]$Year,
        [Parameter(Mandatory = $true)][string]$StagingDirectory
    )

    $kit = $SpecBundle.Kit
    $rte = $SpecBundle.Rte
    $readmePath = Join-Path $StagingDirectory "README.md"
    $startHerePath = Join-Path $StagingDirectory "START_HERE.txt"

    if (-not (Test-Path -LiteralPath $readmePath)) {
        $readme = @(
            "# Kanizu Revit Starter Kit v$($kit.kitVersion) (Revit $Year)",
            "",
            "Kanizu GIS Standard v$($kit.gisStandardVersion) — Metric Station Authoring Template & Lightweight Family Library",
            "Kanizu GIS 標準 v$($kit.gisStandardVersion) — メートル単位駅モデル作成用テンプレート＆軽量ファミリライブラリ",
            "",
            "## Contents / 収録内容",
            "- ``$($rte.templateName).rte`` — Zero-identity starter project template",
            "- ``Families/*.rfa`` — $($SpecBundle.Families.Count) dual-LOD starter families",
            "- ``manifest.json`` — Canonical semantic build manifest",
            "- ``manifest-diff.json`` — Verification gate report (0 diffs, 0 lint errors)",
            "- ``SHA256SUMS.txt`` — SHA-256 checksums",
            "- ``START_HERE.txt`` — Bilingual EN/JA quick-start checklist"
        ) -join "`n"
        Write-Utf8NoBomLf -Path $readmePath -Content ($readme + "`n")
    }

    if (-not (Test-Path -LiteralPath $startHerePath)) {
        $startHere = @(
            "$($rte.startingView.titleEn)",
            "$($rte.startingView.titleJa)",
            "Starter Kit v$($kit.kitVersion) | Kanizu GIS Standard v$($kit.gisStandardVersion) | Revit $Year",
            "========================================================================",
            "1. Create a new project from $($rte.templateName).rte (never Save-As an existing .rvt).",
            "   本テンプレートから新規プロジェクトを作成してください（既存 .rvt の別名保存禁止）。",
            "2. Run Kanizu GIS setup to assign ModelId, EPSG CRS, and level metadata.",
            "   Kanizu GIS の初期設定で ModelId・EPSG 座標系・各階メタデータを設定してください。",
            "3. Confirm zero rows in the '✗' exception schedules before handover.",
            "   納品前に「✗」例外集計表が 0 件であることを確認してください。"
        ) -join "`n"
        Write-Utf8NoBomLf -Path $startHerePath -Content ($startHere + "`n")
    }
}

function Assert-StagedPackageValid {
    param(
        [Parameter(Mandatory = $true)]$SpecBundle,
        [Parameter(Mandatory = $true)][string]$Year,
        [Parameter(Mandatory = $true)][string]$StagingDirectory
    )

    $kit = $SpecBundle.Kit
    $rte = $SpecBundle.Rte
    $rteFileName = "$($rte.templateName).rte"
    $rtePath = Join-Path $StagingDirectory $rteFileName
    $manifestPath = Join-Path $StagingDirectory "manifest.json"
    $diffPath = Join-Path $StagingDirectory "manifest-diff.json"

    if (-not (Test-Path -LiteralPath $rtePath)) {
        throw "Missing required Starter Kit template '$rteFileName' in '$StagingDirectory'."
    }

    $maxRteBytes = [int64]$kit.budgets.maxRteFileSizeMb * 1024 * 1024
    $rteBytes = (Get-Item -LiteralPath $rtePath).Length
    if ($rteBytes -le 0 -or $rteBytes -gt $maxRteBytes) {
        throw "Template '$rteFileName' size ($rteBytes bytes) violates budget (1..$maxRteBytes bytes)."
    }

    $maxRfaBytes = [int64]$kit.budgets.maxRfaFileSizeKb * 1024
    foreach ($fam in $SpecBundle.Families) {
        $rfaRel = "Families/$($fam.familyName).rfa"
        $rfaPath = Join-Path $StagingDirectory $rfaRel
        if (-not (Test-Path -LiteralPath $rfaPath)) {
            throw "Missing required family artifact '$rfaRel' in '$StagingDirectory'."
        }
        $rfaBytes = (Get-Item -LiteralPath $rfaPath).Length
        if ($rfaBytes -le 0 -or $rfaBytes -gt $maxRfaBytes) {
            throw "Family '$rfaRel' size ($rfaBytes bytes) violates budget (1..$maxRfaBytes bytes)."
        }
    }

    $manifest = Read-JsonFile -Path $manifestPath
    if ($manifest.schema -ne "kanizu.starter-kit.manifest") {
        throw "Invalid manifest.json schema '$($manifest.schema)'."
    }
    if ($manifest.kitVersion -ne $kit.kitVersion) {
        throw "manifest.json kitVersion '$($manifest.kitVersion)' does not match kit.json '$($kit.kitVersion)'."
    }
    if ($manifest.canonicalExportViewTemplate -ne "GIS Export") {
        throw "manifest.json canonicalExportViewTemplate must be 'GIS Export', found '$($manifest.canonicalExportViewTemplate)'."
    }
    if ($manifest.hygiene.hasProjectIdentity) {
        throw "manifest.json reports hasProjectIdentity=true; Starter Kit .rte must carry zero project identity."
    }
    if ([int]$manifest.hygiene.worksetCount -ne 0) {
        throw "manifest.json reports worksetCount=$($manifest.hygiene.worksetCount); Starter Kit .rte must contain zero worksets."
    }
    foreach ($sched in $manifest.schedules) {
        if ($sched.category -eq "OST_Levels") {
            throw "manifest.json contains forbidden OST_Levels schedule '$($sched.name)'."
        }
    }

    $diffReport = Read-JsonFile -Path $diffPath
    if ($diffReport.schema -ne "kanizu.starter-kit.verification-report") {
        throw "Invalid manifest-diff.json schema '$($diffReport.schema)' in '$diffPath'."
    }
    if ("$($diffReport.revitYear)" -ne $Year) {
        throw "manifest-diff.json revitYear '$($diffReport.revitYear)' does not match target Revit year '$Year' ($diffPath)."
    }
    if ("$($diffReport.kitVersion)" -ne "$($kit.kitVersion)") {
        throw "manifest-diff.json kitVersion '$($diffReport.kitVersion)' does not match kit.json '$($kit.kitVersion)' ($diffPath)."
    }
    if ([int]$diffReport.rfaCount -ne $SpecBundle.Families.Count) {
        throw "manifest-diff.json rfaCount ($($diffReport.rfaCount)) does not match expected family count ($($SpecBundle.Families.Count)) in $diffPath."
    }
    if (-not [bool]$diffReport.zeroIdentityVerified) {
        throw "manifest-diff.json reports zeroIdentityVerified=false for Revit $Year ($diffPath)."
    }
    if (-not [bool]$diffReport.zeroWorksetsVerified) {
        throw "manifest-diff.json reports zeroWorksetsVerified=false for Revit $Year ($diffPath)."
    }
    if (-not [bool]$diffReport.fileSizeBudgetPassed) {
        throw "manifest-diff.json reports fileSizeBudgetPassed=false for Revit $Year ($diffPath)."
    }
    if ([int]$diffReport.diffCount -ne 0 -or (-not [bool]$diffReport.isMatch)) {
        throw "Manifest parity check failed for Revit $Year ($($diffReport.diffCount) diff(s) in $diffPath)."
    }
    if ([int]$diffReport.rfaLintErrorCount -ne 0) {
        throw "RfaLinter check failed for Revit $Year ($($diffReport.rfaLintErrorCount) error(s) in $diffPath)."
    }
    if (-not [bool]$diffReport.succeeded) {
        throw "Starter Kit verification report did not succeed for Revit $Year ($diffPath)."
    }
}

function Write-DeterministicZipPackage {
    param(
        [Parameter(Mandatory = $true)]$SpecBundle,
        [Parameter(Mandatory = $true)][string]$Year,
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][string]$ZipOutputPath
    )

    $rte = $SpecBundle.Rte
    $rteFileName = "$($rte.templateName).rte"

    $packageRelPaths = New-Object System.Collections.Generic.List[string]
    $packageRelPaths.Add($rteFileName) | Out-Null
    $packageRelPaths.Add("manifest.json") | Out-Null
    $packageRelPaths.Add("manifest-diff.json") | Out-Null
    $packageRelPaths.Add("README.md") | Out-Null
    $packageRelPaths.Add("START_HERE.txt") | Out-Null

    foreach ($fam in $SpecBundle.Families) {
        $packageRelPaths.Add("Families/$($fam.familyName).rfa") | Out-Null
    }

    $sortedChecksumPaths = Sort-StringsOrdinal -Items $packageRelPaths.ToArray()
    $checksumLines = New-Object System.Collections.Generic.List[string]
    foreach ($relPath in $sortedChecksumPaths) {
        $absPath = Join-Path $StagingDirectory $relPath
        $hash = Get-FileSha256Lower -Path $absPath
        $checksumLines.Add("$hash  $relPath") | Out-Null
    }

    $checksumPath = Join-Path $StagingDirectory "SHA256SUMS.txt"
    Write-Utf8NoBomLf -Path $checksumPath -Content (($checksumLines -join "`n") + "`n")

    $allArchiveEntries = Sort-StringsOrdinal -Items ($sortedChecksumPaths + @("SHA256SUMS.txt"))

    $zipParent = Split-Path -Parent $ZipOutputPath
    if (-not (Test-Path -LiteralPath $zipParent)) {
        New-Item -ItemType Directory -Path $zipParent -Force | Out-Null
    }
    if (Test-Path -LiteralPath $ZipOutputPath) {
        Remove-Item -LiteralPath $ZipOutputPath -Force
    }

    $fixedTimestamp = New-Object System.DateTimeOffset(2026, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
    $archive = [System.IO.Compression.ZipFile]::Open($ZipOutputPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relPath in $allArchiveEntries) {
            $absPath = Join-Path $StagingDirectory $relPath
            $entryName = $relPath.Replace("\", "/")
            $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedTimestamp
            $entryStream = $entry.Open()
            try {
                $fileStream = [System.IO.File]::OpenRead($absPath)
                try {
                    $fileStream.CopyTo($entryStream)
                }
                finally {
                    $fileStream.Dispose()
                }
            }
            finally {
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

Write-Host "Validating Kanizu Starter Kit specs in '$specDirFull'..." -ForegroundColor Cyan
$specBundle = Test-StarterKitSpecs -KitSpecDirectory $specDirFull
$kitVersion = $specBundle.Kit.kitVersion
$resolvedStandardsDir = Resolve-StandardsDirectory -ExplicitStandardsDir $StandardsDir -KitSpecDirectory $specDirFull -RepositoryRoot $repoRoot
Write-Host "Validated Starter Kit v$kitVersion ($($specBundle.Families.Count) families, canonical template '$($specBundle.Rte.canonicalExportViewTemplate)')." -ForegroundColor Green

New-Item -ItemType Directory -Path $outputDirFull -Force | Out-Null
$createdZips = New-Object System.Collections.Generic.List[string]
$requirePerYear = ($RevitYears.Count -gt 1)

foreach ($year in $RevitYears) {
    if (-not ($specBundle.Kit.supportedRevitVersions -contains $year)) {
        throw "Revit year '$year' is not listed in kit.json supportedRevitVersions."
    }

    $stagingDir = Join-Path $outputDirFull "staging-Revit$year"
    if ($SkipRevitLaunch) {
        Copy-PrebuiltArtifacts -Year $year -SourceBase $ArtifactsDir -StagingDirectory $stagingDir -RequirePerYearSubdirectory $requirePerYear
    }
    else {
        Invoke-RevitStarterKitBuild -Year $year -StagingDirectory $stagingDir -ResolvedStandardsDir $resolvedStandardsDir
    }

    Ensure-BilingualDocs -SpecBundle $specBundle -Year $year -StagingDirectory $stagingDir
    Assert-StagedPackageValid -SpecBundle $specBundle -Year $year -StagingDirectory $stagingDir

    $zipName = "Kanizu-Revit-StarterKit-v$kitVersion-Revit$year.zip"
    $zipPath = Join-Path $outputDirFull $zipName
    Write-DeterministicZipPackage -SpecBundle $specBundle -Year $year -StagingDirectory $stagingDir -ZipOutputPath $zipPath

    $zipSha = Get-FileSha256Lower -Path $zipPath
    Write-Host "Packaged $zipPath (sha256: $zipSha)" -ForegroundColor Green
    $createdZips.Add($zipPath) | Out-Null
}

Write-Host ""
Write-Host "Kanizu Revit Starter Kit v$kitVersion packaging complete ($($createdZips.Count) archive(s))." -ForegroundColor Green
