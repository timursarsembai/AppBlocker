# build_extension.ps1
# Packs Chrome extension into .crx for auto-install via registry policy.
# Run: .\tools\build_extension.ps1
# Requires: Chrome installed on the build machine

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$extensionDir = Join-Path $projectRoot "src\AppBlocker.Extension"
$outputDir = Join-Path $projectRoot "build\extension"
$keyFile = Join-Path $projectRoot "tools\extension.pem"

# Find Chrome
$chromePaths = @(
    "${env:ProgramFiles}\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
    "${env:LOCALAPPDATA}\Google\Chrome\Application\chrome.exe"
)
$chrome = $chromePaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $chrome) {
    Write-Error "Chrome not found! Install Google Chrome to build the extension."
    exit 1
}

Write-Host "[1/5] Chrome found: $chrome" -ForegroundColor Green

# Create output directory
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# Pack extension
Write-Host "[2/5] Packing extension..." -ForegroundColor Cyan

if (Test-Path $keyFile) {
    & $chrome --pack-extension="$extensionDir" --pack-extension-key="$keyFile" --no-message-box 2>$null
    Write-Host "  Using existing key: $keyFile" -ForegroundColor DarkGray
} else {
    & $chrome --pack-extension="$extensionDir" --no-message-box 2>$null
    
    $generatedPem = Join-Path (Split-Path $extensionDir) "AppBlocker.Extension.pem"
    if (Test-Path $generatedPem) {
        Move-Item $generatedPem $keyFile -Force
        Write-Host "  Generated new key: $keyFile" -ForegroundColor Yellow
        Write-Host "  IMPORTANT: Keep this key! It determines Extension ID." -ForegroundColor Yellow
    }
}

# Move .crx to output
$generatedCrx = Join-Path (Split-Path $extensionDir) "AppBlocker.Extension.crx"
if (Test-Path $generatedCrx) {
    Move-Item $generatedCrx (Join-Path $outputDir "appblocker.crx") -Force
} else {
    Write-Error "Failed to create .crx file"
    exit 1
}

Write-Host "[3/5] CRX created: $outputDir\appblocker.crx" -ForegroundColor Green

# Compute Extension ID from public key
Write-Host "[4/5] Computing Extension ID..." -ForegroundColor Cyan

$pemContent = Get-Content $keyFile -Raw
$pemBody = $pemContent -replace "-----BEGIN RSA PRIVATE KEY-----", "" -replace "-----END RSA PRIVATE KEY-----", "" -replace "\s+", ""
$privateKeyDer = [Convert]::FromBase64String($pemBody)

$rsa = [System.Security.Cryptography.RSA]::Create()
$rsa.ImportRSAPrivateKey($privateKeyDer, [ref]$null)
$publicKeyDer = $rsa.ExportSubjectPublicKeyInfo()

$sha256 = [System.Security.Cryptography.SHA256]::Create()
$hash = $sha256.ComputeHash($publicKeyDer)

$extensionId = ""
for ($i = 0; $i -lt 16; $i++) {
    $extensionId += [char]([int][char]'a' + ($hash[$i] -shr 4))
    $extensionId += [char]([int][char]'a' + ($hash[$i] -band 0xf))
}

$publicKeyBase64 = [Convert]::ToBase64String($publicKeyDer)

Write-Host "  Extension ID: $extensionId" -ForegroundColor Green

# Create updates.xml
Write-Host "[5/5] Generating updates.xml..." -ForegroundColor Cyan

$xmlContent = "<?xml version='1.0' encoding='UTF-8'?>"
$xmlContent += "`r`n<gupdate xmlns='http://www.google.com/update2/response' protocol='2.0'>"
$xmlContent += "`r`n  <app appid='$extensionId'>"
$xmlContent += "`r`n    <updatecheck codebase='file:///C:/Program Files/AppBlocker/Extension/appblocker.crx' version='1.0.0' />"
$xmlContent += "`r`n  </app>"
$xmlContent += "`r`n</gupdate>"

[System.IO.File]::WriteAllText((Join-Path $outputDir "updates.xml"), $xmlContent, [System.Text.Encoding]::UTF8)
Write-Host "  updates.xml created" -ForegroundColor Green

# Save Extension ID for installer.iss
[System.IO.File]::WriteAllText((Join-Path $outputDir "extension_id.txt"), $extensionId, [System.Text.Encoding]::UTF8)

# Summary
Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " Extension build complete!" -ForegroundColor Green
Write-Host " Extension ID: $extensionId" -ForegroundColor White
Write-Host " CRX: $outputDir\appblocker.crx" -ForegroundColor White
Write-Host " Updates: $outputDir\updates.xml" -ForegroundColor White
Write-Host "============================================" -ForegroundColor Green

$rsa.Dispose()
$sha256.Dispose()
