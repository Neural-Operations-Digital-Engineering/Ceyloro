# PRD 00: Master Windows Parity Architecture & Engineering Roadmap

**Status:** Active Engineering Roadmap  
**Target Platform:** Windows 10 (Build 19041+) dan Windows 11  
**Target Architectures:** `win-x64` dan `win-arm64` (Native compiled)  
**Tujuan Utama:** Menjadikan shell Windows Ceyloro setara secara operasional, interaktif, performa, dan ergonomi dengan implementasi macOS, serta memaksimalkan kapabilitas sistem operasi Windows modern.

---

## 1. Analisis Gap Menyeluruh: macOS vs Windows

| Dimensi | Implementasi macOS Saat Ini | Implementasi Windows Baseline | Target Paritas Lanjutan (Roadmap Ini) |
| :--- | :--- | :--- | :--- |
| **Interaksi Utama Drag** | Menahan `Shift` + drag dari Finder membuka roda di dekat kursor. | Menahan `Shift` + drag dari Explorer membuka roda di dekat kursor (`ShiftDragMonitor.cs`). | Setara Penuh. Tambahan: gesture debounce, dynamic clamping multi-monitor, dan mixed DPI scaling. |
| **Modalitas Roda (Modes)** | 3 Mode Roda: Convert, Tools, dan Actions. | Roda tunggal (Convert + 1 tool Kompres). | **Tri-Modal Radial Wheel**: Mendukung 3 mode terpisah via modifier key (`Shift` = Convert, `Ctrl` = Tools, `Alt` = Actions) atau hub tab switch. *(Lihat PRD-01)*. |
| **Katalog Tools File** | Mode Tools: Compress, Split, Trim, Mute, Extract, Crop. | Baru tool `Kompres` yang terintegrasi. | **Katalog Tools Diperluas**: Video Mute, Video Snapshot, Audio/Video Trim, Extract Archive (zip-slip protected), dan PDF Split. *(Lihat PRD-02)*. |
| **Aksi File Sistem** | AirDrop, Copy Path, Open in Terminal, Move, Float Shelf. | Terbatas pada drag drop output biasa. | **Native File Actions**: Copy Path (UNC/WSL), Buka di Windows Terminal/PowerShell, Explorer Select, Nearby Share. *(Lihat PRD-03)*. |
| **UX Notifikasi Selesai** | macOS Notification Center Toast dengan tombol aksi. | `MessageBox.Show` konvensional. | **Windows 10/11 Action Center Toast Notifications** native dengan progress bar dan tombol `Buka File` / `Buka Folder`. *(Lihat PRD-04)*. |
| **Akselerasi GPU Media** | Apple VideoToolbox hardware encoder bawaan (`h264_videotoolbox`). | CPU-only software encoder (`libx264`). | **Hardware GPU Auto-Probing**: Deteksi NVENC (NVIDIA), QSV (Intel), dan AMF (AMD) untuk rendering batch secepat kilat. *(Lihat PRD-05)*. |
| **Integrasi Shell Explorer** | Finder Services / Quick Actions. | Ikon System Tray & Global Input Hook. | **Windows 11 Modern Context Menu (`IExplorerCommand`)** + Classic registry fallback. *(Lihat PRD-06)*. |
| **Lifecycle & Update** | Sparkle / GitHub Release JSON Updater. | `UpdateService.cs` + Inno Setup (`Ceyloro.iss`) + portable installer. | Updater otomatis background dengan validasi hash SHA-256 dan rollback staging. |

---

## 2. Diagram Arsitektur Sistem Windows

```
+-----------------------------------------------------------------------------------+
|                              Windows Desktop / Explorer                           |
|    [Shift + Drag]                 [Ctrl + Drag]                  [Alt + Drag]     |
+---------+-------------------------------+------------------------------+----------+
          |                               |                              |
          v                               v                              v
+-----------------------------------------------------------------------------------+
|                        Global Input Hook Layer (WH_MOUSE_LL)                      |
|                     ShiftDragMonitor.cs & DragGestureTracker.cs                   |
+-----------------------------------------------------------------------------------+
                                          |
                                          v (Post to UI Thread SynchronizationContext)
+-----------------------------------------------------------------------------------+
|                                 RadialOverlayForm                                 |
|   +---------------------------------------------------------------------------+   |
|   | Tri-Modal Renderer: [1. Convert Mode]  [2. Tools Mode]  [3. Actions Mode] |   |
|   | Petal Geometry Engine (RadialGeometry.cs) - Clamped per-monitor work area |   |
|   | Orange Accent (#FF9500) | Hub Dynamic Active Target Display               |   |
|   | Keyboard Navigation: [1-9] [Arrows] [Tab] [Enter] [Escape]                |   |
|   +---------------------------------------------------------------------------+   |
+-----------------------------------------------------------------------------------+
          |                               |                              |
          v                               v                              v
+-------------------+           +-------------------+          +--------------------+
|  Convert Pipeline |           |   Tools Engine    |          | File Actions Host  |
|  (ConversionEngine|           | (ToolsEngine.cs)  |          | (FileActions.cs)   |
+-------------------+           +-------------------+          +--------------------+
          |                               |                              |
          +-------------------------------+------------------------------+
                                          |
                                          v
+-----------------------------------------------------------------------------------+
|                             Capability & Engine Registry                          |
|  +--------------------+  +-------------------+  +---------------+  +------------+ |
|  | FFmpeg / FFprobe   |  | LibreOffice LO    |  | Pandoc Engine |  | GDI+ / WIC | |
|  | GPU: NVENC/QSV/AMF |  | soffice.exe       |  | pandoc.exe    |  | & PdfPig   | |
|  +--------------------+  +-------------------+  +---------------+  +------------+ |
+-----------------------------------------------------------------------------------+
                                          |
                                          v
+-----------------------------------------------------------------------------------+
|                         Atomic Job Execution & Output Pipeline                    |
|  Staging Folder Isolation -> Collision-Safe Naming -> Native Toast Notifications |
+-----------------------------------------------------------------------------------+
```

