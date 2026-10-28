# Ceyloro Windows Deep Technical PRD & Architecture Roadmap

Dokumen ini dan berkas-berkas di dalam folder `prd/` merupakan spesifikasi rekayasa teknis, arsitektur sistem, dan roadmap mendalam untuk membawa shell Windows Ceyloro mencapai kesetaraan 100% (*feature & behavioral parity*) dengan versi macOS serta melampauinya dengan memanfaatkan keunggulan native Windows 10/11 (x64 dan ARM64).

Folder ini menjadi **satu sumber kebenaran (Single Source of Truth / SSOT)** permanen dalam repositori agar tidak ada kehilangan konteks arsitektural di sesi masa depan.

---

## Indeks Dokumen PRD

| Dokumen | Judul | Fokus & Cakupan Utama | Status |
| :--- | :--- | :--- | :--- |
| [**`00-MASTER-WINDOWS-PARITY-ROADMAP.md`**](00-MASTER-WINDOWS-PARITY-ROADMAP.md) | **Master Roadmap & Arsitektur Paritas** | Analisis gap komprehensif, arsitektur thread, capability registry, pipeline konversi, dan sekuens milestone rilis (WIN-8 s/d WIN-12). | Baseline Selesai, Tahap Lanjutan Aktif |
| [**`01-PRD-WINDOWS-TRI-MODAL-WHEEL.md`**](01-PRD-WINDOWS-TRI-MODAL-WHEEL.md) | **Tri-Modal Radial Wheel Engine** | Arsitektur 3 mode roda (*Convert*, *Tools*, *Actions*), modifier hook (`Shift`, `Ctrl`, `Alt`), hub switcher dinamis, hit-testing presisi, dan aksesibilitas keyboard. | Spesifikasi Lengkap |
| [**`02-PRD-WINDOWS-EXTENDED-TOOLS-CATALOG.md`**](02-PRD-WINDOWS-EXTENDED-TOOLS-CATALOG.md) | **Katalog Tools File Lanjutan** | Implementasi Video Mute, Video Snapshot, Audio/Video Trim, Archive Extraction aman (zip-slip guard), dan PDF Split/Organize preservasi teks. | Spesifikasi Lengkap |
| [**`03-PRD-WINDOWS-NATIVE-FILE-ACTIONS.md`**](03-PRD-WINDOWS-NATIVE-FILE-ACTIONS.md) | **Aksi File Native Windows** | Copy Path (UNC/WSL/Windows format), Buka di Windows Terminal/PowerShell, Explorer Select, Clipboard HDROP, dan integrasi Nearby Sharing. | Spesifikasi Lengkap |
| [**`04-PRD-WINDOWS-MODERN-UX-TOAST-NOTIFICATIONS.md`**](04-PRD-WINDOWS-MODERN-UX-TOAST-NOTIFICATIONS.md) | **Modern Toast Notification Engine** | Menggantikan MessageBox dengan Windows 10/11 Action Center Toasts native, progress bar interaktif, dan tombol aksi `Buka File`/`Buka Folder`. | Spesifikasi Lengkap |
| [**`05-PRD-WINDOWS-GPU-HARDWARE-ACCELERATION.md`**](05-PRD-WINDOWS-GPU-HARDWARE-ACCELERATION.md) | **Akselerasi Hardware GPU (NVENC/QSV/AMF)** | Probing dan pemanfaatan GPU encoder otomatis di FFmpeg untuk lonjakan kecepatan rendering video batch setara Apple VideoToolbox. | Spesifikasi Lengkap |
| [**`06-PRD-WINDOWS-11-SHELL-EXTENSION.md`**](06-PRD-WINDOWS-11-SHELL-EXTENSION.md) | **Integrasi Context Menu Explorer Windows 11** | Sparse package / `IExplorerCommand` modern context menu dan fallback classic shell registry menu. | Spesifikasi Lengkap |

---

## Prinsip Desain Kunci (*Core Engineering Tenets*)

1. **Local-First & Zero Telemetry**: Seluruh operasi pemrosesan file berjalan 100% lokal di mesin pengguna tanpa ketergantungan cloud.
2. **Safe By Default**: File sumber asli tidak boleh ditimpa, dihapus, atau dimodifikasi secara diam-diam. Output selalu memiliki penamaan unik anti-konflik dan staging terisolasi.
3. **Non-Blocking Drag Loop**: Hook mouse global (`WH_MOUSE_LL`) dan event OLE drag-and-drop harus secepat mungkin menyerahkan beban berat ke background thread agar drag loop Explorer tidak mengalami lag atau stutter.
4. **Hardware-Aware Adaptive Execution**: Deteksi kapabilitas CPU, GPU, dan memori untuk mengalokasikan thread worker dan akselerator hardware secara optimal.
5. **Cross-Architecture Parity (x64 & ARM64)**: Seluruh biner dan pustaka harus dikompilasi secara native untuk arsitektur target tanpa mengandalkan lapisan emulasi overhead.

