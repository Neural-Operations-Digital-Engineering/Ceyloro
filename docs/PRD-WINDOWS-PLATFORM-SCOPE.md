# Ceyloro Platform Windows Product Scope

**Status:** Implemented Baseline & Scoped  
**Versi:** 1.0  
**Sumber:** Master Product Scope Ceyloro, PRD Windows yang sudah ada, dan referensi visual radial wheel  
**Platform:** Windows 10 2004 atau lebih baru  
**Arsitektur:** `win-x64` dan `win-arm64`  
**Hubungan:** Dokumen scope Windows; selaras dengan PRD finalisasi Windows.

## 1. Tujuan

Mendefinisikan pengalaman Ceyloro pada Windows dengan tetap mempertahankan
konsep radial wheel, tetapi mengikuti perilaku native File Explorer, system
tray, WinForms, Windows input, dan perbedaan x64/ARM64.

Dokumen ini hanya menentukan scope produk dan rencana perilaku. Implementasi
harus dilakukan setelah capability, packaging, dan acceptance criteria disetujui.

## 2. Baseline yang perlu diverifikasi

- system tray application;
- hotkey `Ctrl + Alt + Space`;
- radial overlay;
- drag-and-drop dari File Explorer;
- FFmpeg dan FFprobe sesuai arsitektur;
- konversi media, subtitle, arsip, PDF, dan dokumen tertentu;
- video-to-audio;
- kompresi;
- progress, cancellation, batch, dan cleanup;
- Settings untuk output dan FFmpeg;
- self-contained build x64 dan ARM64.

Semua baseline tetap berstatus Verify sampai diuji pada Windows nyata.

## 3. Tujuan pengalaman pengguna

- Pengguna dapat membuka wheel dari Explorer tanpa harus memahami backend.
- Wheel menampilkan operasi yang kompatibel dengan seluruh file yang sedang
  dipilih.
- Hotkey tetap menjadi fallback resmi ketika global drag monitoring tidak dapat
  digunakan.
- UI Windows mengikuti bahasa visual Ceyloro, tetapi tidak meniru Finder atau
  menu bar macOS secara literal.
- Cancel, error, dan output dapat dipahami tanpa membuka terminal.

## 4. Model interaksi Windows

### Alur utama yang direncanakan

1. Pengguna memilih file di File Explorer.
2. Pengguna memulai drag, dengan Shift-drag bila kemampuan tersebut tersedia.
3. Ceyloro menampilkan wheel dekat pointer.
4. Target yang tidak kompatibel disembunyikan atau disabled dengan alasan.
5. Pengguna melepaskan file pada target.
6. Ceyloro membuat job, menampilkan progress, dan melaporkan hasil.

### Fallback

`Ctrl + Alt + Space` membuka wheel secara manual, lalu file ditarik ke overlay.
Fallback ini harus tetap tersedia walaupun Shift-drag sudah didukung.

### Aturan keamanan interaksi

- Drag biasa tanpa modifier tidak boleh membuka wheel secara tidak sengaja.
- Escape, click outside, timeout, atau drag yang hilang membatalkan dengan aman.
- Drop di luar wheel tidak boleh diteruskan ke aplikasi di bawah overlay.
- Wheel harus berada di working area monitor, bukan menutupi taskbar secara
  tidak terduga.
- Perilaku modifier tidak boleh mengganggu shortcut Explorer atau aplikasi
  sumber.

## 5. Radial wheel dan visual direction

Rencana visual mengikuti referensi produk:

- petal aktif memakai aksen oranye;
- petal lain netral dengan border tipis;
- pusat wheel menampilkan target aktif;
- ikon garis digunakan untuk tools dan actions;
- label singkat dan terbaca pada scaling tinggi;
- jumlah petal menyesuaikan konteks;
- wheel mendukung keyboard navigation dan angka `1–9`;
- state loading, disabled, processing, success, error, dan cancelled terlihat.

Implementasi harus memperhatikan:

- Windows DPI awareness PerMonitorV2;
- scaling 100%, 125%, 150%, dan 200%;
- monitor dengan resolusi dan DPI berbeda;
- koordinat virtual desktop negatif;
- high-contrast mode;
- keyboard-only navigation;
- label accessibility dan screen reader.

## 6. Conversion scope Windows

### Images

Target scope: JPG, PNG, WebP, HEIC, TIFF, AVIF, BMP, GIF, dan PDF bila
backend tersedia.

Image targets harus ditentukan oleh decoder/encoder Windows atau FFmpeg yang
terdeteksi. Transparansi, EXIF orientation, dan flattening ke JPG harus
dijelaskan.

### Audio

Target scope: MP3, M4A, WAV, FLAC, OGG, OPUS, AIFF, dan WMA bila capability
FFmpeg tersedia.

### Video

Target scope: MP4, MOV, MKV, WebM, AVI, WMV, GIF, serta audio extraction ke
format audio yang tersedia.

Video conversion dan video-to-audio harus tampil sebagai capability yang
berbeda, meskipun keduanya menggunakan FFmpeg.

### PDF, dokumen, slides, dan spreadsheets

Scope direncanakan:

