// Yüz Yüze buluşma yeri araması. Randevu Al'da görüşme türü Yüz Yüze seçilince, Değişiklik Öner'de "Buluşma yeri"
// kutusu işaretlenince bölüm görünür. Arama adresi bölümün data-places-url özelliğinden okunur.
// Arama yalnızca "Ara" düğmesi ya da Enter ile yapılır (Nominatim kuralı: yazarken otomatik arama yok).
// İstek sitenin kendi adresine gider (/Appointments/Create?handler=Places); sunucu Nominatim'e sorar, hız sınırını uygular.
// Sonuçtan seçilen yerin adı ve koordinatı forma girer; ad elle değiştirilirse koordinat silinir (elle yazılmış yer).
// Bütün kurallar talep gönderilince sunucuda yeniden kontrol edilir.
(function () {
    const picker = document.getElementById('place-picker');
    if (!picker) {
        return;
    }

    const url = picker.dataset.placesUrl;
    const query = document.getElementById('place-query');
    const button = document.getElementById('place-search');
    const status = document.getElementById('place-status');
    const list = document.getElementById('place-results');
    const attribution = document.getElementById('place-attribution');
    const nameInput = document.getElementById('place-name');
    const latInput = document.getElementById('place-lat');
    const lonInput = document.getElementById('place-lon');
    const kind = document.getElementById('place-kind');
    const types = document.querySelectorAll('input[name="Input.MeetingType"]');
    // Değişiklik Öner sayfasında bölüm "Buluşma yeri" kutusuna bağlıdır (data-toggle-checkbox), Randevu Al'da görüşme türüne
    const toggle = picker.dataset.toggleCheckbox ? document.getElementById(picker.dataset.toggleCheckbox) : null;
    const unavailable = 'Konum araması şu an yapılamıyor. Buluşma yerini elle yazabilirsiniz.';
    let searching = false;

    // Bölüm yalnızca Yüz Yüze seçiliyken (ya da "Buluşma yeri" kutusu işaretliyken) görünür
    function togglePicker() {
        picker.hidden = toggle
            ? !toggle.checked
            : !Array.from(types).some(function (t) { return t.checked && t.value === '1'; });
    }

    // Seçilen yerin türü: listeden (haritada açılabilir) ya da elle yazılmış
    function updateKind() {
        if (!nameInput.value.trim()) {
            kind.textContent = '';
        } else if (latInput.value && lonInput.value) {
            kind.textContent = 'Listeden seçildi. Randevu kartında haritada açılabilir.';
        } else {
            kind.textContent = 'Elle yazıldı. Bu yer haritada gösterilmez.';
        }
    }

    function setStatus(text, isError) {
        status.textContent = text;
        status.classList.toggle('text-danger', !!isError);
    }

    function choose(place, item) {
        nameInput.value = place.name;
        latInput.value = place.lat;
        lonInput.value = place.lon;
        Array.from(list.querySelectorAll('.place-result')).forEach(function (b) {
            b.classList.remove('is-selected');
            b.setAttribute('aria-pressed', 'false');
        });
        item.classList.add('is-selected');
        item.setAttribute('aria-pressed', 'true');
        setStatus('Buluşma yeri seçildi.', false);
        updateKind();
    }

    async function search() {
        const text = query.value.trim();
        if (text.length < 3) {
            setStatus('Aramak için en az 3 karakter yazın.', true);
            return;
        }
        if (searching) {
            return;
        }
        searching = true;
        button.disabled = true;
        list.innerHTML = '';
        attribution.hidden = true;
        setStatus('Aranıyor...', false);
        try {
            const response = await fetch(url + '&q=' + encodeURIComponent(text), { headers: { 'Accept': 'application/json' } });
            if (!response.ok) {
                throw new Error('HTTP ' + response.status);
            }
            const data = await response.json();
            if (!data.ok) {
                setStatus(data.error || unavailable, true);
                return;
            }
            if (!data.places || data.places.length === 0) {
                setStatus('Sonuç bulunamadı. Başka bir ad deneyin ya da yeri aşağıya elle yazın.', false);
                return;
            }
            setStatus('Bir sonuç seçin.', false);
            data.places.forEach(function (place) {
                const li = document.createElement('li');
                const item = document.createElement('button');
                item.type = 'button';
                item.className = 'place-result';
                item.setAttribute('aria-pressed', 'false');
                // textContent: dışarıdan gelen ad HTML olarak yorumlanmaz
                item.textContent = place.name;
                item.addEventListener('click', function () { choose(place, item); });
                li.appendChild(item);
                list.appendChild(li);
            });
            // Nominatim sonuçları için atıf (lisans gereği)
            attribution.hidden = false;
        } catch {
            setStatus(unavailable, true);
        } finally {
            searching = false;
            button.disabled = false;
        }
    }

    types.forEach(function (t) { t.addEventListener('change', togglePicker); });
    if (toggle) {
        toggle.addEventListener('change', togglePicker);
    }
    button.addEventListener('click', search);
    query.addEventListener('keydown', function (e) {
        // Enter formu göndermez, arama yapar
        if (e.key === 'Enter') {
            e.preventDefault();
            search();
        }
    });
    // Ad elle değiştirilirse listeden seçilen konum geçerliliğini yitirir
    nameInput.addEventListener('input', function () {
        latInput.value = '';
        lonInput.value = '';
        Array.from(list.querySelectorAll('.place-result.is-selected')).forEach(function (b) {
            b.classList.remove('is-selected');
            b.setAttribute('aria-pressed', 'false');
        });
        updateKind();
    });

    togglePicker();
    updateKind();
})();
