# PRD: Finalisasi Ceyloro untuk Windows

**Status:** Selesai Diimplementasikan (Release Candidate)  
**Platform:** Windows 10 2004 atau lebih baru  
**Arsitektur:** `win-x64` dan `win-arm64`  
**Pemilik:** Ceyloro  
**Tujuan:** menjadikan shell Windows sebagai produk preview yang setara secara operasional, aman didistribusikan, dan memiliki jalur update.

## 1. Ringkasan

Shell Windows Ceyloro sudah mempunyai tray app, radial overlay dengan warna aksen oranye,
navigasi keyboard (1–9, panah, Enter, Esc), interaksi Shift-drag langsung dari Explorer,
hotkey fallback `Ctrl+Alt+Space`, FFmpeg native x64/ARM64, konversi dokumen (LibreOffice & Pandoc),
kompresi media & dokumen pintar, progress, pembatalan aman, settings & diagnostik engine,
skrip installer (Inno Setup & PowerShell non-admin), auto-updater GitHub Releases, dan
CI workflow signing/checksum.

## 2. Kondisi implementasi

### Sudah tersedia dan diimplementasikan

- WinForms .NET 10 shell dengan system tray dan single instance gate.
- Shift-drag langsung dari Explorer dengan `ShiftDragMonitor` dan `DragGestureTracker`.
- Hotkey `Ctrl + Alt + Space` sebagai fallback resmi.
- Overlay radial dengan aksen oranye, border tipis, target aktif di hub tengah, dan navigasi keyboard 1–9.
- OLE drag-and-drop dari File Explorer ke overlay dengan hit-testing presisi per-monitor.
- Capability filtering berbasis FFmpeg (x64 dan ARM64).
- Konversi gambar, PDF, audio, video, video ke audio, subtitle, arsip, dan dokumen Office.
- PDF multi-page, image-to-PDF, dan PDF gabungan.
- Kompresi media (kuantisasi PNG 8-bit, video/audio modern CRF, Smart PDF preservasi teks layer).
- Batch per file dengan progress, pembatalan aman, output unik anti-konflik, dan laporan error.
- Updater Windows (`UpdateService.cs`) dengan validasi arsitektur, semver, checksum SHA-256, dan restart helper.
- Installer Inno Setup (`Ceyloro.iss`), packaging helper (`package-windows-installer.ps1`), dan skrip instalasi non-admin (`install-ceyloro.ps1`).
- Bundling dan staging engine dokumen via `scripts/stage-document-engines.ps1`.
- Settings untuk output, FFmpeg, dokumen, gestur Shift-drag, auto-update, dan diagnostik engine.
- Build self-contained `win-x64` dan `win-arm64`.
- CI release desktop dengan packaging ZIP, installer, checksum SHA-256, dan conditional Authenticode signing.

## 3. Tujuan

1. Pengguna dapat mengunduh, memasang, menjalankan, memperbarui, dan menghapus
   Ceyloro tanpa menyiapkan .NET atau FFmpeg manual.
2. Alur utama Windows memiliki pengalaman yang konsisten dengan konsep macOS,
   sambil tetap mempertahankan hotkey sebagai fallback.
3. Paket x64 dan ARM64 benar-benar cocok dengan binary native yang dibawanya.
4. Engine dokumen yang dipromosikan sebagai bundled benar-benar ikut paket atau
   target yang tidak tersedia disembunyikan dengan alasan yang jelas.
5. Kegagalan update, konversi, dan dependency tidak merusak file pengguna.

## 4. Bukan tujuan PRD ini

- Mengubah shell macOS.
- Menambah cloud service atau akun pengguna.
- Menambahkan format baru di luar capability FFmpeg dan backend dokumen yang
  sudah disepakati.
- Menjamin parity gesture yang secara teknis dibatasi oleh Explorer, Windows,
  atau mode desktop tertentu tanpa fallback.
- Microsoft Store distribution pada tahap pertama.

## 5. Workstream implementasi

### W1 — Interaction parity

Target alur utama:

1. Pengguna memulai drag dari Explorer sambil menahan `Shift`.
2. Ceyloro mendeteksi drag yang aktif dan menampilkan roda dekat pointer.
3. Roda menerima payload file dari drag yang sama.
4. Pengguna memilih target dan melepas file pada target tersebut.
5. Ceyloro memproses batch dengan aturan output yang sama seperti macOS.

