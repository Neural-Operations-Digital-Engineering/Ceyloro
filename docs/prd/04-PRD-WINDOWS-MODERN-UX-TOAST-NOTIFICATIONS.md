# PRD 04: Modern Toast Notification Engine

**Dokumen:** `prd/04-PRD-WINDOWS-MODERN-UX-TOAST-NOTIFICATIONS.md`  
**Status:** Spesifikasi Teknis Lengkap  
**Komponen Terkait:** `NotificationService.cs`, `RadialOverlayForm.cs`, `ConversionEngine.cs`

---

## 1. Masalah & Tujuan
Saat ini, shell Windows menggunakan `MessageBox.Show` modal untuk memberikan feedback kepada pengguna setelah konversi selesai (*"Selesai: 2 file MP4 dibuat. Buka folder hasil? Yes/No"*). 

Kelemahan pendekatan modal:
* Memblokir alur kerja pengguna (harus menekan tombol dialog sebelum bisa berinteraksi dengan aplikasi lain).
* Terlihat kuno dan tidak selaras dengan pengalaman visual modern Windows 10/11 maupun macOS Notification Center.

Tujuan PRD ini adalah mengganti seluruh dialog modal dengan **Windows Action Center Toast Notifications** native yang elegan, non-intrusif, dan interaktif.

---

## 2. Struktur Visual & Template Notifikasi Toast

### 2.1. Notifikasi Sukses Batch (Success Toast)
```
+-------------------------------------------------------------+
| [Ceyloro Icon]  Ceyloro • Baru saja                         |
| Konversi Selesai                                            |
| 3 berkas MP4 berhasil dibuat di folder 'Videos'.            |
|                                                             |
| [ Buka File ]          [ Buka Folder ]          [ Tutup ]   |
+-------------------------------------------------------------+
```
* **Header**: Ikon resmi Ceyloro + Judul *"Ceyloro"*.
* **Title**: *"Konversi Selesai"* atau *"Kompresi Selesai"*.
* **Body**: Rincian jumlah file output dan folder tujuan.
* **Tombol Interaktif (Inline Action Buttons)**:
  * **`Buka File`**: Meluncurkan file hasil pertama dengan aplikasi default OS.
  * **`Buka Folder`**: Membuka Windows Explorer ke folder output dengan file terseleksi.
  * **`Tutup`**: Menutup notifikasi.

### 2.2. Notifikasi Sebagian Gagal (Partial Failure Toast)
```
+-------------------------------------------------------------+
| [Ceyloro Warning Icon]  Ceyloro • Baru saja                 |
| Konversi Selesai dengan Catatan                             |
| 2 file berhasil, 1 file gagal (video-corrupt.mp4).          |
|                                                             |
| [ Buka Folder ]        [ Lihat Log ]            [ Tutup ]   |
+-------------------------------------------------------------+
```
* Tombol **`Lihat Log`**: Membuka berkas `%LocalAppData%\Ceyloro\Logs\app.log` di Notepad untuk memudahkan diagnosis tanpa membuka terminal.

---

## 3. Implementasi Teknis Tanpa Dependensi Berat

### 3.1. Penggunaan WinRT Native Windows AppNotification
Shell Windows Ceyloro menargetkan `net10.0-windows10.0.19041.0`. Target framework ini sudah memiliki akses langsung ke API namespace `Windows.UI.Notifications` tanpa memerlukan pustaka NuGet tambahan pihak ketiga.

### 3.2. Struktur XML Toast Payload
```xml
<toast duration="short" scenario="default">
  <visual>
    <binding template="ToastGeneric">
      <text hint-maxLines="1">Konversi Selesai</text>
      <text>{summaryText}</text>
      <image placement="appLogoOverride" hint-crop="circle" src="{logoPath}" />
    </binding>
  </visual>
  <actions>
    <action content="Buka File" arguments="action=openFile&amp;path={firstOutputPath}" activationType="background" />
    <action content="Buka Folder" arguments="action=openFolder&amp;path={folderPath}" activationType="background" />
  </actions>
</toast>
```

### 3.3. Penanganan Event Klik & Argumen (Activation Listener)
Aplikasi mendaftarkan event handler `OnNotificationActivated` di `NotificationService.cs`. Ketika pengguna mengklik tombol di notifikasi:
* Jika argumen `action=openFolder`: jalankan `Process.Start("explorer.exe", folderPath)`.
* Jika argumen `action=openFile`: jalankan `Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true })`.

---

## 4. Kriteria Penerimaan Teknis (*Acceptance Criteria*)
1. Tidak ada dialog pop-up `MessageBox.Show` yang memblokir layar saat batch selesai.
2. Mengklik tombol *"Buka Folder"* pada banner notifikasi langsung membuka folder Windows Explorer secara instan.
3. Notifikasi tersimpan di Windows Action Center jika pengguna sedang sibuk (*Focus Assist / Do Not Disturb*) dan dapat diakses kembali kemudian.

