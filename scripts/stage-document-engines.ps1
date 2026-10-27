[CmdletBinding()]
param(
    [string]$Destination = "document-engines",
    [switch]$DownloadPandocIfMissing,
    [switch]$Optional
)

$ErrorActionPreference = "Stop"
$root = (Get-Location).Path
$sourceRoot = $env:CEYLORO_DOCUMENT_ENGINES_PATH
$pandocSource = $env:CEYLORO_PANDOC_PATH

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

if ($sourceRoot) {
    if (-not (Test-Path $sourceRoot -PathType Container)) {
        throw "Folder engine dokumen tidak ditemukan: $sourceRoot"
    }
    Copy-Item (Join-Path $sourceRoot '*') $Destination -Recurse -Force
}

$libreOfficeExecutable = Join-Path $Destination 'LibreOffice\program\soffice.exe'
if (-not (Test-Path $libreOfficeExecutable -PathType Leaf)) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles} 'LibreOffice\program\soffice.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'LibreOffice\program\soffice.exe')
    ) | Where-Object { $_ -and (Test-Path $_ -PathType Leaf) }
    if ($candidates.Count -gt 0) {
        $installRoot = Split-Path (Split-Path $candidates[0] -Parent) -Parent
        Copy-Item $installRoot (Join-Path $Destination 'LibreOffice') -Recurse -Force
    }
}

$pandocDest = Join-Path $Destination 'pandoc\pandoc.exe'
if (-not (Test-Path $pandocDest -PathType Leaf)) {
    if (-not $pandocSource) {
        $command = Get-Command pandoc.exe -ErrorAction SilentlyContinue
        if ($command) { $pandocSource = $command.Source }
    }
    if (-not $pandocSource) {
        $command = Get-Command pandoc -ErrorAction SilentlyContinue
        if ($command) { $pandocSource = $command.Source }
    }
    if ($pandocSource -and (Test-Path $pandocSource -PathType Leaf)) {
        New-Item -ItemType Directory -Force -Path (Join-Path $Destination 'pandoc') | Out-Null
        Copy-Item $pandocSource $pandocDest -Force
    } elseif ($DownloadPandocIfMissing) {
        Write-Host "Mengunduh Pandoc 3.2 untuk Windows..."
        $tempZip = Join-Path ([System.IO.Path]::GetTempPath()) "pandoc-3.2-windows-x86_64.zip"
        $expectedPandocSha256 = "6b402830f3607f2ef8be8a4d70b77b31126ef854ca66956276d8b6713919e1ca"
        $pandocUrl = "https://github.com/jgm/pandoc/releases/download/3.2/pandoc-3.2-windows-x86_64.zip"
        
        Invoke-WebRequest -Uri $pandocUrl -OutFile $tempZip
        $actualSha = (Get-FileHash -Path $tempZip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualSha -ne $expectedPandocSha256.ToLowerInvariant()) {
            Remove-Item $tempZip -Force
            throw "Checksum Pandoc tidak cocok: diharapkan $expectedPandocSha256, didapat $actualSha"
        }
        $tempExtract = Join-Path ([System.IO.Path]::GetTempPath()) "pandoc-extract-$([Guid]::NewGuid().ToString('N'))"
        Expand-Archive -Path $tempZip -DestinationPath $tempExtract -Force
        $extractedExe = Get-ChildItem -Path $tempExtract -Filter "pandoc.exe" -Recurse -File | Select-Object -First 1
        if ($extractedExe) {
            New-Item -ItemType Directory -Force -Path (Join-Path $Destination 'pandoc') | Out-Null
            Copy-Item $extractedExe.FullName $pandocDest -Force
        }
        Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
        Remove-Item $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$hasLo = Test-Path $libreOfficeExecutable -PathType Leaf
$hasPandoc = Test-Path $pandocDest -PathType Leaf

if (-not $hasLo) {
    if (-not $Optional) {
        throw "LibreOffice belum ditemukan. Pasang LibreOffice atau set CEYLORO_DOCUMENT_ENGINES_PATH."
    } else {
        Write-Warning "LibreOffice tidak ditemukan; target yang membutuhkan LibreOffice akan disembunyikan dari UI."
    }
} else {
    Write-Host "LibreOffice siap di: $libreOfficeExecutable"
    & $libreOfficeExecutable --version 2>$null | Select-Object -First 1
}

if (-not $hasPandoc) {
    if (-not $Optional) {
        throw "pandoc belum ditemukan. Pasang pandoc atau jalankan dengan -DownloadPandocIfMissing."
    } else {
        Write-Warning "Pandoc tidak ditemukan; target yang membutuhkan pandoc akan disembunyikan dari UI."
    }
} else {
    Write-Host "Pandoc siap di: $pandocDest"
    & $pandocDest --version 2>$null | Select-Object -First 1
}

Write-Host "Pemeriksaan engine dokumen selesai."
