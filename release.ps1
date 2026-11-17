# Produit dist\Claudiometer-<version>.zip (exe + README + empreinte SHA-256).
# Signature facultative : definir CLAUDIOMETER_CERT (empreinte d'un certificat de signature de code
# present dans le magasin utilisateur) pour signer l'exe, ce qui evite l'avertissement SmartScreen.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
& "$root\build.ps1"
if ($LASTEXITCODE -ne 0) { throw "Echec de la compilation" }

$version = (Select-String -Path "$root\Claudiometer.cs" -Pattern 'public const string Version = "([^"]+)"').Matches[0].Groups[1].Value
$exe = "$root\Claudiometer.exe"

if ($env:CLAUDIOMETER_CERT) {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Thumbprint -eq $env:CLAUDIOMETER_CERT | Select-Object -First 1
    if (-not $cert) { throw "Certificat $env:CLAUDIOMETER_CERT introuvable" }
    Set-AuthenticodeSignature -FilePath $exe -Certificate $cert -TimestampServer 'http://timestamp.digicert.com' | Out-Null
    Write-Host "Exe signe."
} else {
    Write-Host "Exe non signe (CLAUDIOMETER_CERT non defini)."
}

$dist = "$root\dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = "$dist\Claudiometer-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $exe, "$root\README.md" -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  Claudiometer-$version.zip" | Set-Content "$zip.sha256" -Encoding ascii
Write-Host "OK -> $zip"
Write-Host "SHA-256 : $hash"
