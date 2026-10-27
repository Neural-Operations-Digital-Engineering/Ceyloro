[CmdletBinding()]
param(
    [ValidateSet("x64", "arm64")]
    [string]$Architecture = "x64",
    [string]$Version = "0.1.0",
    [string]$PackageDir = "package/Ceyloro",
    [string]$OutputDir = "dist"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PackageDir)) {
    throw "Folder package '$PackageDir' tidak ditemukan. Jalankan publish terlebih dahulu."
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Buat portable ZIP
$zipName = "Ceyloro-Windows-$Architecture.zip"
$zipPath = Join-Path $OutputDir $zipName
Write-Host "Membuat paket ZIP portable: $zipPath"
Compress-Archive -Path "$PackageDir\*" -DestinationPath $zipPath -Force

# Buat SHA256 checksum untuk ZIP
$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $zipName" | Set-Content -Encoding ascii (Join-Path $OutputDir "$zipName.sha256")

# Cari Inno Setup Compiler (ISCC.exe)
$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles} "Inno Setup 6\ISCC.exe"),
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
) | Where-Object { $_ -and (Test-Path $_ -PathType Leaf) }

if ($isccCandidates.Count -gt 0) {
    $iscc = $isccCandidates[0]
    Write-Host "Mengompilasi installer dengan Inno Setup ($iscc)..."
    $issFile = Join-Path (Get-Location).Path "Windows\installer\Ceyloro.iss"
    & $iscc "/DAppArch=$Architecture" "/DMyAppVersion=$Version" $issFile
    
    $setupExe = Join-Path $OutputDir "Ceyloro-Windows-$Architecture-Setup.exe"
    if (Test-Path $setupExe) {
        $setupHash = (Get-FileHash -Path $setupExe -Algorithm SHA256).Hash.ToLowerInvariant()
        "$setupHash  Ceyloro-Windows-$Architecture-Setup.exe" | Set-Content -Encoding ascii "$setupExe.sha256"
        Write-Host "Installer berhasil dibuat: $setupExe ($setupHash)"
    }
} else {
    Write-Host "ISCC.exe tidak ditemukan di lingkungan ini. Melewati kompilasi Inno Setup. Paket portable ZIP tersedia di $zipPath."
}

