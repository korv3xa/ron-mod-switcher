===============================================================================
  RoN Mod Switcher v1.0.0 — Ready or Not Mod Yöneticisi & Vanilla Geçiş Aracı
  Vosien Studio
===============================================================================

GENEL BAKIŞ
-----------
RoN Mod Switcher, Ready or Not için hem Nexus Mods (~mods) hem de oyun içi
mod.io modlarını gigabaytlarca .pak dosyasını kopyalamadan yönetmenizi sağlar.
Aynı disk üzerinde anlık taşıma sayesinde saniyeler içinde Modlu ve temiz
Vanilla Online modları arasında geçiş yapabilirsiniz.


ÖNE ÇIKAN ÖZELLİKLER
--------------------
1. Anlık Vanilla <-> Modlu Geçişi (Kopyalama Yok)
   - ~mods, Paks\mod.io ve C:\Users\Public\mod.io\8254 dizinlerini oyun
     dışına alıp tek tıkla geri getirir.

2. Steam & Epic Games Desteği
   - Steam ve Epic Games kurulumlarını otomatik algılar; isterseniz manuel
     oyun klasörü seçebilirsiniz.

3. Hazır ve Özel Profiller
   - Co-Op (Modlu Lobi): Seçtiğiniz co-op modlarını açar ve lobi uyumu için
     Server Side Checksum ayarını otomatik kapatır.
   - Solo (Tam Modlu): Tek oyunculu tam mod paketi; yeni kurulan modları
     otomatik ekler.
   - Vanilla (Online): Tüm modları ve mod.io önbelleğini kenara alır, herkese
     açık lobi için Checksum ayarını yeniden aktif eder.

4. Co-Op Mod Listesi Paylaşma & Karşılaştırma
   - Herhangi bir profili RONMODS1: paylaşım kodu ve okunabilir liste olarak
     tek tıkla kopyalayın.
   - Arkadaşınızın paylaştığı RONMODS1: kodunu veya .pak listesini yapıştırıp
     anında karşılaştırın:
     * Eşleşenler
     * Sizde eksik olanlar (indirilmesi gerekenler)
     * Sizde fazladan açık olanlar (lobi öncesi kapatılması gerekenler)
   - Eşleşen modlarla Co-Op profilini tek tıkla senkronlayın.

5. Akıllı UE5 Mod Yöneticisi & Paket Sağlığı Uyarıları
   - .pak / .ucas / .utoc / .sig (IoStore) paketlerini tek bir mod birimi
     olarak gruplar ve birlikte açıp kapatır.
   - Arama ve filtreleme + filtrelenmiş modlar için Tümünü Aç / Tümünü Kapat.
   - Pencereye .pak, .ucas, .utoc, .sig, .zip arşivi veya mod klasörü
     sürükleyip bırakarak anında kurulum.
   - Bozuk/eksik paket uyarısı: 0 KB boş dosyaları, eksik .utoc / .ucas
     eşlikçilerini ve boş klasörleri rozetle işaretler.
   - Güvenli silme modu Windows Geri Dönüşüm Kutusu'na gönderir.

6. Ayrıntılı mod.io Görüntüleme
   - Oyun içi mod.io aboneliklerini (metadata\state.json), Paks\mod.io
     içeriklerini ve cache\mods önbelleğini boyutlarıyla listeler.

7. Güvenli GameUserSettings.ini Düzenleme & Yedekten Geri Yükleme
   - İlk değişiklik öncesinde otomatik .ronswitch.bak yedeği oluşturur.
   - Ayarlar sekmesinden tek tıkla orijinal INI dosyanızı geri yükleyebilirsiniz.

8. Hızlı Başlatma Seçici (Launcher | DX11 | DX12) & Güvenlik Korumaları
   - Doğrudan Kontrol Merkezi'nden Launcher, DirectX 11 veya DirectX 12
     seçimi yapıp oyunu başlatın.
   - Oyun çalışırken dosya bozulmasını önleyen kilit koruması ve tek örnek
     (single-instance) kilidi içerir.
   - Türkçe / İngilizce (TR / EN) arayüz desteği.


KULLANIM
--------
1. RoNModSwitcher.exe dosyasını istediğiniz bir klasöre çıkarın (kurulum yok).
2. Mod değiştirmeden önce Ready or Not'ın kapalı olduğundan emin olun.
3. RoNModSwitcher.exe'yi çalıştırıp Co-Op, Solo veya Vanilla profilini seçin.
4. Tekil Mod Yönetimi sekmesinden modları açıp kapatın, toplu işlem yapın veya
   yeni .pak/.zip dosyalarını sürükleyip bırakın.


DESTEK & TOPLULUK
-----------------
Vosien Studio Discord: https://discord.gg/29bndFuds3