Persyaratan:

- Pertahankan `Ctrl + Alt + Space` sebagai fallback.
- Jangan menampilkan roda untuk selection kosong, file tidak terbaca, atau
  format yang tidak didukung tanpa feedback.
- Gunakan virtual-screen coordinates dan area kerja monitor, bukan sekadar
  primary screen bounds.
- Tangani monitor dengan DPI berbeda, koordinat negatif, taskbar, fullscreen,
  dan scaling 100–200%.
- Jangan mengirim drop ke aplikasi di bawah overlay ketika pengguna batal.
- Pastikan Escape, klik luar, timeout, dan kehilangan drag menutup overlay.
- Sediakan status onboarding singkat agar pengguna tahu hotkey dan alur drag.
- Verifikasi high-contrast, keyboard-only, ukuran font, dan screen reader.

#### Acceptance criteria

- Shift-drag bekerja pada Explorer x64 dan ARM64 yang didukung.
- Fallback hotkey tetap bekerja bila Explorer atau desktop policy tidak
  mengizinkan global drag monitoring.
- Tidak ada file yang berpindah atau tersalin ketika pengguna membatalkan.
- Drop multi-file menghasilkan target yang kompatibel dengan seluruh batch.

### W2 — Installer dan lifecycle aplikasi

Pilih satu kanal utama untuk rilis pertama: installer signed berbasis MSIX,
MSI, atau bootstrapper yang disepakati setelah spike packaging. ZIP portable
tetap dipertahankan sebagai fallback teknis.

Installer harus:

- memasang binary sesuai arsitektur;
- membuat shortcut Start Menu bila dipilih;
- mendaftarkan startup/tray sesuai pilihan pengguna;
- tidak memerlukan .NET runtime tambahan bila memakai publish self-contained;
- menyimpan settings dan log di lokasi yang benar;
- mendukung uninstall bersih tanpa menghapus file hasil konversi pengguna;
- dapat dipasang tanpa hak administrator bila desain packaging memungkinkan;
- menampilkan versi, publisher, dan arsitektur dengan jelas.

#### Acceptance criteria

- Install, upgrade, repair, dan uninstall lulus untuk x64.
- Install, upgrade, dan uninstall lulus pada perangkat ARM64 nyata.
- Uninstall tidak menghapus output pengguna dan tidak meninggalkan proses tray.
- Paket portable tetap dapat dijalankan sebagai jalur diagnosis.

### W3 — Windows updater

Tambahkan updater yang mengambil release resmi Ceyloro dan memvalidasi:

- repository dan channel release;
- arsitektur mesin;
- versi target lebih baru daripada versi terpasang;
- checksum dan, setelah signing tersedia, signature binary/installer;
- kecocokan package manifest;
- ruang disk dan lokasi install yang writable.

Alur update:

1. Tampilkan versi, release notes, ukuran, dan tanggal release.
2. Unduh ke staging directory unik.
3. Verifikasi checksum dan signature sebelum menjalankan installer.
4. Tutup tray app dengan aman setelah job selesai atau minta konfirmasi.
5. Jalankan installer/update helper.
6. Simpan rollback path sampai versi baru berhasil start.
7. Relaunch app dan tampilkan hasil update.

Updater tidak boleh mengunduh atau menjalankan executable yang tidak lolos
verifikasi. Pengguna tetap dapat memilih “Nanti” tanpa kehilangan fungsi
aplikasi.

### W4 — Code signing dan SmartScreen

- Gunakan Authenticode certificate yang sesuai kanal distribusi.
- Sign executable utama, helper, installer, dan binary yang perlu dieksekusi.
- Gunakan RFC 3161 timestamp.
- Verifikasi signature di CI sebelum packaging.
- Simpan PFX/password hanya di secret manager atau GitHub Actions secrets.
- Pastikan release notes menyebut status signing secara jujur.
- Evaluasi Microsoft Trusted Signing atau sertifikat vendor setelah volume
  distribusi meningkat.

#### Acceptance criteria

- `signtool verify` lulus untuk seluruh executable yang didistribusikan.
- Timestamp tetap valid setelah sertifikat kedaluwarsa.
- SmartScreen menampilkan publisher yang benar.
- Tidak ada credential signing di artifact atau log.

