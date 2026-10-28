# PRD 01: Tri-Modal Radial Wheel Engine

**Dokumen:** `prd/01-PRD-WINDOWS-TRI-MODAL-WHEEL.md`  
**Status:** Spesifikasi Teknis Lengkap  
**Komponen Terkait:** `RadialOverlayForm.cs`, `RadialGeometry.cs`, `ShiftDragMonitor.cs`, `DragGestureTracker.cs`

---

## 1. Latar Belakang & Masalah
Pada desain awal Windows, seluruh item diletakkan dalam satu roda tunggal sehingga format konversi dan alat (tools) bercampur. Di macOS dan Master Scope produk Ceyloro, roda memiliki 3 modalitas berbeda yang disesuaikan dengan niat pengguna (*user intent*):
1. **Mode Konversi (Convert Mode)**: Mengubah ekstensi/format file menjadi format lain (misal: PNG $\rightarrow$ JPG, MP4 $\rightarrow$ MP3).
2. **Mode Alat (Tools Mode)**: Memproses isi file tanpa mengubah format dasarnya (misal: Compress, Video Mute, Video Snapshot, PDF Split, Extract).
3. **Mode Tindakan File (Actions Mode)**: Melakukan aksi terhadap file dalam ekosistem sistem operasi (misal: Salin Path, Buka di PowerShell, Buka Folder, Nearby Share).

PRD ini menspesifikasikan pemisahan 3 mode tersebut secara bersih di shell Windows.

---

## 2. Model Interaksi & Pemicu Mode

### 2.1. Pemicu Berbasis Tombol Modifier Saat Drag
Saat pengguna memulai drag file dari File Explorer, `ShiftDragMonitor` mendeteksi kombinasi tombol modifier yang sedang ditekan:

| Modifier Keyboard | Mode Roda yang Dibuka | Keterangan |
| :--- | :--- | :--- |
| **`Shift` + Drag** | **Convert Mode** | Mode bawaan konversi format file. Petal berisi ekstensi target yang kompatibel. |
| **`Ctrl` + Drag** | **Tools Mode** | Mode alat pengolahan media dan berkas (Kompres, Mute, Snapshot, Split, Extract). |
| **`Alt` + Drag** | **File Actions Mode** | Mode aksi shell sistem (Salin Path, Terminal/PowerShell, Buka Folder, Bagikan). |
| **Drag Tanpa Modifier (via Hotkey `Ctrl+Alt+Space`)** | **Dynamic Contextual Mode** | Membuka roda pada mode terakhir atau mode Convert dengan tab pemilih di hub tengah. |

### 2.2. Hub Mode Switcher (Interaktif Tanpa Drag Ulang)
Di pusat roda (hub), disediakan tombol/indikator tab mikro yang memungkinkan pengguna beralih mode secara langsung menggunakan:
* **Tombol `Tab` pada Keyboard**: Berpindah secara sekuensial: `Convert` $\rightarrow$ `Tools` $\rightarrow$ `Actions` $\rightarrow$ `Convert`.
* **Klik pada Pill Indikator Hub**: Terdapat 3 titik/label kecil di hub atas: `[Konversi]` • `[Alat]` • `[Aksi]`.

---

## 3. Spesifikasi Visual & Geometri Roda

### 3.1. Ukuran & Radius
* **Radius Dalam (Inner Radius)**: $66\text{ px}$ (batas luar hub tengah).
* **Radius Luar (Outer Radius)**: $154\text{ px}$ (batas luar petal).
* **Gap Antar-Petal**: $7^\circ$ (memberikan celah pemisah yang tegas antar sektor).
* **Maksimum Petal Per Sektor**: Maksimal 10 petal per layer. Jika pilihan melebihi 10, target dengan prioritas frekuensi tertinggi ditampilkan terlebih dahulu.

### 3.2. Skema Warna (*Color Palette*)
* **Petal Aktif (Hovered / Selected)**:
  * Latar: `#FF8C00` / `#FF9500` (Aksen Oranye Ceyloro).
  * Border: `#FFE0B2` (Lebar $1.5\text{ px}$).
  * Teks: Putih `#FFFFFF` (Segoe UI 10pt Bold).
* **Petal Netral (Unselected)**:
  * Latar: `rgba(38, 38, 38, 0.92)` (Abu-abu gelap pekat).
  * Border: `rgba(255, 255, 255, 0.28)` (Lebar $1.0\text{ px}$).
  * Teks: Putih `#FFFFFF` (Segoe UI 9.5pt Semibold).
* **Hub Pusat**:
  * Latar: `rgba(22, 22, 22, 0.96)`.
  * Border: `rgba(255, 255, 255, 0.24)` (Lebar $1.5\text{ px}$).
  * Teks Target Aktif: Oranye `#FFA500` (Segoe UI 12pt Bold).
  * Teks Subjudul: `rgba(220, 220, 220, 0.85)` (Segoe UI 8pt Regular).

---

## 4. Navigasi Keyboard Lengkap (Aksesibilitas)

| Tombol Keyboard | Aksi |
| :--- | :--- |
| **`1` s/d `9`** | Langsung memilih petal nomor 1 hingga 9 sesuai urutan jarum jam dari jam 12. |
| **Panah Kanan / Panah Bawah** | Berpindah ke petal berikutnya (searah jarum jam). |
| **Panah Kiri / Panah Atas** | Berpindah ke petal sebelumnya (berlawanan jarum jam). |
| **`Tab`** | Berpindah ke Mode Roda berikutnya (`Convert` $\rightarrow$ `Tools` $\rightarrow$ `Actions`). |
| **`Enter` / `Return`** | Mengeksekusi target/alat yang sedang dipilih terhadap file yang di-drag. |
| **`Escape`** | Menutup roda seketika tanpa melakukan aksi apapun (*Clean cancel*). |

---

## 5. Kriteria Penerimaan Teknis (*Acceptance Criteria*)
1. Menahan `Ctrl` saat drag menampilkan petal tools, bukan daftar format konversi.
2. Menahan `Alt` saat drag menampilkan aksi shell native, bukan daftar format konversi.
3. Roda tidak pernah mencampur target format (misal `.mp4`) dengan aksi file (misal `Salin Path`) dalam satu lingkaran petal yang sama.
4. Hit-test sektor matematika (`RadialGeometry.HitTest`) konsisten 100% antara rendering grafis GDI+ dan deteksi titik kursor mouse.

