<#
.SYNOPSIS
    Generates a static C# dictionary of PLATEAU municipality codes -> romaji.

.DESCRIPTION
    Pulls the JIS X 0402 (zenkoku-chiho-koukyodantai-code) dataset from the
    public CC0 mirror at https://madefor.github.io/jisx0402/api/v1/all.json,
    converts each entry's katakana city name to Hepburn romaji, and writes
    src/RevitGeoSuite.PlateauImport/Online/MunicipalityRomajiNames.cs.

    The dataset uses 6-digit codes (5-digit JIS X 0402 + 1-digit check digit);
    we strip the check digit so the dictionary key matches PLATEAU's `city_code`
    / `ward_code` (5 digits).

    Run from anywhere; the script writes paths relative to its location.

.EXAMPLE
    pwsh -File tools/Generate-MunicipalityRomaji.ps1
#>

[CmdletBinding()]
param(
    [string]$DataUrl = "https://madefor.github.io/jisx0402/api/v1/all.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$repoRoot  = Split-Path -Parent $scriptDir
$outFile   = Join-Path $repoRoot "src/RevitGeoSuite.PlateauImport/Online/MunicipalityRomajiNames.cs"

# --- Katakana -> Hepburn romaji table ---------------------------------------
# Digraphs (e.g. キャ / シュ) must be matched BEFORE single-char fallbacks.
$digraphs = [ordered]@{
    'キャ'='kya'; 'キュ'='kyu'; 'キョ'='kyo'
    'ギャ'='gya'; 'ギュ'='gyu'; 'ギョ'='gyo'
    'シャ'='sha'; 'シュ'='shu'; 'ショ'='sho'
    'ジャ'='ja';  'ジュ'='ju';  'ジョ'='jo'
    'チャ'='cha'; 'チュ'='chu'; 'チョ'='cho'
    'ヂャ'='ja';  'ヂュ'='ju';  'ヂョ'='jo'
    'ニャ'='nya'; 'ニュ'='nyu'; 'ニョ'='nyo'
    'ヒャ'='hya'; 'ヒュ'='hyu'; 'ヒョ'='hyo'
    'ビャ'='bya'; 'ビュ'='byu'; 'ビョ'='byo'
    'ピャ'='pya'; 'ピュ'='pyu'; 'ピョ'='pyo'
    'ミャ'='mya'; 'ミュ'='myu'; 'ミョ'='myo'
    'リャ'='rya'; 'リュ'='ryu'; 'リョ'='ryo'
    'ファ'='fa';  'フィ'='fi';  'フェ'='fe';  'フォ'='fo'
    'ヴァ'='va';  'ヴィ'='vi';  'ヴェ'='ve';  'ヴォ'='vo'
    'ティ'='ti';  'ディ'='di';  'デュ'='du'
    'ウィ'='wi';  'ウェ'='we';  'ウォ'='wo'
}

$singles = @{
    'ア'='a';   'イ'='i';   'ウ'='u';   'エ'='e';   'オ'='o'
    'カ'='ka';  'キ'='ki';  'ク'='ku';  'ケ'='ke';  'コ'='ko'
    'サ'='sa';  'シ'='shi'; 'ス'='su';  'セ'='se';  'ソ'='so'
    'タ'='ta';  'チ'='chi'; 'ツ'='tsu'; 'テ'='te';  'ト'='to'
    'ナ'='na';  'ニ'='ni';  'ヌ'='nu';  'ネ'='ne';  'ノ'='no'
    'ハ'='ha';  'ヒ'='hi';  'フ'='fu';  'ヘ'='he';  'ホ'='ho'
    'マ'='ma';  'ミ'='mi';  'ム'='mu';  'メ'='me';  'モ'='mo'
    'ヤ'='ya';              'ユ'='yu';              'ヨ'='yo'
    'ラ'='ra';  'リ'='ri';  'ル'='ru';  'レ'='re';  'ロ'='ro'
    'ワ'='wa';  'ヰ'='i';   'ヱ'='e';   'ヲ'='wo';  'ン'='n'
    'ガ'='ga';  'ギ'='gi';  'グ'='gu';  'ゲ'='ge';  'ゴ'='go'
    'ザ'='za';  'ジ'='ji';  'ズ'='zu';  'ゼ'='ze';  'ゾ'='zo'
    'ダ'='da';  'ヂ'='ji';  'ヅ'='zu';  'デ'='de';  'ド'='do'
    'バ'='ba';  'ビ'='bi';  'ブ'='bu';  'ベ'='be';  'ボ'='bo'
    'パ'='pa';  'ピ'='pi';  'プ'='pu';  'ペ'='pe';  'ポ'='po'
    'ヴ'='vu'
}

function Convert-KatakanaToRomaji {
    param([Parameter(Mandatory)][string]$Kana)

    if ([string]::IsNullOrEmpty($Kana)) { return '' }

    $sb = [System.Text.StringBuilder]::new()
    $i = 0
    $chars = $Kana.ToCharArray()
    while ($i -lt $chars.Length) {
        $ch = $chars[$i]

        # Small tsu doubles the next consonant (or is silent at end of word).
        if ($ch -eq 'ッ') {
            if ($i + 1 -lt $chars.Length) {
                $nextRoma = $null
                if ($i + 2 -lt $chars.Length) {
                    $digraph = "$($chars[$i + 1])$($chars[$i + 2])"
                    if ($digraphs.Contains($digraph)) { $nextRoma = $digraphs[$digraph] }
                }
                if ($null -eq $nextRoma) {
                    $single = "$($chars[$i + 1])"
                    if ($singles.ContainsKey($single)) { $nextRoma = $singles[$single] }
                }
                if ($null -ne $nextRoma -and $nextRoma.Length -gt 0) {
                    $first = $nextRoma[0]
                    # Hepburn doubles 'tch' rather than 'cch' for ッチ.
                    if ($first -eq 'c') { [void]$sb.Append('t') }
                    else { [void]$sb.Append($first) }
                }
            }
            $i++
            continue
        }

        # Long vowel mark -> repeat the previous vowel.
        if ($ch -eq 'ー') {
            if ($sb.Length -gt 0) {
                $prev = $sb.ToString()[$sb.Length - 1]
                if ('aeiou' -contains [string]$prev) { [void]$sb.Append($prev) }
            }
            $i++
            continue
        }

        # Try two-char digraph first.
        if ($i + 1 -lt $chars.Length) {
            $digraph = "$ch$($chars[$i + 1])"
            if ($digraphs.Contains($digraph)) {
                [void]$sb.Append($digraphs[$digraph])
                $i += 2
                continue
            }
        }

        # Single char.
        $single = "$ch"
        if ($singles.ContainsKey($single)) {
            [void]$sb.Append($singles[$single])
            $i++
            continue
        }

        # Unknown char (latin / digit / punctuation) - pass through lowercased.
        [void]$sb.Append([char]::ToLowerInvariant($ch))
        $i++
    }

    return $sb.ToString()
}

# --- Fetch + parse ----------------------------------------------------------
Write-Host "Fetching $DataUrl ..." -ForegroundColor Cyan
$json = Invoke-WebRequest -Uri $DataUrl -UseBasicParsing | Select-Object -ExpandProperty Content
$data = $json | ConvertFrom-Json
$entries = $data.PSObject.Properties

# --- Build the romaji map ---------------------------------------------------
# Key: 5-digit PLATEAU code (strip check digit from 6-digit JIS code).
# Value: romaji of city_kana.
$romaji = [System.Collections.Generic.SortedDictionary[string,string]]::new()
$skipped = 0
foreach ($prop in $entries) {
    $jisCode = [string]$prop.Name
    if ($jisCode.Length -lt 6) { $skipped++; continue }
    $plateauCode = $jisCode.Substring(0, $jisCode.Length - 1)
    $kana = [string]$prop.Value.city_kana
    if ([string]::IsNullOrWhiteSpace($kana)) { $skipped++; continue }
    $r = Convert-KatakanaToRomaji $kana
    if ([string]::IsNullOrWhiteSpace($r)) { $skipped++; continue }
    $romaji[$plateauCode] = $r
}

Write-Host "Generated $($romaji.Count) entries (skipped $skipped)" -ForegroundColor Green

# --- Emit C# file -----------------------------------------------------------
$lines = [System.Collections.Generic.List[string]]::new()
[void]$lines.Add('// <auto-generated>')
[void]$lines.Add('// Generated by tools/Generate-MunicipalityRomaji.ps1 from')
[void]$lines.Add('// https://madefor.github.io/jisx0402/api/v1/all.json (CC0 1.0).')
[void]$lines.Add('// Do not edit by hand; re-run the script if the source updates.')
[void]$lines.Add('// </auto-generated>')
[void]$lines.Add('using System.Collections.Generic;')
[void]$lines.Add('')
[void]$lines.Add('namespace RevitGeoSuite.PlateauImport.Online;')
[void]$lines.Add('')
[void]$lines.Add('public static class MunicipalityRomajiNames')
[void]$lines.Add('{')
[void]$lines.Add('    public static bool TryGet(string areaCode, out string romaji)')
[void]$lines.Add('    {')
[void]$lines.Add('        return Romaji.TryGetValue(areaCode, out romaji!);')
[void]$lines.Add('    }')
[void]$lines.Add('')
[void]$lines.Add('    private static readonly IReadOnlyDictionary<string, string> Romaji = new Dictionary<string, string>')
[void]$lines.Add('    {')
foreach ($kvp in $romaji.GetEnumerator()) {
    [void]$lines.Add("        [`"$($kvp.Key)`"] = `"$($kvp.Value)`",")
}
[void]$lines.Add('    };')
[void]$lines.Add('}')

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $outFile)) | Out-Null
[System.IO.File]::WriteAllText($outFile, ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))

Write-Host "Wrote $outFile" -ForegroundColor Green
