# compute_extension_id.ps1
# Reads extension.pem and computes Chrome Extension ID + public key

param(
    [string]$PemFile = (Join-Path $PSScriptRoot "extension.pem"),
    [string]$OutputDir = (Join-Path (Split-Path $PSScriptRoot) "build\extension")
)

Add-Type -AssemblyName System.Security

# Read PEM file
$pemLines = Get-Content $PemFile
$base64 = ($pemLines | Where-Object { $_ -notmatch "^-----" }) -join ""
$privateKeyBytes = [Convert]::FromBase64String($base64)

# Import private key and export public key
$rsa = [System.Security.Cryptography.RSA]::Create()
$bytesRead = 0
$rsa.ImportRSAPrivateKey($privateKeyBytes, [ref]$bytesRead)

$pubKeyBytes = $rsa.ExportSubjectPublicKeyInfo()

# Compute SHA256 of public key
$hasher = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $hasher.ComputeHash($pubKeyBytes)

# Extension ID: first 16 bytes, each nibble mapped to 'a'..'p'
$sb = [System.Text.StringBuilder]::new()
for ($i = 0; $i -lt 16; $i++) {
    $hi = ($hashBytes[$i] -shr 4) -band 0xF
    $lo = $hashBytes[$i] -band 0xF
    [void]$sb.Append([char](97 + $hi))
    [void]$sb.Append([char](97 + $lo))
}
$extensionId = $sb.ToString()

# Public key base64 (for manifest.json "key" field)
$publicKeyBase64 = [Convert]::ToBase64String($pubKeyBytes)

Write-Host "Extension ID: $extensionId"
Write-Host "Public Key: $publicKeyBase64"

# Save files
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory $OutputDir -Force | Out-Null }

[System.IO.File]::WriteAllText((Join-Path $OutputDir "extension_id.txt"), $extensionId)

# Generate updates.xml
$xml = @"
<?xml version='1.0' encoding='UTF-8'?>
<gupdate xmlns='http://www.google.com/update2/response' protocol='2.0'>
  <app appid='$extensionId'>
    <updatecheck codebase='file:///C:/Program Files/AppBlocker/Extension/appblocker.crx' version='1.0.0' />
  </app>
</gupdate>
"@
[System.IO.File]::WriteAllText((Join-Path $OutputDir "updates.xml"), $xml, [System.Text.Encoding]::UTF8)

$rsa.Dispose()
$hasher.Dispose()

Write-Host "Files saved to: $OutputDir"