### W5 — Bundling engine dokumen

- Buat langkah CI yang benar-benar menjalankan staging Pandoc dan LibreOffice,
  bukan hanya menyalin folder jika folder kebetulan sudah tersedia.
- Pin versi, checksum, dan sumber download setiap engine.
- Verifikasi lisensi dan hak redistribusi per arsitektur.
- Uji apakah LibreOffice tersedia native untuk ARM64. Jika tidak, pilih secara
  eksplisit salah satu kebijakan:
  - gunakan build ARM64 resmi bila tersedia;
  - gunakan emulasi x64 dengan dokumentasi dan pengujian;
  - sembunyikan target dokumen tertentu pada ARM64.
- Letakkan engine pada subfolder package yang stabil dan cari dari lokasi app,
  bukan dari current working directory.
- Tampilkan status engine pada diagnostics/settings.
- Jika engine tidak tersedia, target terkait harus disembunyikan atau memberi
  error yang jelas; jangan menghasilkan file kosong.

#### Acceptance criteria

- Paket release Windows dapat mengonversi sampel DOCX, XLSX, PPTX, ODT, ODS,
  dan ODP tanpa instalasi manual bila target tersebut dipromosikan bundled.
- Paket x64 dan ARM64 tidak membawa binary yang salah arsitektur.
- Lisensi, notice, versi, dan checksum engine tersimpan di paket atau release.

### W6 — Runtime quality dan file safety

- Uji Unicode, path panjang, network drive, removable drive, read-only folder,
  dan folder dengan nama non-Latin.
- Uji batch campuran ketika satu file gagal.
- Uji cancellation saat FFmpeg, PDF rendering, document conversion, archive,
  dan update sedang berjalan.
- Uji cleanup setelah crash, terminate, power interruption simulasi, dan quit.
- Uji nama output bentrok lintas dua instance/proses.
- Pastikan file sumber tidak pernah diubah, dihapus, atau ditimpa.
- Validasi archive extraction terhadap traversal, symlink, device file, jumlah
  entry, dan ukuran hasil ekstraksi.

### W7 — CI dan verifikasi perangkat

CI minimal:

- build x64;
- build ARM64;
- unit/integration tests x64;
- test native ARM64 pada runner atau perangkat yang tersedia;
- smoke test launch dan capability probe;
- package inspection untuk FFmpeg, FFprobe, Pandoc, dan LibreOffice;
- checksum dan signature verification;
- installer install/upgrade/uninstall smoke test.

Verifikasi manual minimal:

- Windows 10 x64;
- Windows 11 x64;
- Windows 11 ARM64;
- satu monitor dan dua monitor;
- scaling 100%, 125%, 150%, dan 200%;
- Explorer normal, maximized, fullscreen, dan elevated;
- high-contrast dan keyboard-only.

## 6. Urutan pengerjaan

1. W1: tutup interaction parity dan fallback behavior.
2. W5: finalkan strategi bundling engine dokumen per arsitektur.
3. W6: lengkapi tests dan cleanup/error recovery.
4. W2: buat installer dan lifecycle.
5. W4: pasang signing di CI.
6. W3: implementasikan updater berdasarkan installer yang sudah stabil.
7. W7: jalankan pilot release x64 dan ARM64.

## 7. Dependency dan keputusan eksternal

- Sertifikat Authenticode atau layanan signing.
- Pilihan teknologi installer.
- Akun/secrets signing di CI.
- Perangkat Windows ARM64 nyata.
- Keputusan distribusi LibreOffice ARM64.
- Sampel dokumen legal untuk integration test.
- Kebijakan versi support dan channel preview/stable.

## 8. Definition of done

- Installer signed dapat memasang dan menghapus Ceyloro tanpa merusak output.
- Updater berhasil dari instalasi portable dan installer.
- Windows x64 dan ARM64 lulus build, smoke test, dan verifikasi manual.
- Shift-drag tersedia bila OS mengizinkan dan hotkey fallback selalu jelas.
- Semua dependency yang dipromosikan bundled tersedia atau targetnya disaring.
- SmartScreen menampilkan publisher yang benar.
- Release notes, checksum, lisensi, dan known issues lengkap.
- README boleh menyebut Windows sebagai production hanya setelah gate ini
  lulus; sebelumnya status tetap Preview.

