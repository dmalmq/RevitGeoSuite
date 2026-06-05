<#
.SYNOPSIS
    Verifies that generated TypeScript contracts match the committed file.
.DESCRIPTION
    Runs the contracts generator and compares output against the committed
    contracts.generated.ts. Fails with exit code 1 if there's a diff.
    Use this in CI to catch silent contract drift.
#>

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$generatorDir = Join-Path $repoRoot 'src\RevitGeoSuite.SharedUI.Web.Contracts.Generator'
$contractsAssembly = Join-Path $repoRoot 'bin\Deploy\RevitGeoSuite.SharedUI.Web.Contracts.dll'
$committedFile = Join-Path $repoRoot 'src\RevitGeoSuite.SharedUI.Web\src\lib\bridge\contracts.generated.ts'
$tempFile = Join-Path $env:TEMP "contracts.generated.$(Get-Random).ts"

try {
    Write-Host "Building contracts assembly..." -ForegroundColor Cyan
    & dotnet build (Join-Path $repoRoot 'src\RevitGeoSuite.SharedUI.Web.Contracts\RevitGeoSuite.SharedUI.Web.Contracts.csproj') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Failed to build contracts assembly" }

    Write-Host "Generating contracts to temp file..." -ForegroundColor Cyan
    & dotnet run --project $generatorDir --no-build -- $contractsAssembly $tempFile
    if ($LASTEXITCODE -ne 0) { throw "Failed to generate contracts" }

    if (-not (Test-Path $committedFile)) {
        Write-Host "ERROR: Committed contracts file not found at: $committedFile" -ForegroundColor Red
        Write-Host "Run the build to generate it, then commit it." -ForegroundColor Yellow
        exit 1
    }

    $committed = Get-Content $committedFile -Raw
    $generated = Get-Content $tempFile -Raw

    if ($committed -eq $generated) {
        Write-Host "OK: contracts.generated.ts is up to date" -ForegroundColor Green
        exit 0
    } else {
        Write-Host "ERROR: contracts.generated.ts is out of date" -ForegroundColor Red
        Write-Host ""
        Write-Host "Run 'dotnet build' to regenerate, then commit the updated file." -ForegroundColor Yellow
        Write-Host ""

        $diff = & git diff --no-index $committedFile $tempFile 2>&1
        Write-Host $diff
        exit 1
    }
} finally {
    if (Test-Path $tempFile) {
        Remove-Item $tempFile -Force
    }
}