- PDF ke JPG, PNG, TXT, dan DOCX best-effort;
- gambar ke PDF dan PDF gabungan;
- DOCX, XLSX, PPTX, ODT, ODS, ODP melalui engine dokumen bila tersedia;
- CSV dan TSV untuk data tabular;
- PDF export dengan batasan fidelity yang jelas.

Target yang membutuhkan Pandoc atau LibreOffice tidak boleh ditampilkan bila
engine tidak ditemukan atau tidak cocok dengan arsitektur.

### Subtitle dan archives

- SRT, VTT, dan TXT sesuai operasi;
- ZIP, TAR, GZIP, dan TAR.GZ;
- Extract sebagai tool terpisah;
- RAR/7Z ditunda sampai backend dan security review tersedia.

## 7. Tools scope Windows

### Prioritas awal

- Compress;
- Extract;
- PDF Split;
- PDF Organize;
- Image Crop;
- Image Add Background;
- Audio Trim;
- Video Trim;
- Video Snapshot;
- Video Mute.

### Kandidat lanjutan

- Metadata;
- Redact;
- Annotate;
- Normalize;
- Channels;
- Visualize;
- Bleep;
- Video Crop;
- Video Speed;
- Video Split;
- Float atau shelf yang sesuai dengan Windows.

Setiap tool harus mempunyai input, output, parameter, progress, cancellation,
conflict naming, serta error message yang dapat ditindaklanjuti.

## 8. File actions Windows

Padanan native perlu ditentukan secara eksplisit:

- Copy melalui clipboard atau Explorer semantics;
- Copy Path sebagai teks;
- Open Folder atau Terminal/PowerShell;
- Move dengan destination picker dan conflict handling;
- pin/shelf hanya jika pengalaman Windows benar-benar berguna;
- AirDrop tidak ditampilkan sebagai fitur Windows.

File actions yang tidak memiliki padanan native tidak boleh dipaksakan hanya
untuk menyamakan jumlah petal dengan macOS.

## 9. Engine dan capability

Capability registry Windows minimal mencatat:

- input/output;
- encoder, decoder, muxer, atau engine;
- versi dan arsitektur engine;
- x64/ARM64 availability;
- batch dan multi-page;
- progress dan cancellation;
- batasan fidelity;
- error code.

FFmpeg, FFprobe, Pandoc, dan LibreOffice harus ditemukan dari lokasi package
yang stabil, bukan current working directory. Engine yang tidak tersedia harus
menyebabkan target disembunyikan atau menghasilkan pesan yang jelas.

## 10. Output, job, dan Settings

Output:

- folder di samping sumber atau folder kustom;
- nama unik saat konflik;
- multi-page sebagai folder atau ZIP;
- sumber tidak diubah atau ditimpa;
- output parsial dan temporary dibersihkan.

Job menggunakan status:

`queued → probing → running → succeeded / failed / cancelled`

Settings yang masuk scope:

- output folder;
- FFmpeg path;
- kualitas media;
- multi-page Folder/ZIP;
- launch at startup;
- hotkey;
- preferensi tools;
- diagnostics engine.

## 11. QA scope

### Perangkat

- Windows 10 x64;
- Windows 11 x64;
- Windows 11 ARM64;
- native ARM64 dan emulated x64 bila relevan.

### Desktop interaction

- Explorer normal, maximized, fullscreen, dan elevated;
- satu dan dua monitor;
- koordinat negatif;
- mixed DPI;
- high contrast;
- keyboard-only;
- drag cancellation dan hotkey fallback.

### Engine dan file safety

- FFmpeg bundled, custom, hilang, dan invalid;
- document engines tersedia/tidak tersedia;
- video-to-audio seluruh target yang capability-nya ada;
- file Unicode, path panjang, network drive, removable drive;
- folder read-only, disk penuh, file locked;
- archive traversal, symlink, archive rusak;
- cancel saat FFmpeg, PDF, document engine, archive, dan cleanup.

## 12. Urutan pengerjaan yang direncanakan

1. WIN-0: freeze minimum Windows, modifier, engine, dan capability matrix.
2. WIN-1: stabilkan hotkey, Explorer drop, overlay, dan cancellation.
3. WIN-2: terapkan visual wheel, keyboard, DPI, dan accessibility.
4. WIN-3: finalisasi conversion dan document engine policy.
5. WIN-4: implementasikan tools prioritas.
6. WIN-5: implementasikan file actions yang memiliki padanan native.
7. WIN-6: validasi x64 dan ARM64 pada perangkat nyata.
8. WIN-7: lanjutkan ke installer, signing, updater, dan release gate melalui
   PRD finalisasi Windows.

## 13. Definition of done

PRD Windows ini siap menjadi acuan implementasi ketika:

- modifier dan fallback disetujui;
- conversion matrix per arsitektur disetujui;
- engine dokumen dan kebijakan ARM64 ditentukan;
- wheel tidak mencampur Convert, Tools, dan Actions;
- fitur yang belum tersedia tidak tampil sebagai aktif;
- accessibility, DPI, multi-monitor, output, cancel, dan error memiliki
  acceptance criteria;
- scope ini dapat diturunkan menjadi task implementasi tanpa asumsi tersembunyi.

## 14. Status dokumen

Dokumen ini hanya mendefinisikan scope produk Windows. Ia tidak mengaktifkan
fitur baru, tidak menjanjikan parity penuh, dan tidak menggantikan PRD
finalisasi Windows.

