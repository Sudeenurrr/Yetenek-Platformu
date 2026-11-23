// Ortak otomatik tamamlama (Yeteneklerim, Arama: Yetenek Ara ve Kişi Ara).
// Yazdıkça sunucudan eşleşenleri getirir. Eşleşme ve sıralama sunucuda yapılır; bu dosya yalnızca listeyi gösterir.
//
// Kutu (input) ayarları:
//   data-autocomplete        bu kutuda otomatik tamamlama çalışır
//   data-search-url          sunucu adresi (sonuna &term=... eklenir)
//   aria-controls            sonuç listesinin (ul) id'si
//   data-hidden-id           seçilenin id'si yazılacak gizli alan; seçilince form gönderilir (Yetenek)
//   data-select-url          seçilince bu adrese + id ile gidilir (Kişi -> profil)
//   data-enter-submits       "true": listeden seçim yokken Enter formu yazılan kelimeyle gönderir (Arama)
//   data-city-select         seçili şehir bu açılır listeden alınıp &city=... olarak eklenir;
//                            şehir değişince kutuda bir arama varsa form yeniden gönderilir (filtre sunucuda uygulanır)
//   data-city-submit-empty   "true": kutu boşken de şehir değişince form gönderilir (Kişi Ara: o şehirdeki herkes)
//   data-empty-text          eşleşme yokken gösterilecek yazı
(function () {
    document.querySelectorAll('input[data-autocomplete]').forEach(setup);

    function setup(input) {
        if (input.disabled) {
            return;
        }

        const list = document.getElementById(input.getAttribute('aria-controls'));
        const hiddenId = input.dataset.hiddenId ? document.getElementById(input.dataset.hiddenId) : null;
        const citySelect = input.dataset.citySelect ? document.getElementById(input.dataset.citySelect) : null;
        const form = input.form;
        const searchUrl = input.dataset.searchUrl;
        const selectUrl = input.dataset.selectUrl;
        const emptyText = input.dataset.emptyText || 'Eşleşme bulunamadı.';

        let results = [];
        let activeIndex = -1;
        let timer = null;
        let requestNo = 0; // eski cevaplar yeni sonuçların üzerine yazmasın

        function close() {
            list.hidden = true;
            input.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant');
            activeIndex = -1;
        }

        function highlight(index) {
            const options = list.querySelectorAll('[role="option"]');
            options.forEach(function (option, i) {
                option.setAttribute('aria-selected', i === index ? 'true' : 'false');
            });
            activeIndex = index;
            if (index >= 0 && options[index]) {
                input.setAttribute('aria-activedescendant', options[index].id);
                options[index].scrollIntoView({ block: 'nearest' });
            }
        }

        function choose(index) {
            const item = results[index];
            if (!item) {
                return;
            }
            close();
            if (selectUrl) {
                window.location.href = selectUrl + encodeURIComponent(item.id);
                return;
            }
            if (hiddenId) {
                hiddenId.value = item.id;
            }
            input.value = item.name;
            form.submit();
        }

        function render(items) {
            results = items;
            list.innerHTML = '';

            if (items.length === 0) {
                const empty = document.createElement('li');
                empty.className = 'skill-search-empty';
                empty.textContent = emptyText;
                list.appendChild(empty);
            }

            items.forEach(function (item, i) {
                const option = document.createElement('li');
                option.id = input.id + '-option-' + i;
                option.setAttribute('role', 'option');
                option.setAttribute('aria-selected', 'false');
                // textContent: kullanıcı verisi HTML olarak yorumlanmaz
                option.textContent = item.city ? item.name + ' · ' + item.city : item.name;
                // mousedown: kutu odağı kaybetmeden seçim yapılsın
                option.addEventListener('mousedown', function (e) {
                    e.preventDefault();
                    choose(i);
                });
                list.appendChild(option);
            });

            list.hidden = false;
            input.setAttribute('aria-expanded', 'true');
            activeIndex = -1;
        }

        async function search(term) {
            const no = ++requestNo;
            let url = searchUrl + '&term=' + encodeURIComponent(term);
            if (citySelect && citySelect.value) {
                url += '&city=' + encodeURIComponent(citySelect.value);
            }
            try {
                const response = await fetch(url, { headers: { 'Accept': 'application/json' } });
                if (!response.ok || no !== requestNo) {
                    return;
                }
                const items = await response.json();
                if (no === requestNo) {
                    render(items);
                }
            } catch {
                // Ağ hatasında sessizce kapat
                close();
            }
        }

        input.addEventListener('input', function () {
            clearTimeout(timer);
            if (hiddenId) {
                hiddenId.value = '';
            }
            const term = input.value.trim();
            if (term.length < 2) {
                requestNo++;
                close();
                return;
            }
            // Her tuşta değil, yazmaya ara verince ara
            timer = setTimeout(function () { search(term); }, 250);
        });

        input.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowDown' && results.length) {
                e.preventDefault();
                highlight((activeIndex + 1) % results.length);
            } else if (e.key === 'ArrowUp' && results.length) {
                e.preventDefault();
                highlight((activeIndex - 1 + results.length) % results.length);
            } else if (e.key === 'Enter') {
                if (!list.hidden && activeIndex >= 0) {
                    e.preventDefault();
                    choose(activeIndex);
                } else if (input.dataset.enterSubmits !== 'true') {
                    // Yeteneklerim: listeden seçim yapılmadan form gönderilmesin
                    e.preventDefault();
                }
            } else if (e.key === 'Escape') {
                close();
            }
        });

        input.addEventListener('blur', close);

        // Şehir değişince sonuçlar o şehre göre yenilensin (seçili yetenek gizli alanda durur).
        // 1 harf yazılmışsa bir şey yapılmaz; kutu boşsa yalnızca data-city-submit-empty="true" iken gönderilir.
        if (citySelect) {
            citySelect.addEventListener('change', function () {
                const length = input.value.trim().length;
                if (length >= 2 || (length === 0 && input.dataset.citySubmitEmpty === 'true')) {
                    form.submit();
                }
            });
        }
    }
})();
