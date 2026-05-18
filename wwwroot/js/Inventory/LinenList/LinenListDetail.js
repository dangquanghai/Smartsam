$(document).ready(function () {
    const urlParams = new URLSearchParams(window.location.search);
    const mode = urlParams.get('mode')?.toLowerCase() || 'view';

    initializePage(mode);

    $('form').on('submit', function (e) {
        if (mode === 'view') return true;

        e.preventDefault();

        if (validateMainForm()) {
            normalizeEcoWashForPost();
            $(this).off('submit').submit();
        }
    });
});

function initializePage(mode) {
    formatEcoWashDisplay();

    if (mode === 'view') {
        $('input, select, textarea').prop('disabled', true);
        $('#btnSave').hide();
        return;
    }

    $('#EcoWashHcmc').off('blur.linenPrice').on('blur.linenPrice', formatEcoWashDisplay);
    $('#EcoWashHcmc').off('focus.linenPrice').on('focus.linenPrice', function () {
        $(this).val(unformatNumberText($(this).val()));
    });
    $('#EcoWashHcmc').off('input.linenPrice').on('input.linenPrice', function () {
        $(this).val(unformatNumberText($(this).val()).replace(/\D/g, ''));
    });
}

function validateMainForm() {
    const fields = [
        { id: 'LinnenCode', name: 'LinnenCode' }
    ];

    for (let field of fields) {
        let $el = $('#' + field.id);
        if (!$el.val() || $el.val().toString().trim() === "") {
            alert("Please enter/select: " + field.name);
            focusErrorField($el);
            return false;
        }
    }

    if ($('#LinnenCode').val().trim().length > 50) {
        alert("LinnenCode cannot exceed 50 characters.");
        focusErrorField($('#LinnenCode'));
        return false;
    }

    const ecoWashValue = unformatNumberText($('#EcoWashHcmc').val()).trim();
    if (ecoWashValue.length > 10) {
        alert("EcoWash HCMC cannot exceed 10 characters.");
        focusErrorField($('#EcoWashHcmc'));
        return false;
    }

    if (ecoWashValue && !/^\d+$/.test(ecoWashValue)) {
        alert("EcoWash HCMC must be numeric.");
        focusErrorField($('#EcoWashHcmc'));
        return false;
    }

    return true;
}

function formatEcoWashDisplay() {
    const $field = $('#EcoWashHcmc');
    const rawValue = unformatNumberText($field.val());
    if (!rawValue || isNaN(Number(rawValue))) {
        $field.val(rawValue);
        return;
    }

    $field.val(Number(rawValue).toLocaleString('en-US'));
}

function normalizeEcoWashForPost() {
    const $field = $('#EcoWashHcmc');
    $field.val(unformatNumberText($field.val()));
}

function unformatNumberText(value) {
    return (value || '').toString().replace(/,/g, '').trim();
}

function focusErrorField($el) {
    let $tabPane = $el.closest('.tab-pane');
    if ($tabPane.length > 0 && !$tabPane.hasClass('active')) {
        $('.nav-tabs a[href="#' + $tabPane.attr('id') + '"]').tab('show');
    }
    setTimeout(() => $el.focus(), 300);
}
