param(
    [Parameter(Mandatory = $false)]
    [string]$InputPath = (Join-Path $PSScriptRoot 'steamdb-localization-output.csv'),

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\..\HD2ModManager\Resources\Data\assets_stingray_map.generated.txt')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Convert-ToMurmurUnsignedString {
    param([Parameter(Mandatory = $true)][string]$SignedValue)

    $signed = [long]$SignedValue
    if ($signed -lt 0) {
        $unsigned = [System.Numerics.BigInteger]::Parse('18446744073709551616') + [System.Numerics.BigInteger]$signed
        return $unsigned.ToString()
    }

    return ([System.Numerics.BigInteger]$signed).ToString()
}

function Convert-ToTypeName {
    param([Parameter(Mandatory = $true)][string]$Value)

    switch ($Value) {
        'unit' { return 'unit' }
        'wwise_stream' { return 'wwise_stream' }
        default { return $Value }
    }
}

if (-not (Test-Path -LiteralPath $InputPath)) {
    throw "Input file not found: $InputPath`nDownload CSV from https://steamdb.info/app/553850/localization/ and save it as steamdb-localization-output.csv."
}

$rows = Import-Csv -LiteralPath $InputPath
$records = New-Object System.Collections.Generic.List[object]
$seen = New-Object 'System.Collections.Generic.HashSet[string]'

foreach ($row in $rows) {
    $token = [string]$row.Token
    if ([string]::IsNullOrWhiteSpace($token)) {
        continue
    }

    if ($token -notmatch '^(?<source>.+?)_(?<path>content/.+)_(?<type>unit|wwise_stream)_(?<hash>-?\d+)$') {
        continue
    }

    $assetPath = $Matches['path']
    $typeName = Convert-ToTypeName $Matches['type']
    $hash = Convert-ToMurmurUnsignedString $Matches['hash']
    $key = "$hash|$typeName|$assetPath"

    if ($seen.Add($key)) {
        $records.Add([pscustomobject]@{
            Hash = $hash
            Type = $typeName
            Path = $assetPath
        })
    }
}

if ($records.Count -eq 0) {
    throw 'No asset records were parsed. Check whether the SteamDB CSV column names or token format changed.'
}

$ordered = $records | Sort-Object Path, Type, Hash
$outDir = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Generated from SteamDB localization tokens: https://steamdb.info/app/553850/localization/')
$lines.Add('# Format: murmur,type,path')
foreach ($record in $ordered) {
    $lines.Add("$($record.Hash),$($record.Type),$($record.Path)")
}

[System.IO.File]::WriteAllLines((Resolve-Path -LiteralPath (Split-Path -Parent $OutputPath)).Path + [System.IO.Path]::DirectorySeparatorChar + (Split-Path -Leaf $OutputPath), $lines, [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $($ordered.Count) asset mappings to $OutputPath"
