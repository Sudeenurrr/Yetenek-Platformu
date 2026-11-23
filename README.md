# Yetenek Paylaşım ve Randevu Platformu

Kullanıcıların yeteneklerini paylaştığı, öğrenmek isteyenlerin randevu alarak onlarla buluştuğu bir web uygulaması. Platform gönüllülük esaslıdır; ücret zorunluluğu yoktur, ders sonrası gönüllü ödeme benzetimi vardır.

**ASP.NET Core Razor Pages** ve **Microsoft SQL Server** ile geliştirilmiştir.

---

## Özellikler

### Üyelik
- **Kayıt:** ad, soyad, şifre, telefon, IBAN ve şehir ile. E-posta istenmez.
  - Telefon formda `+90 5` sabit olarak girilir, `05XXXXXXXXX` olarak saklanır.
  - IBAN gerçek değildir: `TR` + 7 rakam (ör. `TR1234567`).
  - Telefon, IBAN ve ad+soyad birleşimi her üyede benzersizdir.
- **Giriş:** ad, soyad ve şifre ile.
- **Kurtarma kodu:** Kayıt sonunda bir kez gösterilen 12 karakterlik kod (veritabanında yalnızca karıştırılmış hali tutulur). Profilden yenilenebilir.
- **Şifremi unuttum:** Ad, soyad ve kurtarma koduyla yeni şifre belirlenir.
- **Üyelik silme:** Üye, şifresini girerek üyeliğini siler. Kişisel bilgiler tamamen silinir, aktif randevular iptal edilir; geçmiş ders ve ödeme kayıtlarında "Silinmiş Üye" görünür.

### Profil
- Kişisel bilgiler (şehir değiştirilebilir), yetenekler, verdiğim dersler, aldığım dersler ve favoriler sekmeleri.
- **Yetenek ekleme:** Arama kutusuna yazdıkça eşleşen yetenekler listelenir (otomatik tamamlama).

### Arama
- **Yetenek Ara:** Seçilen yeteneği ekleyen üyeler, o yetenekte tamamladıkları toplam ders saatine göre sıralanır.
- **Kişi Ara:** Üyeler ad veya soyadla aranır (Türkçe büyük/küçük harf kurallarıyla), yazdıkça öneri gösterilir.
- Her iki aramada **şehir filtresi** vardır. Sonuçlar en fazla 50 üyedir.
- Beğenilen üyeler **favorilere** eklenir.

### Randevu
- **Randevu Al:** Üyenin profilinden, eklediği yeteneklerden biri için.
  - Takvimden gün seçilince saatler sunucudan gelir: Boş, Dolu, "Bu saatte başka randevunuz var", Geçti.
  - Saatler 1 saatlik dilimlerdir, art arda 2 saat seçilebilir. Müsait saatler 09:00–21:00, en fazla 30 gün sonrası.
  - **Online** ya da **Yüz Yüze** seçilir. Yüz Yüze yalnızca iki üyenin şehri aynıysa seçilebilir; buluşma yeri OpenStreetMap Nominatim ile aranır ya da elle yazılır.
- **Randevu İstekleri / Gönderdiğim Randevular:** Talepler listelenir; zaman (Tümü / Yaklaşan / Geçmiş) ve duruma göre filtrelenebilir.
- **Onay ve red:** Talebi alan üye onaylar ya da reddeder. Online randevuda onaylarken görüşme linkini (https) girer.
- **İptal ve geri çekme:** Onaylı randevu iki taraf da ders başlayana kadar iptal edebilir (isteğe bağlı neden). Bekleyen talebi gönderen geri çekebilir.
- **Değişiklik önerisi:** Onaylı randevu için yeni gün/saat/süre ve (Yüz Yüze ise) yeni buluşma yeri önerilebilir. Karşı taraf kabul eder ya da reddeder.
- **Bekleme listesi:** Dolu saat için sıraya girilir. Saat boşalınca sıradaki kişinin talebi otomatik oluşur.
- **Durum değişikliği göstergesi:** Görülmemiş bir değişiklik olduğunda menüde kırmızı nokta çıkar.

