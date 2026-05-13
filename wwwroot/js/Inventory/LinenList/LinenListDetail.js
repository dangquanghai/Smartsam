$(document).ready(function () {
    const urlParams = new URLSearchParams(window.location.search);
    const mode = urlParams.get('mode')?.toLowerCase() || 'view';

    initializePage(mode);

    $('form').on('submit', function (e) {
        if (mode === 'view') return true;

        e.preventDefault();

        if (validateMainForm()) {
            $(this).off('submit').submit();
        }
    });
});

function initializePage(mode) {
    if (mode === 'view') {
        $('input, select, textarea').prop('disabled', true);
        $('#btnSave').hide();
    }
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

    if ($('#EcoWashHcmc').val().trim().length > 10) {
        alert("EcoWash HCMC cannot exceed 10 characters.");
        focusErrorField($('#EcoWashHcmc'));
        return false;
    }

    return true;
}

function focusErrorField($el) {
    let $tabPane = $el.closest('.tab-pane');
    if ($tabPane.length > 0 && !$tabPane.hasClass('active')) {
        $('.nav-tabs a[href="#' + $tabPane.attr('id') + '"]').tab('show');
    }
    setTimeout(() => $el.focus(), 300);
}
