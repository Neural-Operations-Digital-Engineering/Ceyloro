# PRD 03: Native Windows File Actions

**Dokumen:** `prd/03-PRD-WINDOWS-NATIVE-FILE-ACTIONS.md`  
**Status:** Spesifikasi Teknis Lengkap  
**Komponen Terkait:** `FileActionsHost.cs`, `RadialOverlayForm.cs`

---

## 1. Latar Belakang & Tujuan
Pada macOS, pengguna Finder terbiasa menggunakan aksi file seperti *AirDrop*, *Copy Path*, dan *Open in Terminal*. Windows memiliki ekosistem shell yang sangat kaya (PowerShell, Windows Terminal, Windows Explorer, dan Nearby Sharing).

PRD ini mendefinisikan implementasi **Mode Aksi File (Actions Mode)** pada radial wheel Windows yang memberikan utilitas harian instan bagi pengguna.

---

## 2. Katalog Aksi File Native Windows

### 2.1. Action: Salin Path (Copy Path)
* **Deskripsi**: Menyalin path lengkap file ke clipboard Windows.
* **Perilaku Multi-File**: Jika beberapa file di-drag, setiap path dipisahkan oleh baris baru (`\r\n`).
* **Format yang Didukung (Berdasarkan Preferensi Settings)**:
  1. **Standard Windows Path**: `C:\Users\Documents\File.pdf`
  2. **Quoted Path (Siap Pakai di Shell)**: `"C:\Users\Documents\File.pdf"`
  3. **Unix / Forward-Slash Path**: `C:/Users/Documents/File.pdf`
  4. **WSL Path (Linux Subsystem)**: `/mnt/c/Users/Documents/File.pdf`
* **Feedback**: Menampilkan toast mini atau tooltip tray: *"Path disalin ke clipboard."*

### 2.2. Action: Buka di Terminal / PowerShell
* **Deskripsi**: Membuka antarmuka baris perintah langsung ke direktori lokasi file yang dipilih.
* **Prioritas Deteksi Terminal**:
  1. **Windows Terminal (`wt.exe`)**: Jika terpasang di sistem (`Get-Command wt.exe`), jalankan:
     ```powershell
     wt.exe -d "<direktori-file>"
     ```
  2. **PowerShell 7 (`pwsh.exe`)**: Jika terpasang, buka direktori dengan PowerShell modern.
  3. **Windows PowerShell Bawaan (`powershell.exe`)**: Fallback standar dengan argumen `-NoExit -Command "Set-Location '<dir>'"`
  4. **Command Prompt (`cmd.exe`)**: Fallback dasar `/K cd /d "<dir>"`.

### 2.3. Action: Buka Folder & Sorot File (Reveal in Explorer)
* **Deskripsi**: Membuka Windows Explorer dan langsung menyorot (*highlight/select*) file terkait.
* **Implementasi API Native**:
  ```csharp
  Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
  ```
* **Kelebihan**: Pengguna tidak hanya membuka folder induknya, tetapi kursor langsung memilih file tersebut tanpa perlu mencarinya secara manual di antara ratusan file lain.

### 2.4. Action: Salin File / Pindahkan File (Clipboard HDROP)
* **Deskripsi**: Memasukkan file ke dalam clipboard Windows menggunakan format native `CF_HDROP` sehingga pengguna dapat langsung menekan `Ctrl + V` di folder tujuan manapun.
* **Dukungan Operasi**:
  * **Salin (Copy)**: `Preferred DropEffect` diset ke `DROPEFFECT_COPY` (1).
  * **Pindahkan (Cut / Move)**: `Preferred DropEffect` diset ke `DROPEFFECT_MOVE` (2).

### 2.5. Action: Bagikan (Windows Nearby Share / Share Charm)
* **Deskripsi**: Padanan native dari AirDrop di Windows. Membuka antarmuka berbagi Windows 10/11 untuk mengirim file ke perangkat Windows terdekat via Bluetooth/Wi-Fi Direct atau aplikasi terinstal.
* **Implementasi Native WinRT**:
  Memanggil `Windows.ApplicationModel.DataTransfer.DataTransferManager.ShowShareUI()` dengan payload `StorageFile` dari berkas yang dipilih.

---

## 3. Kriteria Penerimaan Teknis (*Acceptance Criteria*)
1. Aksi *Salin Path* bekerja seketika (< 50 milidetik) dan langsung mengisi clipboard tanpa dialog yang mengganggu.
2. Membuka terminal secara otomatis memilih Windows Terminal jika tersedia di Windows 10/11.
3. Operasi aksi file tidak pernah memodifikasi isi berkas.

