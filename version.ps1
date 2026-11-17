# Change le numero de version dans Claudiometer.cs (Program.Version + AssemblyVersion + AssemblyFileVersion).
# Sans argument : affiche la version courante.
#   .\version.ps1 1.2.0
param([string]$Version)
$ErrorActionPreference = 'Stop'
$file = "$PSScriptRoot\Claudiometer.cs"
$utf8 = New-Object System.Text.UTF8Encoding($false)
$src = [IO.File]::ReadAllText($file, $utf8)

$cur = [regex]::Match($src, 'public const string Version = "([^"]+)"').Groups[1].Value
if (-not $Version) { Write-Host $cur; return }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version invalide '$Version' : attendu MAJEUR.MINEUR.PATCH (ex. 1.2.0)" }

$src = [regex]::Replace($src, '(public const string Version = ")[^"]+(")', "`${1}$Version`${2}")
$src = [regex]::Replace($src, '(AssemblyVersion\(")[^"]+("\))', "`${1}$Version.0`${2}")
$src = [regex]::Replace($src, '(AssemblyFileVersion\(")[^"]+("\))', "`${1}$Version.0`${2}")
[IO.File]::WriteAllText($file, $src, $utf8)
Write-Host "Version : $cur -> $Version"
