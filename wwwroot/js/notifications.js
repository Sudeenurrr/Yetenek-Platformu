// Kırmızı nokta: sayfa açıkken 60 saniyede bir sunucuya "görülmemiş değişiklik var mı?" diye sorar.
// Neyin görülmemiş olduğuna sunucu karar verir; bu dosya yalnızca noktaları gösterir ya da gizler.
// Sekme arka plandayken sormaz, sekmeye dönülünce hemen bir kez sorar. Hiçbir şeyi "görüldü" yapmaz.
(function () {
    const script = document.currentScript;
    const url = script && script.dataset.unseenUrl;
    if (!url) {
        return;
    }

    const INTERVAL_MS = 60 * 1000;

    function show(name, visible) {
        document.querySelectorAll('[data-unseen-dot="' + name + '"]').forEach(function (dot) {
            dot.hidden = !visible;
        });
    }

    async function check() {
        if (document.visibilityState !== 'visible') {
            return;
        }
        try {
            const response = await fetch(url, { headers: { 'Accept': 'application/json' }, cache: 'no-store' });
            // Oturum kapandıysa giriş sayfasına yönlenir (JSON gelmez): noktalara dokunulmaz
            const type = response.headers.get('Content-Type') || '';
            if (!response.ok || response.redirected || type.indexOf('application/json') === -1) {
                return;
            }
            const state = await response.json();
            show('incoming', state.incoming === true);
            show('sent', state.sent === true);
            show('any', state.incoming === true || state.sent === true);
        } catch {
            // Ağ hatası: mevcut noktalar olduğu gibi kalır, sonraki denemede yeniden sorulur
        }
    }

    setInterval(check, INTERVAL_MS);
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible') {
            check();
        }
    });
})();
