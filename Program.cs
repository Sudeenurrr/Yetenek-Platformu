using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Services;
using YetenekPlatformu.Settings;

var builder = WebApplication.CreateBuilder(args);

// Veritabanı: bağlantı dizesi appsettings.Development.json içinde
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("'DefaultConnection' bağlantı dizesi bulunamadı.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));

// Kimlik doğrulama (Identity)
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // E-posta yok, onay yok
        options.User.RequireUniqueEmail = false;
        options.SignIn.RequireConfirmedAccount = false;
        options.SignIn.RequireConfirmedEmail = false;
        options.SignIn.RequireConfirmedPhoneNumber = false;

        // Kullanıcı adı "ayşe yılmaz" biçiminde: boşluk ve Türkçe karakterlere izin ver
        options.User.AllowedUserNameCharacters = string.Empty;

        // Basit şifre kuralları: en az 6 karakter
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;

        // Brute-force koruması: 3 yanlış şifrede 2 dakika kilit
        options.Lockout.MaxFailedAccessAttempts = 3;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(2);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddErrorDescriber<TurkishIdentityErrorDescriber>()
    // Şifremi unuttum: ResetPasswordAsync'in kullandığı tek seferlik şifre sıfırlama anahtarı için
    .AddDefaultTokenProviders();

// Oturumlar 1 dakikada bir kontrol edilir: şifre değişince ya da üyelik silinince
// diğer cihazlardaki oturumlar en geç 1 dakika içinde kapanır (varsayılan 30 dakika)
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(1));

// Ad karşılaştırmalarında Türkçe kurallar (ali ≠ alı)
builder.Services.AddScoped<ILookupNormalizer, TurkishLookupNormalizer>();

builder.Services.AddScoped<AccountDeletionService>();
builder.Services.AddScoped<RecoveryCodeService>();
builder.Services.AddScoped<WaitlistService>();
builder.Services.AddScoped<AppointmentBookingService>();
builder.Services.AddScoped<AppointmentResponseService>();
builder.Services.AddScoped<AppointmentChangeService>();
builder.Services.AddScoped<AppointmentNotificationService>();
builder.Services.AddScoped<PaymentService>();

// Müsait saatler: appsettings.json -> "Scheduling"
builder.Services.AddOptions<SchedulingOptions>()
    .Bind(builder.Configuration.GetSection(SchedulingOptions.SectionName))
    .Validate(o => o.DayStartHour >= 0 && o.DayStartHour < o.DayEndHour && o.DayEndHour <= 24,
        "Scheduling ayarı hatalı: 0 <= DayStartHour < DayEndHour <= 24 olmalı.")
    .Validate(o => o.MaxDaysAhead is >= 1 and <= 365,
        "Scheduling ayarı hatalı: MaxDaysAhead 1 ile 365 arasında olmalı.")
    .Validate(o => o.StatusUpdateIntervalSeconds is >= 10 and <= 3600,
        "Scheduling ayarı hatalı: StatusUpdateIntervalSeconds 10 ile 3600 arasında olmalı.")
    .ValidateOnStart();

// Yüz Yüze buluşma yeri araması (OpenStreetMap Nominatim): appsettings.json -> "Nominatim".
// İstekler sunucudan gider; User-Agent uygulamayı tanıtır, kişisel e-posta içermez.
builder.Services.AddOptions<NominatimOptions>()
    .Bind(builder.Configuration.GetSection(NominatimOptions.SectionName))
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _) && o.BaseUrl.EndsWith('/'),
        "Nominatim ayarı hatalı: BaseUrl tam bir adres olmalı ve \"/\" ile bitmeli.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.UserAgent) && !o.UserAgent.Contains('@'),
        "Nominatim ayarı hatalı: UserAgent boş olamaz ve e-posta içermemeli.")
    .Validate(o => o.MinIntervalMilliseconds >= 1000,
        "Nominatim ayarı hatalı: MinIntervalMilliseconds en az 1000 olmalı (saniyede en fazla 1 istek).")
    .Validate(o => o.TimeoutSeconds is >= 1 and <= 30 && o.MaxQueueWaitMilliseconds is >= 0 and <= 30000 && o.CacheHours is >= 0 and <= 168,
        "Nominatim ayarı hatalı: TimeoutSeconds 1-30, MaxQueueWaitMilliseconds 0-30000, CacheHours 0-168 olmalı.")
    .ValidateOnStart();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<PlaceSearchService>((services, client) =>
{
    var nominatim = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<NominatimOptions>>().Value;
    client.BaseAddress = new Uri(nominatim.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(nominatim.TimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.Clear();
    client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", nominatim.UserAgent);
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "tr");
});

// Otomatik durum güncelleme: bitmiş dersler Tamamlandı, süresi dolan talepler Süresi Doldu olur
builder.Services.AddHostedService<AppointmentStatusWorker>();

builder.Services.AddRazorPages()
    .AddMvcOptions(options =>
    {
        // Formdan gelen değer alanın tipine uymazsa (ör. saat alanına "9.5") gösterilen mesajlar Türkçe
        var messages = options.ModelBindingMessageProvider;
        messages.SetAttemptedValueIsInvalidAccessor((value, field) => $"\"{value}\" geçerli bir değer değil.");
        messages.SetValueIsInvalidAccessor(value => $"\"{value}\" geçerli bir değer değil.");
        messages.SetValueMustBeANumberAccessor(field => "Lütfen bir sayı girin.");
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"\"{value}\" geçerli bir değer değil.");
        messages.SetNonPropertyValueMustBeANumberAccessor(() => "Lütfen bir sayı girin.");
        messages.SetUnknownValueIsInvalidAccessor(field => "Geçersiz değer.");
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Geçersiz değer.");
        messages.SetValueMustNotBeNullAccessor(value => "Bu alan zorunludur.");
        messages.SetMissingBindRequiredValueAccessor(field => "Bu alan zorunludur.");
        messages.SetMissingKeyOrValueAccessor(() => "Bu alan zorunludur.");
        messages.SetMissingRequestBodyRequiredValueAccessor(() => "Bu alan zorunludur.");
    });

var app = builder.Build();

// Uye ve Admin rollerini oluştur (yoksa)
await AppRoles.SeedAsync(app.Services);

// Yetenek veri setini aktar (Data/Seed/skills.csv; yalnızca eksik olanlar eklenir)
await SkillSeeder.SeedAsync(app.Services);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

