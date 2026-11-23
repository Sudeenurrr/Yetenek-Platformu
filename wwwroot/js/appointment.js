// Randevu Al: Türkçe takvimden gün seçilir, o günün saatleri sunucudan gelir (boş / dolu / başka randevunuz var / geçti).
// Süre 2 saatse sonraki saate de bakılır. Eğitmenin dolu saati seçilirse düğme "Bekleme Listesine Gir" olur.
// "Bugün" ve son seçilebilir gün sunucudan (Türkiye saatine göre) gelir; tarayıcının saatine bakılmaz.
// Bu yalnızca görünüm içindir; bütün kurallar talep gönderilince sunucuda yeniden kontrol edilir.
(function () {
    const calendar = document.getElementById('appointment-calendar');
    if (!calendar) {
        return;
    }

    const dateInput = document.getElementById('appointment-date');
    const dateLabel = document.getElementById('appointment-date-label');
    const grid = document.getElementById('appointment-slots');
    const note = document.getElementById('appointment-slots-note');
    const hiddenHour = document.getElementById('appointment-start-hour');
    const durations = document.querySelectorAll('input[name="Input.DurationHours"]');
    const waitlistInput = document.getElementById('appointment-join-waitlist');
    const waitlistNote = document.getElementById('appointment-waitlist-note');
    const submitButton = document.getElementById('appointment-submit');
    // Bekleme listesi yalnızca Randevu Al sayfasında var; Değişiklik Öner sayfasında dolu saat seçilemez
    const allowWaitlist = !!waitlistInput;
    const slotsUrl = calendar.dataset.slotsUrl;
    const rangeUrl = calendar.dataset.rangeUrl;

    const MONTHS = ['Ocak', 'Şubat', 'Mart', 'Nisan', 'Mayıs', 'Haziran', 'Temmuz', 'Ağustos', 'Eylül', 'Ekim', 'Kasım', 'Aralık'];
    const WEEKDAYS_SHORT = ['Pzt', 'Sal', 'Çar', 'Per', 'Cum', 'Cmt', 'Paz'];
    const WEEKDAYS_LONG = ['Pazartesi', 'Salı', 'Çarşamba', 'Perşembe', 'Cuma', 'Cumartesi', 'Pazar'];
    const labels = { free: 'Boş', busy: 'Dolu', mine: 'Başka randevunuz var', past: 'Geçti' };

    // Sunucunun verdiği aralık ("yyyy-MM-dd"); aynı biçimdeki tarihler metin olarak karşılaştırılabilir
    let today = calendar.dataset.today;
    let maxDay = calendar.dataset.max;
    let viewYear, viewMonth; // gösterilen ay (0 = Ocak)
    let slots = [];
    let requestNo = 0; // eski cevaplar yeni günün üzerine yazmasın

    // ---- Tarih yardımcıları (saat dilimi karışmasın diye UTC ile hesaplanır) ----
    function parse(iso) {
        const p = iso.split('-').map(Number);
        return { y: p[0], m: p[1] - 1, d: p[2] };
    }
    function iso(y, m, d) {
        return y + '-' + String(m + 1).padStart(2, '0') + '-' + String(d).padStart(2, '0');
    }
    function daysInMonth(y, m) {
        return new Date(Date.UTC(y, m + 1, 0)).getUTCDate();
    }
    // Pazartesi = 0 ... Pazar = 6
    function weekdayIndex(y, m, d) {
        return (new Date(Date.UTC(y, m, d)).getUTCDay() + 6) % 7;
    }
    function longLabel(isoDate) {
        const t = parse(isoDate);
        return t.d + ' ' + MONTHS[t.m] + ' ' + t.y + ', ' + WEEKDAYS_LONG[weekdayIndex(t.y, t.m, t.d)];
    }
    function inRange(isoDate) {
        return isoDate >= today && isoDate <= maxDay;
    }

    // ---- Takvim ----
    function renderCalendar() {
        calendar.innerHTML = '';
        const first = parse(today);
        const last = parse(maxDay);
        const canPrev = viewYear * 12 + viewMonth > first.y * 12 + first.m;
        const canNext = viewYear * 12 + viewMonth < last.y * 12 + last.m;

        const head = document.createElement('div');
        head.className = 'tr-calendar-head';
        head.appendChild(navButton('‹', 'Önceki ay', canPrev, -1));
        const title = document.createElement('span');
        title.className = 'tr-calendar-title';
        title.textContent = MONTHS[viewMonth] + ' ' + viewYear;
        title.setAttribute('aria-live', 'polite');
        head.appendChild(title);
        head.appendChild(navButton('›', 'Sonraki ay', canNext, 1));
        calendar.appendChild(head);

        const days = document.createElement('div');
        days.className = 'tr-calendar-grid';
        days.setAttribute('role', 'group');
        days.setAttribute('aria-label', MONTHS[viewMonth] + ' ' + viewYear);
        WEEKDAYS_SHORT.forEach(function (name, i) {
            const cell = document.createElement('span');
            cell.className = 'tr-calendar-weekday';
            cell.textContent = name;
            cell.title = WEEKDAYS_LONG[i];
            days.appendChild(cell);
        });

        // Ayın ilk gününden önceki boşluklar (hafta Pazartesi başlar)
        for (let i = 0; i < weekdayIndex(viewYear, viewMonth, 1); i++) {
            days.appendChild(document.createElement('span'));
        }

        for (let d = 1; d <= daysInMonth(viewYear, viewMonth); d++) {
            const value = iso(viewYear, viewMonth, d);
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'tr-calendar-day';
            button.textContent = d;
            button.setAttribute('aria-label', longLabel(value));
            if (value === today) {
                button.classList.add('is-today');
            }
            if (value === dateInput.value) {
                button.classList.add('is-selected');
                button.setAttribute('aria-pressed', 'true');
            }
            if (inRange(value)) {
                button.addEventListener('click', function () { selectDay(value); });
            } else {
                button.disabled = true;
            }
            days.appendChild(button);
        }
        calendar.appendChild(days);
    }

    function navButton(text, label, enabled, step) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'tr-calendar-nav';
        button.textContent = text;
        button.setAttribute('aria-label', label);
        button.disabled = !enabled;
        button.addEventListener('click', function () {
            viewMonth += step;
            if (viewMonth < 0) { viewMonth = 11; viewYear--; }
            if (viewMonth > 11) { viewMonth = 0; viewYear++; }
            renderCalendar();
        });
        return button;
    }

    function showLabel() {
        dateLabel.textContent = dateInput.value ? 'Seçilen gün: ' + longLabel(dateInput.value) : '';
    }

    function selectDay(value) {
        dateInput.value = value;
        hiddenHour.value = '';
        showLabel();
        renderCalendar();
        loadSlots();
    }

    // Sunucudan güncel aralığı al (sayfa açıkken gece yarısı geçmiş olabilir)
    async function refreshRange() {
        try {
            const response = await fetch(rangeUrl, { headers: { 'Accept': 'application/json' } });
            if (!response.ok) {
                return;
            }
            const range = await response.json();
            if (range.today === today && range.max === maxDay) {
                return;
            }
            today = range.today;
            maxDay = range.max;
            // Seçili gün artık aralıkta değilse (ör. dün oldu) seçim kalkar
            if (dateInput.value && !inRange(dateInput.value)) {
                dateInput.value = '';
                hiddenHour.value = '';
                slots = [];
                grid.innerHTML = '';
                note.textContent = 'Saatleri görmek için bir gün seçin.';
                showLabel();
            }
            const first = parse(today);
            if (viewYear * 12 + viewMonth < first.y * 12 + first.m) {
                viewYear = first.y;
                viewMonth = first.m;
            }
            renderCalendar();
            if (dateInput.value) {
                loadSlots(); // "Geçti" durumları güncellensin
            }
        } catch {
            // Ağ hatası: mevcut aralıkla devam edilir, sunucu talepte yine kontrol eder
        }
    }

    // ---- Saatler ----
    function duration() {
        const checked = document.querySelector('input[name="Input.DurationHours"]:checked');
        return checked && checked.value === '2' ? 2 : 1;
    }

    function pad(hour) {
        return (hour < 10 ? '0' : '') + hour + ':00';
    }

    // Seçilen süre bu saatten başlayınca ne yapılabilir (2 saatte sonraki saate de bakılır):
    // 'book' = hepsi boş, talep gönderilir; 'wait' = eğitmenin saati dolu, bekleme listesine girilir; null = seçilemez
    function modeAt(index) {
        let anyBusy = false;
        for (let i = 0; i < duration(); i++) {
            const s = slots[index + i];
            if (!s || s.state === 'past' || s.state === 'mine') {
                return null;
            }
            if (s.state === 'busy') {
                if (!allowWaitlist) {
                    return null;
                }
                anyBusy = true;
            }
        }
        return anyBusy ? 'wait' : 'book';
    }

    // Gönder düğmesi ve gizli alan seçilen saate göre: talep ya da bekleme listesi
    function updateMode(mode) {
        if (!allowWaitlist) {
            return;
        }
        waitlistInput.value = mode === 'wait' ? 'true' : 'false';
        submitButton.textContent = mode === 'wait' ? 'Bekleme Listesine Gir' : 'Talep Gönder';
        waitlistNote.hidden = mode !== 'wait';
    }

    function renderSlots() {
        grid.innerHTML = '';
        const selected = hiddenHour.value === '' ? null : Number(hiddenHour.value);
        let selectedStillFits = false;
        let selectedMode = null;

        slots.forEach(function (slot, index) {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'slot slot-' + slot.state;
            // textContent: sunucudan gelen veri HTML olarak yorumlanmaz
            button.textContent = pad(slot.hour) + (slot.state === 'free' ? '' : ' · ' + labels[slot.state]);
            button.setAttribute('aria-pressed', 'false');

            const mode = modeAt(index);
            if (mode) {
                button.title = pad(slot.hour) + ' – ' + pad(slot.hour + duration())
                    + (mode === 'wait' ? ' (dolu: bekleme listesine girebilirsiniz)' : '');
                button.addEventListener('click', function () {
                    hiddenHour.value = slot.hour;
                    renderSlots();
                });
                if (slot.hour === selected) {
                    button.classList.add('is-selected');
                    button.setAttribute('aria-pressed', 'true');
                    selectedStillFits = true;
                    selectedMode = mode;
                }
            } else {
                button.disabled = true;
                if (slot.state === 'free' || (slot.state === 'busy' && allowWaitlist)) {
                    button.title = 'Sonraki saat uygun değil ya da gün bitiyor: 2 saat sığmıyor';
                }
            }
            grid.appendChild(button);
        });

        // Süre değişince seçili saat artık sığmıyorsa seçim kalkar
        if (selected !== null && !selectedStillFits) {
            hiddenHour.value = '';
        }
        updateMode(selectedMode);
    }

    async function loadSlots() {
        const no = ++requestNo;
        slots = [];
        grid.innerHTML = '';
        updateMode(null);
        if (!dateInput.value) {
            note.textContent = 'Saatleri görmek için bir gün seçin.';
            return;
        }
        note.textContent = 'Saatler yükleniyor...';
        try {
            const response = await fetch(slotsUrl + '&date=' + encodeURIComponent(dateInput.value), {
                headers: { 'Accept': 'application/json' }
            });
            if (no !== requestNo) {
                return;
            }
            if (!response.ok) {
                note.textContent = 'Bu gün için randevu alınamaz. Lütfen takvimden başka bir gün seçin.';
                refreshRange();
                return;
            }
            slots = await response.json();
            if (no !== requestNo) {
                return;
            }
            const anyFree = slots.some(function (s) { return s.state === 'free'; });
            const anyBusy = allowWaitlist && slots.some(function (s) { return s.state === 'busy'; });
            note.textContent = anyFree ? 'Başlangıç saatini seçin.' + (allowWaitlist ? ' Dolu bir saati seçerek bekleme listesine de girebilirsiniz.' : '')
                : anyBusy ? 'Bu günde boş saat yok. Dolu bir saati seçerek bekleme listesine girebilirsiniz.'
                : 'Bu günde boş saat yok.';
            renderSlots();
        } catch {
            note.textContent = 'Saatler yüklenemedi. Lütfen tekrar deneyin.';
        }
    }

    durations.forEach(function (d) { d.addEventListener('change', renderSlots); });

    // Sekmeye geri dönülünce ve sayfa açık kaldıkça aralık sunucudan yenilenir (gece yarısı)
    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible') {
            refreshRange();
        }
    });
    setInterval(refreshRange, 5 * 60 * 1000);

    // Başlangıç: seçili gün varsa (hatayla geri dönüldüyse) onun ayı, yoksa bugünün ayı gösterilir
    if (dateInput.value && !inRange(dateInput.value)) {
        dateInput.value = '';
    }
    const start = parse(dateInput.value || today);
    viewYear = start.y;
    viewMonth = start.m;
    renderCalendar();
    showLabel();
    loadSlots();
})();
