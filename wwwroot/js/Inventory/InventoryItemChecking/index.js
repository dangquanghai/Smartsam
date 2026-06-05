(function () {
    var pageCfg = window.inventoryItemCheckingPage || {};
    var detailBase = pageCfg.detailUrlBase || 'ItemCheckingDetail';
    var exportUrl = pageCfg.exportUrl || 'Index?handler=ExportExcel';
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

    var pageSize = document.getElementById('inventoryItemCheckingPageSize');
    if (pageSize) {
        pageSize.addEventListener('change', function () {
            var url = new URL(window.location.href);
            url.searchParams.set('Filter.PageSize', pageSize.value);
            url.searchParams.set('Filter.Page', '1');
            window.location.href = url.toString();
        });
    }
    initSelect2(document);

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
})();

