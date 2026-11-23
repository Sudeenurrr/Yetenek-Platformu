// Ödeme Yap sayfası: form gönderilince düğme pasif olur (çift tıklamayla ikinci istek gitmesin).
// Asıl koruma sunucuda: her ders için tek ödeme (PaymentService + IX_Payments_AppointmentId benzersiz indeksi).
(function () {
    var form = document.getElementById('payment-form');
    var button = document.getElementById('payment-submit');
    if (!form || !button) return;

    form.addEventListener('submit', function () {
        button.disabled = true;
        button.textContent = 'Gönderiliyor...';
    });

    // Tarayıcının "geri" düğmesiyle sayfa önbellekten gelirse düğme yeniden açılsın
    window.addEventListener('pageshow', function () {
        button.disabled = false;
        button.textContent = 'Ödemeyi Gönder';
    });
})();