---

## 3. Sekuens Milestone Pengerjaan (Engineering Roadmap)

### Milestone WIN-Baseline (Selesai)
* [x] **WIN-0**: Freeze matriks OS target (Windows 10 2004+), compiler .NET 10, target arsitektur `win-x64` dan `win-arm64`.
* [x] **WIN-1**: Shift-drag gesture monitor (`WH_MOUSE_LL`) terintegrasi dengan OLE drag data receiver.
* [x] **WIN-2**: Roda radial beraksen oranye, hub target dinamis, dan navigasi keyboard 1–9.
* [x] **WIN-3**: FFmpeg LGPL per arsitektur dan PDF smart compression (PdfPig text-layer preservation).
* [x] **WIN-4**: Tool kompresi pertama aktif (gambar, video, audio, PDF, dokumen).
* [x] **WIN-5**: Auto-Updater (`UpdateService.cs`) dan Inno Setup / PowerShell Installer.
* [x] **WIN-6**: CI workflow release desktop dengan SHA-256 checksums dan Authenticode signing.

---

### Milestone WIN-Lanjutan (Tahap Implementasi Sekarang)

### WIN-8: Tri-Modal Radial Wheel Engine
* Memisahkan roda menjadi 3 mode terisolasi: **Convert**, **Tools**, dan **Actions**.
* Deteksi modifier global saat drag: `Shift` (Convert), `Ctrl` (Tools), `Alt` (Actions).
* Hub switcher interaktif (tombol klik atau shortcut Tab) untuk beralih mode jika drag diinisiasi tanpa modifier khusus.
* Petal rendering dengan ikon garis monokrom resolusi tinggi (DPI-aware).

### WIN-9: Extended Tools Catalog Engine
* **Tool: Video Mute**: Ekstraksi video tanpa track audio instan via `-c:v copy -an`.
* **Tool: Video Snapshot**: Mengambil 1 frame gambar lossless (PNG/JPG) dari posisi durasi video.
* **Tool: Media Trim**: Pemotongan durasi audio/video lossless berbasis keyframe.
* **Tool: Archive Extraction**: Ekstraktor ZIP, TAR, dan TAR.GZ dengan proteksi path traversal, symlink, dan batas ukuran 2 GB.
* **Tool: PDF Split**: Pemecah berkas PDF per halaman atau berdasarkan rentang halaman kustom tanpa merusak lapisan teks vektor.

### WIN-10: Native Windows File Actions
* **Action: Copy Path**: Salin path file tunggal / multi-file ke clipboard dalam format Windows standar, Quoted, atau WSL path.
* **Action: Buka di Windows Terminal / PowerShell**: Deteksi terminal aktif dan navigasi langsung ke direktori file.
* **Action: Buka Folder & Sorot File**: Meluncurkan `explorer.exe /select,"<path>"`.
* **Action: Nearby Sharing**: Memanggil antarmuka Windows Share Charm native.

### WIN-11: Modern Notifications & Hardware GPU Acceleration
* Mengganti seluruh `MessageBox.Show` konversi dengan **Windows Action Center Toast Notifications**.
* Menambahkan tombol interaktif `[ Buka File ]` dan `[ Buka Folder ]` langsung di toast.
* Probing otomatis GPU encoder di FFmpeg: deteksi `h264_nvenc` (NVIDIA), `h264_qsv` (Intel), `h264_amf` (AMD) dan beralih otomatis dari CPU `libx264`.

### WIN-12: Windows 11 Shell Context Menu Integration
* Implementasi Windows 11 Modern Context Menu via Sparse MSIX Package / `IExplorerCommand`.
* Integrasi Classic Context Menu (Windows 10 / Shift+F10) melalui registri HKCU Shell.
* Komunikasi inter-proses ke instance tray Ceyloro yang sedang berjalan via `SingleInstanceGate`.

---

## 4. Standar Kualitas & Kriteria Selesai (*Definition of Done*)
1. Tidak ada dependensi eksternal runtime yang harus diinstal manual oleh pengguna (.NET runtime sudah *self-contained*).
2. Sumber file asli pengguna 100% aman (tidak pernah dimodifikasi, ditimpa, atau dihapus).
3. Setiap operasi memiliki penanganan pembatalan (*cancellation token*) instan tanpa file sampah tersisa.
4. Kompatibel penuh dan teruji lulus kompilasi untuk `win-x64` dan `win-arm64`.

