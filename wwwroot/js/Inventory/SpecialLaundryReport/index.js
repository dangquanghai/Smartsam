(function () {
    'use strict';

    function initializePage() {
        bindEvents();
        restoreModeState();
        syncCheckAll();
        updateSelectedApartments();
    }

    function bindEvents() {
        $('input[name="reportMode"]').off('change').on('change', function () {
            updateDateVisibility($(this).val());
        });

        $('#chkSelectAll').off('change').on('change', function () {
            $('.special-laundry-apartment-check').prop('checked', $(this).is(':checked'));
            updateSelectedApartments();
        });

        $('.special-laundry-apartment-check').off('change').on('change', function () {
            syncCheckAll();
            updateSelectedApartments();
        });

        $('#specialLaundryReportForm').off('submit').on('submit', function (e) {
            updateSelectedApartments();
            if (!$('#selectedApartments').val()) {
                e.preventDefault();
                alert('Please select apartment.');
            }
        });
    }

    function restoreModeState() {
        const mode = window.specialLaundryReport?.initialMode || $('input[name="reportMode"]:checked').val() || 'TotalInMonth';
        const $mode = $(`input[name="reportMode"][value="${mode}"]`);
        if ($mode.length) {
            $mode.prop('checked', true);
        }

        updateDateVisibility($('input[name="reportMode"]:checked').val());
    }

    function updateDateVisibility(mode) {
        $('#monthDateGroup').toggleClass('d-none', mode !== 'TotalInMonth');
        $('#betweenDateGroup').toggleClass('d-none', mode !== 'Between');
    }

    function syncCheckAll() {
        const $items = $('.special-laundry-apartment-check');
        const checkedCount = $items.filter(':checked').length;
        $('#chkSelectAll').prop('checked', $items.length > 0 && checkedCount === $items.length);
    }

    function updateSelectedApartments() {
        const selected = $('.special-laundry-apartment-check:checked')
            .map(function () {
                return $(this).val();
            })
            .get();

        $('#selectedApartments').val(selected.join(','));
    }

    $(document).ready(initializePage);
})();
