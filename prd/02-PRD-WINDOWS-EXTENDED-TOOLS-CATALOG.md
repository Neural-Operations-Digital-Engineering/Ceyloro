# PRD 02: Extended Tools Catalog Engine

**Dokumen:** `prd/02-PRD-WINDOWS-EXTENDED-TOOLS-CATALOG.md`  
**Status:** Spesifikasi Teknis Lengkap  
**Komponen Terkait:** `ToolsEngine.cs`, `ConversionEngine.cs`, `ArchiveConverter.cs`, `PdfConversion.cs`

---

## 1. Ringkasan & Ruang Lingkup
Melengkapi fitur `Kompres` yang sudah tersedia, PRD ini mendefinisikan implementasi 5 tool prioritas lanjutan untuk Windows. Seluruh tool ini memanfaatkan biner **FFmpeg/FFprobe LGPL** yang sudah dibundel serta library **PdfPig** tanpa menambah dependensi pihak ketiga baru.

---

## 2. Spesifikasi Teknis Tiap Tool

### 2.1. Tool: Video Mute (Hapus Audio)
* **Tujuan**: Menghilangkan seluruh track audio dari berkas video secara instan tanpa re-encoding video.
* **Input**: Format video yang didukung (MP4, MOV, MKV, WebM, AVI).
* **Perintah Engine**:
  ```bash
  ffmpeg -y -i "input.mp4" -c:v copy -an "output.mp4"
  ```
* **Karakteristik**: Berjalan instan (kecepatan I/O disk) karena video stream disalin langsung (`-c:v copy`) tanpa beban CPU/GPU.
* **Penamaan Output**: `<nama-file>-mute.<ext>` (dengan penomoran anti-konflik jika file sudah ada).

### 2.2. Tool: Video Snapshot (Tangkapan Layar Frame Video)
* **Tujuan**: Mengambil satu frame gambar beresolusi penuh dari video menjadi file PNG atau JPG berkualitas tinggi.
* **Input**: Video (MP4, MOV, MKV, WebM, AVI).
* **Perilaku**:
  * Menggunakan `ffprobe` untuk membaca durasi total video.
  * Mengambil frame pada posisi $10\%$ durasi atau detik ke-1 (menghindari frame hitam pembuka).
  * Pengguna dapat mengatur detik frame melalui dialog mini jika diperlukan.
* **Perintah Engine**:
  ```bash
  ffmpeg -ss 00:00:01.000 -i "input.mp4" -frames:v 1 -q:v 2 "snapshot.png"
  ```
* **Penamaan Output**: `<nama-file>-snapshot.png`.

### 2.3. Tool: Audio & Video Trim (Potong Durasi)
* **Tujuan**: Memotong bagian awal dan akhir media dengan menentukan waktu mulai (*start time*) dan durasi/waktu selesai (*end time*).
* **Mode Pemrosesan**:
  1. **Mode Lossless Copy (Default)**: `-ss <start> -to <end> -i <in> -c copy <out>` (Sangat cepat, memotong pada keyframe terdekat).
  2. **Mode Re-encode Presisi Frame**: Diterapkan jika pemotongan berada di antara keyframe dan pengguna memilih presisi tinggi.
* **Penamaan Output**: `<nama-file>-trim.<ext>`.

### 2.4. Tool: Extract Archive (Ekstraksi Arsip Terproteksi)
* **Tujuan**: Mengekstrak isi berkas `.zip`, `.tar`, `.tar.gz`, `.tgz` ke folder tujuan tanpa membuka aplikasi eksternal.
* **Protokol Keamanan Wajib (Security Guards)**:
  * **Zip-Slip Guard**: Memvalidasi setiap nama entri menggunakan `Path.GetFullPath`. Menolak entri dengan path traversal (`../` atau `..\`) yang mengarah ke luar folder staging.
  * **Symlink / Reparse Point Guard**: Melewati dan menolak pembuatan tautan simbolik atau file perangkat khusus yang dapat mengeksploitasi sistem berkas.
  * **Zip-Bomb Mitigation**: Batas kuota ekstraksi maksimum 100.000 entri dan ukuran total ekstraksi maksimum $2\text{ GB}$.
  * **Root Naming Deterministic**: Jika isi arsip tidak memiliki root folder tunggal, seluruh file ditempatkan ke dalam folder bernama `<nama-arsip>/`.

### 2.5. Tool: PDF Split & Multi-Page Extract
* **Tujuan**: Memecah dokumen PDF banyak halaman menjadi berkas-berkas PDF terpisah per halaman atau berdasarkan rentang tertentu.
* **Preservasi Teks & Vektor**:
  * Menggunakan parser **PdfPig** untuk mengekstrak struktur halaman asli tanpa merasterisasi teks menjadi bitmap.
  * Menghasilkan file PDF baru yang tetap mempertahankan lapisan teks yang dapat diblok, disalin (*selectable text*), dan diindeks.
* **Penamaan Output**: Folder `<nama-dokumen>/` berisi `<nama-dokumen>-01.pdf`, `<nama-dokumen>-02.pdf`, dst. (mengikuti konvensi macOS).

---

## 3. Matriks Error Handling & Pembatalan
* Setiap proses worker dijalankan dalam `Task.Run` terpisah dengan `CancellationToken`.
* Jika pengguna membatalkan (via tombol Batal di progress window atau menutup notifikasi):
  1. Pohon proses `ffmpeg.exe` atau I/O worker dimatikan seketika (`process.Kill(entireProcessTree: true)`).
  2. Direktori staging sementara di `%Temp%\Ceyloro\<guid>` dihapus bersih.
  3. Tidak ada file output parsial atau korup yang tertinggal di folder tujuan pengguna.

