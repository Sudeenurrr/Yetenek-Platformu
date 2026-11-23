// Telefon ve IBAN kutuları: yazarken biçimlendirir. Asıl kontrol sunucuda yapılır (MemberValidation).
//   data-format="phone": "+90 5" sabit kutuda; kalan 9 rakam "55 555 55 55" biçimine girer.
//   data-format="iban":  "TR" sabit kutuda; yalnızca 7 rakam alınır.
(function () {
    // Numaranın tamamı yapıştırılırsa ("05551234567", "+90 555 123 45 67") baştaki fazlalığı at
    function phoneLocalDigits(value) {
        let digits = value.replace(/[^0-9]/g, '');
        if (digits.length >= 12 && digits.startsWith('905')) {
            digits = digits.slice(3);
        } else if (digits.length >= 11 && digits.startsWith('05')) {
            digits = digits.slice(2);
        } else if (digits.length >= 10 && digits.startsWith('5')) {
            digits = digits.slice(1);
        }
        return digits.slice(0, 9);
    }

    // "551234567" -> "55 123 45 67"
    function formatPhone(digits) {
        const groups = [digits.slice(0, 2), digits.slice(2, 5), digits.slice(5, 7), digits.slice(7, 9)];
        return groups.filter(Boolean).join(' ');
    }

    function ibanDigits(value) {
        let v = value.toUpperCase().replace(/\s/g, '');
        if (v.startsWith('TR')) {
            v = v.slice(2);
        }
        return v.replace(/[^0-9]/g, '').slice(0, 7);
    }

    function attach(input, transform) {
        const apply = function () {
            const before = input.value;
            const after = transform(before);
            if (before !== after) {
                input.value = after;
            }
        };
        input.addEventListener('input', apply);
        apply(); // sayfa açılırken (ör. hata sonrası) mevcut değeri de düzenle
    }

    document.querySelectorAll('input[data-format="phone"]').forEach(function (input) {
        attach(input, function (value) { return formatPhone(phoneLocalDigits(value)); });
    });

    document.querySelectorAll('input[data-format="iban"]').forEach(function (input) {
        attach(input, ibanDigits);
    });
})();