### Ders sonrası gönüllü ödeme (benzetim)
- Tamamlanan ders için öğrenci, eğitmene **200, 250, 300, 400 veya 500 TL** seçerek ödeme kaydı oluşturabilir. Ders başına en fazla bir ödeme yapılır.
- Gerçek para aktarılmaz; yalnızca kayıt oluşturulur.
- Profilde "Toplam kazancım" (Verdiğim Dersler) ve "Toplam ödemem" (Aldığım Dersler) gösterilir.

### Otomatik durum güncelleme
Arka planda çalışan bir görev (`AppointmentStatusWorker`) belirli aralıklarla:
- bitiş saati geçen onaylı randevuları **Tamamlandı**,
- 24 saat içinde yanıtlanmayan talepleri **Süresi Doldu** yapar,
- süresi dolan değişiklik önerilerini düşürür, bekleme listesini günceller.

---

## Roller

Uygulama açılırken `Uye` ve `Admin` rolleri veritabanında oluşturulur. Kayıt olan her kullanıcı **Üye** rolündedir. `Admin` rolü tanımlıdır; uygulamanın bu sürümünde admin hesabı ve admin paneli bulunmaz.

Öğrenci ve eğitmen ayrı kullanıcı türleri değildir: her üye hem ders verebilir hem ders alabilir. Bir randevuda talebi gönderen öğrenci, talebi alan eğitmendir.

---

## Güvenlik ve iş kuralları

- **Çakışma kontrolü:** Aynı saat aynı anda iki kişiye verilemez. Her dolu saat `TimeSlots` tablosunda ayrı satırdır ve `UserId + SlotStart` benzersiz kısıtıyla veritabanı seviyesinde korunur.
- **Eşzamanlı işlemler:** Aynı randevuya aynı anda iki işlem gelirse `RowVersion` ile yalnızca biri geçer. Birbirine bağlı kayıtlar tek transaction içinde yazılır.
- **Brute-force koruması:** Art arda 3 yanlış şifrede hesap 2 dakika kilitlenir.
- **Kişisel verilerin görünürlüğü** (sunucuda uygulanır):
  - Telefon yalnızca aralarında onaylı randevu olan iki tarafa gösterilir.
  - IBAN yalnızca tamamlanmış dersin öğrencisine, ödeme sayfasında gösterilir. Kullanıcı kendi IBAN'ını profilinde görür.
  - Arama, favoriler ve üye profili sayfalarına telefon ve IBAN gönderilmez.
- Bütün iş kuralları (yetki, durum geçişleri, saat kuralları, tutar kontrolü) sunucu tarafında doğrulanır.

---

## Kullanılan teknolojiler

| İşlev | Teknoloji |
|---|---|
| Sunucu / iş mantığı | C#, ASP.NET Core Razor Pages (.NET 10) |
| Veritabanı | Microsoft SQL Server (LocalDB), Entity Framework Core (Code First, migration) |
| Kimlik doğrulama / yetki | ASP.NET Core Identity |
| Arayüz | HTML, CSS, JavaScript, Bootstrap, jQuery (doğrulama) |
| Sorgular, sıralama ve toplama | LINQ |
| Zamanlanmış görevler | ASP.NET Core BackgroundService |
| Konum arama | OpenStreetMap Nominatim |
| Yetenek veri seti | Wikidata (`Data/Seed/skills.csv`) |

---

## Veritabanı

Veritabanı EF Core Code First ile oluşturulur. Tablolar:

| Tablo | İçerik |
|---|---|
| `AspNetUsers` | Kullanıcılar (Identity tablosu; ad, soyad, telefon, IBAN, şehir, kurtarma kodu alanları eklenmiştir) |
| `AspNetRoles`, `AspNetUserRoles` | Roller (`Uye`, `Admin`) ve kullanıcı-rol ilişkisi |
| `Skills` | Yetenekler |
| `UserSkills` | Kullanıcı-yetenek ilişkisi (çoka-çok) |
| `Appointments` | Randevular (durum, görüşme türü, link, buluşma yeri, değişiklik önerisi alanları) |
| `TimeSlots` | Dolu saatler (çakışma kontrolü) |
| `WaitlistEntries` | Bekleme listesi |
| `Favorites` | Favoriler |
| `Payments` | Gönüllü ödeme kayıtları |

