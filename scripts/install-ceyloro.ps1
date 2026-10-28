[CmdletBinding()]
param(
    [string]$SourceZip = "",
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\Ceyloro",
    [switch]$CreateDesktopShortcut,
    [switch]$StartAtLogin
)

$ErrorActionPreference = "Stop"

$currentDir = (Get-Location).Path
$sourceDir = if ($SourceZip -and (Test-Path $SourceZip)) {
    $tempExtract = Join-Path ([System.IO.Path]::GetTempPath()) "ceyloro-install-$([Guid]::NewGuid().ToString('N'))"
    Expand-Archive -LiteralPath $SourceZip -DestinationPath $tempExtract -Force
    $tempExtract
} elseif (Test-Path "package\Ceyloro\Ceyloro.exe") {
    Join-Path $currentDir "package\Ceyloro"
} elseif (Test-Path "publish\Ceyloro.exe") {
    Join-Path $currentDir "publish"
} else {
    throw "Sumber binary Ceyloro.exe tidak ditemukan. Sediakan -SourceZip atau jalankan dari direktori build."
}

Write-Host "Memasang Ceyloro ke: $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item "$sourceDir\*" $InstallDir -Recurse -Force

$exePath = Join-Path $InstallDir "Ceyloro.exe"
$version = (Get-Item $exePath).VersionInfo.ProductVersion
if (-not $version) { $version = "0.1.0" }

# Buat Start Menu Shortcut
$wsh = New-Object -ComObject WScript.Shell
$programsDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$shortcutPath = Join-Path $programsDir "Ceyloro.lnk"
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $InstallDir
$shortcut.Description = "Ceyloro File Conversion Wheel"
$shortcut.Save()
Write-Host "Pintasan Start Menu dibuat: $shortcutPath"

# Buat Desktop Shortcut jika diminta
if ($CreateDesktopShortcut) {
    $desktopDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
    $desktopShortcutPath = Join-Path $desktopDir "Ceyloro.lnk"
    $dShortcut = $wsh.CreateShortcut($desktopShortcutPath)
    $dShortcut.TargetPath = $exePath
    $dShortcut.WorkingDirectory = $InstallDir
    $dShortcut.Save()
    Write-Host "Pintasan Desktop dibuat: $desktopShortcutPath"
}

# Daftarkan Startup jika diminta
if ($StartAtLogin) {
    Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "Ceyloro" -Value "`"$exePath`""
}

# Daftarkan Menu Konteks Explorer
$shellKeys = @("HKCU:\Software\Classes\*\shell\Ceyloro", "HKCU:\Software\Classes\Directory\shell\Ceyloro")
foreach ($key in $shellKeys) {
    New-Item -Path $key -Force | Out-Null
    Set-ItemProperty -Path $key -Name "(default)" -Value "Buka dengan Roda Ceyloro"
    Set-ItemProperty -Path $key -Name "Icon" -Value "`"$exePath`",0"
    $cmdKey = Join-Path $key "command"
    New-Item -Path $cmdKey -Force | Out-Null
    Set-ItemProperty -Path $cmdKey -Name "(default)" -Value "`"$exePath`" --files `"%1`""
}
Write-Host "Menu Konteks File Explorer didaftarkan."

# Tulis script uninstall bersih
$uninstallScript = @"
@echo off
setlocal
echo Menghapus instalasi Ceyloro...
taskkill /f /im Ceyloro.exe >nul 2>&1
timeout /t 1 /nobreak >nul

del /q "$shortcutPath" 2>nul
del /q "$desktopShortcutPath" 2>nul
reg delete "HKCU\Software\Classes\*\shell\Ceyloro" /f >nul 2>&1
reg delete "HKCU\Software\Classes\Directory\shell\Ceyloro" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "Ceyloro" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Ceyloro" /f >nul 2>&1

rmdir /s /q "$InstallDir" 2>nul
echo Ceyloro berhasil dihapus. Berkas konversi dan pengaturan Anda tetap tersimpan.
pause
"@
$uninstallCmdPath = Join-Path $InstallDir "uninstall.cmd"
Set-Content -Path $uninstallCmdPath -Value $uninstallScript -Encoding ASCII

# Daftarkan di Add/Remove Programs (Registry HKCU Uninstall)
$uninstallReg = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Ceyloro"
New-Item -Path $uninstallReg -Force | Out-Null
Set-ItemProperty -Path $uninstallReg -Name "DisplayName" -Value "Ceyloro"
Set-ItemProperty -Path $uninstallReg -Name "DisplayVersion" -Value $version
Set-ItemProperty -Path $uninstallReg -Name "Publisher" -Value "Ceyloro"
Set-ItemProperty -Path $uninstallReg -Name "DisplayIcon" -Value "$exePath,0"
Set-ItemProperty -Path $uninstallReg -Name "InstallLocation" -Value $InstallDir
Set-ItemProperty -Path $uninstallReg -Name "UninstallString" -Value "`"$uninstallCmdPath`""
Set-ItemProperty -Path $uninstallReg -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallReg -Name "NoRepair" -Value 1 -Type DWord

Write-Host "Instalasi Ceyloro selesai!"

