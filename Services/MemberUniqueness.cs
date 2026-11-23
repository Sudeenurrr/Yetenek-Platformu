using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using YetenekPlatformu.Models;

namespace YetenekPlatformu.Services;

// Telefon ve IBAN her üyede benzersiz olmalı (kayıt ve profil formlarının ortak kontrolü).
// Veritabanındaki benzersiz indeksler (IX_AspNetUsers_PhoneNumber, IX_AspNetUsers_Iban) aynı kuralı ayrıca korur.
public static class MemberUniqueness
{
    public const string PhoneTakenError = "Bu telefon numarası başka bir üyeye kayıtlı.";
    public const string IbanTakenError = "Bu IBAN başka bir üyeye kayıtlı.";

    public const string PhoneIndexName = "IX_AspNetUsers_PhoneNumber";
    public const string IbanIndexName = "IX_AspNetUsers_Iban";

    public record Result(bool PhoneTaken, bool IbanTaken);

    // Kayıtlı biçimdeki ("05XXXXXXXXX", "TR1234567") değerler başka bir üyede var mı?
    // excludeUserId: profil güncellemesinde kişinin kendi kaydı sayılmaz.
    public static async Task<Result> CheckAsync(IQueryable<ApplicationUser> users, string storedPhone, string storedIban, string? excludeUserId = null)
    {
        var others = excludeUserId is null ? users : users.Where(u => u.Id != excludeUserId);
        var phoneTaken = await others.AnyAsync(u => u.PhoneNumber == storedPhone);
        var ibanTaken = await others.AnyAsync(u => u.Iban == storedIban);
        return new Result(phoneTaken, ibanTaken);
    }

    // Kontrolden sonra, kaydetme anında başka biri aynı değeri almışsa veritabanı reddeder.
    // Hata bu indekslerden birinden geldiyse hangisi olduğunu döner; değilse null.
    public static Result? FromDuplicateKey(DbUpdateException ex)
    {
        // 2601: benzersiz indeks ihlali, 2627: benzersiz kısıt ihlali
        if (ex.InnerException is not SqlException { Number: 2601 or 2627 } sql)
            return null;

        var phone = sql.Message.Contains(PhoneIndexName);
        var iban = sql.Message.Contains(IbanIndexName);
        return phone || iban ? new Result(phone, iban) : null;
    }
}
