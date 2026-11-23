// Değişiklik Öner: "Gün/saat" kutusu işaretli değilse takvim ve saat bölümü gizlenir (sunucu mevcut saati kullanır).
// "Buluşma yeri" kutusu konum arama bölümünü açar (wwwroot/js/place-search.js).
// Bu yalnızca görünüm içindir; en az birinin seçilmesi ve bütün kurallar sunucuda kontrol edilir.
(function () {
    const changeTime = document.getElementById('change-time');
    const timeSection = document.getElementById('time-section');
    if (!changeTime || !timeSection) {
        return;
    }

    function toggleTime() {
        timeSection.hidden = !changeTime.checked;
    }

    changeTime.addEventListener('change', toggleTime);
    toggleTime();
})();
