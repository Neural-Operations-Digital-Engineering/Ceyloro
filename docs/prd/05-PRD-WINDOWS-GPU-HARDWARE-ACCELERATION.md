# PRD 05: GPU Hardware Acceleration Engine

**Dokumen:** `prd/05-PRD-WINDOWS-GPU-HARDWARE-ACCELERATION.md`  
**Status:** Spesifikasi Teknis Lengkap  
**Komponen Terkait:** `FfmpegCapabilities.cs`, `FfmpegCommandBuilder.cs`, `ConversionEngine.cs`

---

## 1. Latar Belakang & Masalah
Pada macOS, konversi video memanfaatkan chip akselerator bawaan Apple VideoToolbox (`h264_videotoolbox` dan `hevc_videotoolbox`). Hal ini membuat rendering video MP4/MKV berjalan sangat cepat, senyap, dan hemat baterai.

Pada Windows, implementasi baseline masih menggunakan software encoding berbasis CPU murni (`libx264` dan `libx265`). Pada perangkat laptop atau PC dengan kartu grafis terpisah maupun GPU terintegrasi, CPU mengalami lonjakan beban hingga 100% dan durasi konversi menjadi relatif lama.

Tujuan PRD ini adalah mengimplementasikan **Auto-Probing & Pemilihan Hardware GPU Encoder Otomatis** pada FFmpeg di Windows.

---

## 2. Matriks Akselerator Hardware Windows

| Vendor GPU | Encoder H.264 | Encoder H.265 (HEVC) | Parameter Kualitas Ekuivalen CRF |
| :--- | :--- | :--- | :--- |
| **NVIDIA (GeForce / RTX / Quadro)** | `h264_nvenc` | `hevc_nvenc` | `-cq 24 -preset p5 -rc:v vbr` |
| **Intel (Core i3/i5/i7/i9 / Arc / Iris Xe)** | `h264_qsv` | `hevc_qsv` | `-global_quality 24 -preset medium` |
| **AMD (Radeon / Ryzen APU)** | `h264_amf` | `hevc_amf` | `-rc cqp -qp_i 24 -qp_p 24 -quality balanced` |
| **Software Fallback (CPU)** | `libx264` | `libx265` | `-crf 23 -preset medium` |

---

## 3. Protokol Auto-Probing Saat Startup

### 3.1. Probing Aktif Tanpa File Input Pengguna
Saat Ceyloro pertama kali berjalan atau saat lokasi `ffmpeg.exe` diperbarui di Pengaturan, Ceyloro menjalankan pengujian mikro di background untuk memverifikasi apakah driver GPU merespons encoder terkait:

```bash
ffmpeg.exe -v error -f lavfi -i color=black:s=64x64:d=0.05 -c:v h264_nvenc -f null -
```
* Jika proses menghasilkan `exitCode == 0`: Tandai `h264_nvenc` sebagai **Hardware Accelerated: Aktif**.
* Jika gagal (driver tidak cocok / non-NVIDIA): Lanjutkan pengujian ke `h264_qsv`.
* Jika gagal: Lanjutkan ke `h264_amf`.
* Jika seluruh pengujian gagal: Tetapkan software encoder `libx264`.

### 3.2. Penyimpanan Hasil di `FfmpegCapabilities`
Hasil probing disimpan dalam memori objek `FfmpegCapabilities`:
* `PreferredVideoEncoder`: string nama encoder tercepat yang tersedia (misal: `"h264_nvenc"`).
* `IsHardwareAccelerated`: boolean flag.

---

## 4. Mekanisme Fallback Otomatis Saat Runtime (*Self-Healing Fallback*)
Jika saat proses konversi nyata GPU kehabisan Video RAM (VRAM) atau driver crash:
1. `ConversionEngine` menangkap error code failure dari FFmpeg.
2. Melakukan *retry* otomatis 1 kali menggunakan CPU software encoder (`libx264`).
3. Menulis peringatan di `%LocalAppData%\Ceyloro\Logs\app.log`: *"GPU encoder gagal; beralih otomatis ke CPU encoding tanpa membatalkan pekerjaan."*
4. File pengguna tetap berhasil dibuat dengan sempurna.

---

## 5. Kriteria Penerimaan Teknis (*Acceptance Criteria*)
1. Pada mesin dengan GPU NVIDIA/Intel/AMD, waktu konversi video batch menurun drastis sebesar $3\times$ hingga $5\times$ lebih cepat dibandingkan software CPU.
2. Penggunaan CPU turun dari ~100% menjadi di bawah 25% selama konversi video berjalan.
3. Parameter visual output mempertahankan ketajaman visual (*visually lossless*) setara preset CRF 23.

