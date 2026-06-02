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
        $row.find('.js-bill-amount').val(formatDecimal(amount));
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


    function formatDetailNumbers() {
        $('#dailyServiceBillDetailTable .js-bill-qty, #dailyServiceBillDetailTable .js-bill-price, #dailyServiceBillDetailTable .js-bill-amount').each(function () {
            $(this).val(formatDecimal(parseDecimal($(this).val())));
        });
    }

    function initPopupMode() {
        if (window.dailyServiceBillPage?.isPopup) {
            document.body.classList.add("daily-service-bill-popup-body");
            document.body.classList.remove("sidebar-mini", "sidebar-collapse", "layout-fixed");
        }
    }

    function initClosePopupButton() {
        $("#btnClosePopupBillDetail").off("click").on("click", function () {
            if (window.parent && window.parent.$) {
                window.parent.$("#linenDeliveryBillDetailModal").modal("hide");
            }
        });
    }
    function initializePage() {
        formatDetailNumbers();

        if (window.dailyServiceBillPage?.isView === true) {
            return;
        }

        $('#dailyServiceBillDetailTable')
            .off('input', '.js-bill-qty, .js-bill-price')
            .on('input', '.js-bill-qty, .js-bill-price', function () {
                recalcRow($(this).closest('tr'));
            })
            .off('blur', '.js-bill-qty, .js-bill-price')
            .on('blur', '.js-bill-qty, .js-bill-price', function () {
                $(this).val(formatDecimal(parseDecimal($(this).val())));
                recalcRow($(this).closest('tr'));
            });
    }

    $(document).ready(function () {
        initPopupMode();
        initClosePopupButton();
        initializePage();
    });
})();