**Randevu durumları:** Beklemede, Onaylandı, Reddedildi, İptal Edildi, Değişiklik Önerildi, Tamamlandı, Süresi Doldu.

**Migration'lar (8 adet):** `InitialCreate`, `AllowNullUserOnHistory`, `IbanWithCountryCode`, `SkillNameTurkishCollation`, `UniquePhoneAndIban`, `AddRecoveryCode`, `AddProposedAt`, `AddProposedPlace`.

---

## Proje yapısı

```
YetenekPlatformu/
├── Data/            # DbContext, roller, yetenek veri seti ve aktarımı, il listesi
├── Migrations/      # EF Core migration'ları
├── Models/          # Veritabanı tabloları (entity sınıfları)
├── Pages/           # Razor Pages (Account, Appointments, Members, Search, Index)
├── Services/        # İş mantığı (randevu, bekleme listesi, ödeme, üyelik silme, konum arama...)
├── Settings/        # Ayar sınıfları (Scheduling, Nominatim, ödeme tutarları)
├── wwwroot/         # CSS, JavaScript, yazı tipleri, Bootstrap ve jQuery
├── Program.cs       # Uygulamanın başlangıç ve servis ayarları
├── appsettings.json
├── appsettings.Development.json   # Veritabanı bağlantı dizesi
└── YetenekPlatformu.csproj
```

---

## Kurulum ve çalıştırma

### Gereksinimler
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server LocalDB (Visual Studio ile birlikte gelir)

### Adımlar

1. Projeyi indirin (bu sayfada **Code → Download ZIP**) ve klasörü çıkarın.
2. Proje klasöründe bir terminal açın.
3. EF Core aracını yükleyin:
   ```bash
   dotnet tool restore
   ```
4. Veritabanını oluşturun (migration'lar uygulanır):
   ```bash
   dotnet ef database update
   ```
5. Uygulamayı başlatın:
   ```bash
   dotnet run
   ```
6. Tarayıcıda açın: **http://localhost:5260**

İlk açılışta roller ve yetenek listesi veritabanına otomatik eklenir. Siteyi kullanmak için **Kayıt Ol** ile yeni bir üye oluşturun.

### Ayarlar
- **Veritabanı bağlantısı:** `appsettings.Development.json`
  ```
  Server=(localdb)\MSSQLLocalDB;Database=YetenekPlatformu;Trusted_Connection=True;TrustServerCertificate=True
  ```
- **Müsait saatler ve görev aralığı:** `appsettings.json` → `Scheduling` (`DayStartHour`, `DayEndHour`, `MaxDaysAhead`, `StatusUpdateIntervalSeconds`)
- **Konum arama:** `appsettings.json` → `Nominatim`

---

## Bilinen kısıtlar

- Ödeme bir **benzetimdir**; gerçek banka ya da ödeme sağlayıcısı yoktur.
- Uygulamada `tr-TR` kültür ayarı tanımlı değildir; tarih ve sayı biçimleri bazı yerlerde bilgisayarın dil ayarına bağlıdır.
- Bekleme listesindeki bir kayıt geçersiz olduğunda (ör. saat geçtiğinde) kişiye bildirim gitmez; durum "Bekleme Listem" sayfasında görünür.
- Ödeme yapıldığında eğitmene bildirim gitmez; ödeme "Verdiğim Dersler" sayfasında görünür.

---

## Veri kaynakları ve lisanslar

- Yetenek listesi: [Wikidata](https://www.wikidata.org) (CC0)
- Konum arama: © [OpenStreetMap](https://www.openstreetmap.org/copyright) katkıda bulunanlar (Nominatim)
- Yazı tipi: Montserrat (SIL Open Font License, `wwwroot/fonts/OFL.txt`)
- Bootstrap ve jQuery: MIT lisansı (`wwwroot/lib`)
