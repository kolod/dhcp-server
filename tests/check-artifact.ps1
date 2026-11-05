<#
.SYNOPSIS
    Checks the release artifact against the constitution (portable and compact).
.DESCRIPTION
    Verifies that the Release output is a single DhcpServer.exe under the size limit, with no
    culture satellite folders and no extra DLLs, that every string table is embedded, and that
    the manifest requests the expected execution level. Prints the SHA-256 of the executable.
#>
param(
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\src\DhcpServer.App\bin\Release\net48')
)

$ErrorActionPreference = 'Stop'
$failures = @()
$exe = Join-Path $OutputDir 'DhcpServer.exe'
$maxSize = 5MB

if (-not (Test-Path $exe)) {
    throw "Executable not found: $exe (run 'dotnet build -c Release' first)"
}

$size = (Get-Item $exe).Length
if ($size -ge $maxSize) { $failures += "DhcpServer.exe is $size bytes (limit $($maxSize / 1MB) MB)" }

$cultureDirs = Get-ChildItem $OutputDir -Directory | Where-Object { $_.Name -match '^[a-z]{2}(-[A-Za-z]+)?$' }
if ($cultureDirs) { $failures += "Satellite folders present: $($cultureDirs.Name -join ', ')" }

$dlls = Get-ChildItem $OutputDir -Filter *.dll
if ($dlls) { $failures += "Runtime DLLs present: $($dlls.Name -join ', ')" }

# Resource names and manifest are checked without loading the assembly for execution.
$bytes = [IO.File]::ReadAllBytes($exe)
$text = [Text.Encoding]::ASCII.GetString($bytes)
foreach ($code in 'en', 'uk', 'de', 'fr', 'es', 'ja') {
    if (-not $text.Contains("DhcpServer.Strings.$code.resources")) { $failures += "Missing embedded strings: $code" }
}
if (-not $text.Contains('level="requireAdministrator"')) { $failures += 'Manifest does not request requireAdministrator' }

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"DhcpServer.exe: $size bytes, SHA-256 $hash"

if ($failures) {
    $failures | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    exit 1
}

'Artifact check passed.'
