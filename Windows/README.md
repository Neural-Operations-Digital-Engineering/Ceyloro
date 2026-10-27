# Ceyloro Windows shell

Shell Windows Ceyloro memakai WinForms .NET 10 dan PdfPig untuk membaca teks PDF.
Paket release resmi menyertakan dependensi aplikasi dan binary media yang
sesuai arsitektur (`win-x64` dan `win-arm64`). Shell ini menyediakan:

- **Ikon system tray** dan auto-update checker resmi.
- **Interaksi Shift-drag native** dari File Explorer untuk membuka roda konversi langsung di dekat pointer mouse.
- **Hotkey fallback `Ctrl+Alt+Space`** untuk membuka roda secara manual.
- **Roda radial presisi tinggi**: petal aktif beraksen oranye, border netral, indikator target di pusat hub, serta navigasi keyboard (angka `1–9`, panah, Enter, Esc).
- **Target format cerdas** yang disaring dari jenis file yang dipilih dan kapabilitas engine (FFmpeg/FFprobe, LibreOffice, Pandoc).
- **Konversi batch** dengan progres, pembatalan aman tanpa file sampah, penamaan unik anti-konflik, dan laporan kegagalan per file.
- **Kompresi media & dokumen** (gambar kuantisasi 8-bit, audio/video CRF modern, PDF pintar dengan preservasi teks, dan dokumen Office).
- **Pengaturan & Diagnostik** untuk folder output, path engine, preferensi kualitas, dan status ketersediaan dependency.
- **Dukungan installer Inno Setup** (`Setup.exe`) serta skrip instalasi native non-admin per-user.

## Alur Penggunaan

1. **Alur Utama (Shift-Drag):**
   Tahan tombol **Shift**, lalu tarik satu atau beberapa file dari File Explorer. Roda konversi akan otomatis muncul di dekat kursor. Arahkan ke target format yang diinginkan (petal oranye), lalu lepaskan tombol mouse untuk memulai konversi.
2. **Alur Fallback (Hotkey):**
   Tekan `Ctrl+Alt+Space` untuk membuka roda secara manual di posisi kursor, kemudian seret file dari Explorer ke roda.
3. **Navigasi Keyboard:**
   Saat roda aktif, gunakan tombol angka `1`–`9` atau tombol panah kiri/kanan/atas/bawah untuk memilih target, lalu tekan `Enter` untuk konfirmasi atau `Esc` untuk membatalkan.

## Build di Windows

Pasang .NET 10 SDK, lalu jalankan:

```powershell
dotnet build .\Windows\Ceyloro.Windows\Ceyloro.Windows.csproj -c Release -r win-x64
dotnet build .\Windows\Ceyloro.Windows\Ceyloro.Windows.csproj -c Release -r win-arm64
```

Untuk menjalankan test suite konversi otomatis Windows:

```powershell
dotnet run --project .\Windows\Ceyloro.Windows.Tests\Ceyloro.Windows.Tests.csproj -c Release -r win-x64
```

Paket release resmi sudah membawa `ffmpeg.exe` build LGPL di samping `Ceyloro.exe`.
Build dari source tree dapat mengambil FFmpeg otomatis menggunakan skrip:

```powershell
.\scripts\fetch-ffmpeg-windows.ps1 -Destination ffmpeg-package -Architecture x64
```

## Distribusi, Installer, & Code Signing

Paket rilis resmi didistribusikan dalam dua bentuk per arsitektur (`win-x64` dan `win-arm64`):
1. **Installer Setup (`Ceyloro-Windows-<arch>-Setup.exe`)**:
   Dibuat menggunakan Inno Setup (`scripts/package-windows-installer.ps1`), dapat dipasang per-user tanpa memerlukan hak administrator (UAC), membuat pintasan Start Menu, mendaftarkan startup tray, dan menyediakan uninstall bersih.
2. **Paket Portable ZIP (`Ceyloro-Windows-<arch>.zip`)**:
   Dapat langsung diekstrak dan dijalankan, atau dipasang via skrip `scripts/install-ceyloro.ps1`.

### Authenticode Signing:
Gunakan sertifikat penandatanganan kode yang valid dengan `signtool.exe`:
```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f "SertifikatCeyloro.pfx" /p "Password" "publish\Ceyloro.exe"
```

### Verifikasi Checksum:
Setiap rilis GitHub menyertakan `SHA256SUMS.txt` / `.sha256` untuk memastikan integritas biner sebelum dijalankan:
```powershell
Get-FileHash -Algorithm SHA256 .\Ceyloro-Windows-x64.zip
```
