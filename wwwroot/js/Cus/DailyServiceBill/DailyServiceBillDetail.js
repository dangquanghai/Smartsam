(function () {
    function parseDecimal(value) {
        const text = (value || '').toString().replace(/,/g, '').trim();
        if (text === '') {
            return 0;
        }

        const parsed = Number(text);
        return Number.isFinite(parsed) ? parsed : 0;
    }

    function formatDecimal(value) {
        const number = Number(value);
        if (!Number.isFinite(number)) {
            return '0';
        }

        return number.toLocaleString('en-US', {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        });
    }

    function recalcRow($row) {
        const qty = parseDecimal($row.find('.js-bill-qty').val());
        const price = parseDecimal($row.find('.js-bill-price').val());
        const amount = qty * price;
        $row.find('.js-bill-amount').val(amount.toFixed(4));
        recalcTotal();
    }

    function recalcTotal() {
        let beforeVat = 0;
        $('.js-bill-amount').each(function () {
            beforeVat += parseDecimal($(this).val());
        });

        const pctTax = parseDecimal(window.dailyServiceBillPage?.pctTax || 0);
        const vat = pctTax > 0 ? beforeVat / pctTax : 0;
        const afterVat = Math.round(beforeVat + vat);

        $('#billBeforeVat').val(formatDecimal(beforeVat));
        $('#billVat').val(formatDecimal(vat));
        $('#billAfterVat').val(formatDecimal(afterVat));
    }

    function initializePage() {
        if (window.dailyServiceBillPage?.isView === true) {
            return;
        }

        $('#dailyServiceBillDetailTable')
            .off('input', '.js-bill-qty, .js-bill-price')
            .on('input', '.js-bill-qty, .js-bill-price', function () {
                recalcRow($(this).closest('tr'));
            });
    }

    $(document).ready(initializePage);
})();
