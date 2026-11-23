// Ana sayfa arama kutusu: Yetenek / Kişi seçimine göre kutudaki yardımcı yazı değişir
(function () {
    const form = document.querySelector('.home-search');
    if (!form) {
        return;
    }
    const input = form.querySelector('input[name="q"]');
    const modes = form.querySelectorAll('input[name="mode"][data-placeholder]');

    function update() {
        const selected = form.querySelector('input[name="mode"]:checked');
        if (selected && selected.dataset.placeholder) {
            input.placeholder = selected.dataset.placeholder;
        }
    }

    modes.forEach(function (mode) { mode.addEventListener('change', update); });
    // Tarayıcı geri tuşunda önceki seçimi hatırlayabilir: açılışta seçili olana göre ayarla
    update();
})();

// Ana sayfa: "reveal" sınıflı öğeler ekrana girince sırayla belirir
(function () {
    const home = document.querySelector('.home');
    if (!home || !('IntersectionObserver' in window)) {
        return; // Desteklenmiyorsa öğeler zaten görünür kalır
    }

    const items = home.querySelectorAll('.reveal');

    // Kartlar sırayla gelsin diye her birine küçük bir gecikme ver
    home.querySelectorAll('.why-item').forEach(function (item, index) {
        item.style.setProperty('--reveal-delay', (index * 0.15) + 's');
    });

    home.classList.add('reveal-ready');

    const observer = new IntersectionObserver(function (entries) {
        entries.forEach(function (entry) {
            if (entry.isIntersecting) {
                entry.target.classList.add('is-visible');
                observer.unobserve(entry.target);
            }
        });
    }, { threshold: 0.2 });

    items.forEach(function (item) { observer.observe(item); });
})();
