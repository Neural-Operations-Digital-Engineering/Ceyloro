# PRD 06: Windows 11 Shell Context Menu Integration

**Dokumen:** `prd/06-PRD-WINDOWS-11-SHELL-EXTENSION.md`  
**Status:** Spesifikasi Teknis Lengkap  
**Komponen Terkait:** `SingleInstanceGate.cs`, `Program.cs`, `TrayApplicationContext.cs`, `Ceyloro.Windows.csproj`

---

## 1. Latar Belakang & Masalah
Meskipun drag gesture (`Shift-drag`) adalah alur interaksi paling cepat, sebagian pengguna desktop lebih terbiasa dengan menu klik kanan mouse (*context menu*) pada File Explorer.

Di Windows 11, Microsoft memperkenalkan sistem menu konteks modern baru yang menyembunyikan item-item aplikasi lama di balik submenu *"Show more options"* (Shift + F10). Untuk menyajikan pengalaman premium setara macOS Finder Services, Ceyloro memerlukan integrasi yang muncul di menu tingkat atas (*top-level modern context menu*) Windows 11 sekaligus mendukung menu klasik Windows 10.

---

## 2. Arsitektur Teknis Integrasi Shell

### 2.1. Windows 11 Modern Menu (`IExplorerCommand` + Sparse Package)
* Mengimplementasikan antarmuka COM `IExplorerCommand`:
  * `GetTitle`: *"Buka dengan Roda Ceyloro"*
  * `GetIcon`: Path ikon Ceyloro.
  * `GetState`: Diaktifkan hanya jika item yang diklik adalah berkas yang didukung.
  * `Invoke`: Mengirim daftar path berkas terpilih ke proses Ceyloro.
* **Sparse Package Identity**:
  Aplikasi WinForms *self-contained* didaftarkan menggunakan identitas paket renggang (*sparse MSIX package*) saat installer dijalankan. Hal ini memberikan hak kepada Ceyloro untuk tampil langsung di menu utama Windows 11 tanpa harus dikonversi menjadi aplikasi UWP tertutup.

### 2.2. Windows 10 Classic Shell Context Menu Fallback
Untuk Windows 10 atau saat menu klasik dipanggil, installer mendaftarkan kunci registri tingkat pengguna (`HKCU`):
```ini
[HKEY_CURRENT_USER\Software\Classes\*\shell\Ceyloro]
@="Buka dengan Roda Ceyloro"
"Icon"="\"%LOCALAPPDATA%\\Programs\\Ceyloro\\Ceyloro.exe\",0"

[HKEY_CURRENT_USER\Software\Classes\*\shell\Ceyloro\command]
@="\"%LOCALAPPDATA%\\Programs\\Ceyloro\\Ceyloro.exe\" --context-menu \"%1\""
```

---

## 3. Komunikasi Inter-Proses (IPC) via `SingleInstanceGate`

Ketika pengguna mengklik *"Buka dengan Roda Ceyloro"* dari File Explorer:
1. Shell Windows menjalankan instance baru `Ceyloro.exe --files "<path1>" "<path2>"`.
2. `SingleInstanceGate` mendeteksi bahwa instance utama Ceyloro sudah berjalan di system tray.
3. Instance baru mengirim pesan IPC (`WM_COPYDATA` atau Named Pipe lokal) berisi daftar file yang dipilih ke instance utama.
4. Instance kedua langsung keluar (*exit*) dalam waktu < 20 milidetik.
5. Instance utama yang berada di tray menerima daftar file, mendeteksi posisi kursor mouse saat itu, dan **langsung menampilkan Roda Ceyloro di posisi kursor dengan file sudah termuat**.

---

## 4. Kriteria Penerimaan Teknis (*Acceptance Criteria*)
1. Mengklik kanan file di File Explorer menampilkan item *"Buka dengan Roda Ceyloro"*.
2. Memilih item tersebut membuka roda di posisi kursor mouse dalam waktu kurang dari 200 milidetik.
3. Tidak ada jendela konsol hitam (*CMD window*) yang berkedip saat menu dipanggil.
4. Bekerja sempurna untuk pemilihan satu file maupun banyak file sekaligus (*multi-file selection*).

