(function () {
    var pageCfg = window.inventoryItemCheckingPage || {};
    var detailBase = pageCfg.detailUrlBase || 'ItemCheckingDetail';
    var exportUrl = pageCfg.exportUrl || 'Index?handler=ExportExcel';
    var table = document.getElementById('itemCheckingTable');
    var editBtn = document.getElementById('btnEdit');
    var viewBtn = document.getElementById('btnView');
    var addBtn = document.getElementById('iicAddBtn');
    var exportBtn = document.getElementById('btnExportExcel');

    function initSelect2(scope) {
        if (!window.jQuery || !window.jQuery.fn || !window.jQuery.fn.select2) return;
        window.jQuery(scope || document).find('select.select2').each(function () {
            var $element = window.jQuery(this);
            if ($element.hasClass('select2-hidden-accessible')) {
                $element.select2('destroy');
            }
            $element.select2({
                width: '100%',
                placeholder: $element.data('placeholder') || '',
                allowClear: true
            });
        });
    }

    function setButtonEnabled(button, enabled) {
        if (!button) return;
        button.disabled = !enabled;
    }

    function updateActionState() {
        var row = selectedRow();
        setButtonEnabled(viewBtn, !!row);
        setButtonEnabled(editBtn, !!row && row.getAttribute('data-can-edit') === 'true');
    }

    var pageSize = document.getElementById('inventoryItemCheckingPageSize');
    if (pageSize) {
        pageSize.addEventListener('change', function () {
            var url = new URL(window.location.href);
            url.searchParams.set('PageSize', pageSize.value);
            url.searchParams.set('Page', '1');
            window.location.href = url.toString();
        });
    }
    initSelect2(document);
    function selectedRow() {
        var selected = document.querySelector('#itemCheckingTable input[name="selectedChecking"]:checked');
        return selected ? selected.closest('tr') : null;
    }

    if (table) {
        table.addEventListener('click', function (e) {
            var row = e.target.closest('tr[data-id]');
            if (!row) return;
            var radio = row.querySelector('input[name="selectedChecking"]');
            if (!radio) return;
            var wasChecked = radio.checked;
            table.querySelectorAll('tr.selected').forEach(function (r) { r.classList.remove('selected'); });
            table.querySelectorAll('input[name="selectedChecking"]').forEach(function (r) { r.checked = false; });
            if (!wasChecked) {
                radio.checked = true;
                row.classList.add('selected');
            }
            updateActionState();
        });
    }

    function wire(id, mode) {
        var btn = document.getElementById(id);
        if (!btn) return;
        btn.addEventListener('click', function (e) {
            e.preventDefault();
            if (btn.disabled) return;
            var row = selectedRow();
            var checkingId = row ? row.getAttribute('data-id') : '';
            if (!checkingId) { alert('Please select a voucher.'); return; }
            if (mode === 'edit' && row.getAttribute('data-can-edit') !== 'true') {
                alert('Cannot edit');
                return;
            }
            window.location.href = detailBase + '?id=' + encodeURIComponent(checkingId) + '&mode=' + mode;
        });
    }
    wire('btnEdit', 'edit');
    wire('btnView', 'view');

    if (addBtn) {
        addBtn.addEventListener('click', function () {
            window.location.href = detailBase + '?mode=add';
        });
    }

    if (exportBtn) {
        exportBtn.addEventListener('click', function () {
            var url = new URL(window.location.href);
            var exportTarget = new URL(exportUrl, window.location.origin);
            exportTarget.search = url.search;
            exportTarget.searchParams.set('handler', 'ExportExcel');
            window.location.href = exportTarget.toString();
        });
    }

    updateActionState();
})();
