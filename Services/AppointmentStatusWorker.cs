using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YetenekPlatformu.Data;
using YetenekPlatformu.Models;
using YetenekPlatformu.Settings;

namespace YetenekPlatformu.Services;

// Otomatik durum güncelleme (BackgroundService). Ayardaki aralıkla (Scheduling:StatusUpdateIntervalSeconds) çalışır:
//   1) Bitiş saati geçmiş Onaylandı randevular -> Tamamlandı (saat dilimleri geçmiş kayıt olarak kalır)
//   2) 24 saati dolmuş ya da ders saati gelmiş Beklemede talepler -> Süresi Doldu, saat dilimleri silinir,
//      boşalan eğitmen saatleri için bekleme listesindeki sıradakiler aktifleşir
//   0) Süresi dolan değişiklik önerileri (24 saat, eski ders saati ya da önerilen saat geldi) düşer: randevu Onaylandı'ya
//      döner, önerilen saatler silinir, sıradakiler aktifleşir. Eski dersin bitişi de geçtiyse 1. adımda Tamamlandı olur.
//   3) Bekleme listesi: saati geçmiş bekleyen kayıtlar Geçersiz olur, saati boşalmış ama hâlâ bekleyen kayıtlar aktifleşir
// Güncellemeler "durum hâlâ aynıysa" koşuluyla yapılır: kullanıcı aynı anda işlem yaptıysa o kayda dokunulmaz,
// sonraki turda aynı kayıt tekrar işlenmez. Süre dolması ve tamamlanma StudentSeen / InstructorSeen'e dokunmaz
// (bekleme listesinden oluşan yeni talep ise iki tarafta kırmızı nokta çıkarır).
// Bir hata turu ya da servisi durdurmaz: günlüğe yazılır, sonraki turda yeniden denenir.
public class AppointmentStatusWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SchedulingOptions> scheduling,
    ILogger<AppointmentStatusWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(scheduling.Value.StatusUpdateIntervalSeconds));
        try
        {
            // İlk tur hemen: site kapalıyken saati geçmiş randevular da güncellensin
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Site kapanıyor: hata değil
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        // Her iş ayrı scope'ta: biri hata verirse diğerlerinin EF hafızası bozulmaz, yine çalışırlar
        var now = AppClock.Now;

        // Önce süresi dolan öneriler düşer: eski ders saati geldiyse randevu Onaylandı'ya döner,
        // bitiş saati de geçtiyse hemen ardından aynı turda Tamamlandı olur (Değişiklik Önerildi -> Tamamlandı)
        var fromProposals = WaitlistService.ActivationCounts.None;
        var proposals = await TryAsync("Değişiklik önerisi", async (_, _, services) =>
        {
            var change = services.GetRequiredService<AppointmentChangeService>();
            (var count, fromProposals) = await change.ExpireProposalsAsync(now, cancellationToken);
            return count;
        }, cancellationToken);

        var completed = await TryAsync("Tamamlandı", (db, _, _) => CompleteFinishedAsync(db, now, cancellationToken), cancellationToken);

        var fromExpiry = WaitlistService.ActivationCounts.None;
        var expired = await TryAsync("Süresi Doldu", async (db, waitlist, _) =>
        {
            (var count, fromExpiry) = await ExpirePendingAsync(db, waitlist, now, cancellationToken);
            return count;
        }, cancellationToken);

        var fromSweep = WaitlistService.ActivationCounts.None;
        await TryAsync("Bekleme listesi", async (_, waitlist, _) =>
        {
            fromSweep = await waitlist.SweepAsync(cancellationToken);
            return 0;
        }, cancellationToken);

        var activated = fromProposals.Activated + fromExpiry.Activated + fromSweep.Activated;
        var invalidated = fromProposals.Invalidated + fromExpiry.Invalidated + fromSweep.Invalidated;

        // Yalnızca bir şey değiştiyse yazılır
        if (proposals + completed + expired + activated + invalidated > 0)
            logger.LogInformation(
                "Durum güncelleme: {Proposals} önerinin süresi doldu, {Completed} randevu Tamamlandı, {Expired} talep Süresi Doldu, bekleme listesinden {Activated} talep oluştu, {Invalidated} kayıt Geçersiz oldu.",
                proposals, completed, expired, activated, invalidated);
    }

    private async Task<int> TryAsync(string job, Func<ApplicationDbContext, WaitlistService, IServiceProvider, Task<int>> work, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var waitlist = scope.ServiceProvider.GetRequiredService<WaitlistService>();
            return await work(db, waitlist, scope.ServiceProvider);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Durum güncelleme ({Job}) başarısız oldu; bir sonraki turda yeniden denenecek.", job);
            return 0;
        }
    }

    // 1) Onaylandı -> Tamamlandı: tek koşullu güncelleme (satırlar uygulamaya getirilmez).
    //    Saat dilimleri kalır; ders saati geçtiği için bekleme listesini etkilemez.
    private static Task<int> CompleteFinishedAsync(ApplicationDbContext db, DateTime now, CancellationToken cancellationToken) =>
        db.Appointments
            .Where(AppointmentRules.FinishedApproved(now))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatus.Completed), cancellationToken);

    // 2) Beklemede -> Süresi Doldu, saatleri boşa çıkar ve sıradakiler aktifleşir: hepsi aynı transaction'da
    private static async Task<(int Expired, WaitlistService.ActivationCounts Waitlist)> ExpirePendingAsync(
        ApplicationDbContext db, WaitlistService waitlist, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var ids = await db.Appointments
            .Where(AppointmentRules.ExpiredPending(now))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
            return (0, WaitlistService.ActivationCounts.None);

        // Koşul yeniden uygulanır (bu arada kullanıcı onayladıysa o kayda dokunulmaz);
        // yalnızca gerçekten Süresi Doldu olan taleplerin saatleri silinir
        var (expired, counts) = await waitlist.ExpireAndActivateAsync(ids, now);

        await transaction.CommitAsync(cancellationToken);
        return (expired, counts);
    }
}
