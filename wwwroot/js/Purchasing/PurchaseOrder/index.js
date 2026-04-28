(function () {
    'use strict';

    const CONFIG = {
        selectors: {
            tbody: '#purchaseOrderTable tbody',
            pagination: '#pagination',
            actionBtns: '#btnBackToProcessing'
        }
    };

    let pageSize = typeof defaultPageSize !== 'undefined' ? defaultPageSize : 10;

    let state = {
        selectedRow: null,
        currentPage: 1,
        currentDataRows: []
    };

    let viewDetailState = {
        lastRequest: null,
        lastRows: [],
        lastTotal: 0,
        currentPage: 1,
        pageSize: typeof defaultPageSize !== 'undefined' ? defaultPageSize : 10
    };

    function getQueryInt(name) {
        const value = new URLSearchParams(window.location.search).get(name);
        if (!value) {
            return null;
        }

        const parsed = Number.parseInt(value, 10);
        return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
    }

    function getSelectedStatusIds() {
        return $('.po-status-checkbox:checked')
            .map(function () {
                const value = Number.parseInt(this.value, 10);
                return Number.isFinite(value) ? value : null;
            })
            .get()
            .filter((value) => Number.isFinite(value));
    }

    function getSelectedStatusLabels() {
        return $('.po-status-checkbox:checked')
            .map(function () {
                return ($(this).data('label') || '').toString().trim();
            })
            .get()
            .filter((value) => value.length > 0);
    }

    function updateStatusDropdownButton() {
        const labels = getSelectedStatusLabels();
        const text = labels.length > 0 ? labels.join(', ') : 'All statuses';
        const $button = $('#poStatusDropdownBtn');
        $button.text(text);
        $button.attr('title', text);
    }

    function initializeStatusDropdown() {
        updateStatusDropdownButton();

        $(document).off('click.poStatusMenu').on('click.poStatusMenu', '.po-status-menu', function (e) {
            e.stopPropagation();
        });

        $(document).off('change.poStatusCheckbox').on('change.poStatusCheckbox', '.po-status-checkbox', function () {
            updateStatusDropdownButton();
        });

        $(document).off('click.poStatusSelectAll').on('click.poStatusSelectAll', '#poStatusSelectAllBtn', function () {
            $('.po-status-checkbox').prop('checked', true);
            updateStatusDropdownButton();
        });

        $(document).off('click.poStatusClear').on('click.poStatusClear', '#poStatusClearBtn', function () {
            $('.po-status-checkbox').prop('checked', false);
            updateStatusDropdownButton();
        });
    }

    function initializeAjaxSelect2(selector, url, options) {
        const $element = $(selector);
        if (!$element.length || !url || typeof $element.select2 !== 'function') {
            return;
        }

        if ($element.hasClass('select2-hidden-accessible')) {
            $element.select2('destroy');
        }

        const customOptions = Object.assign({}, options || {});
        const useSelectedTermWhenEmpty = customOptions.useSelectedTermWhenEmpty === true;
        delete customOptions.useSelectedTermWhenEmpty;

        const config = Object.assign({
            width: '100%',
            allowClear: true,
            placeholder: $element.data('placeholder') || $element.find('option:first').text() || '-- Select --',
            minimumInputLength: 3,
            ajax: {
                url: url,
                dataType: 'json',
                delay: 250,
                data: function (params) {
                    const term = (params.term || '').trim();
                    return {
                        term: term || (useSelectedTermWhenEmpty ? getSelect2SearchSeed($element) : '')
                    };
                },
                processResults: function (data) {
                    return {
                        results: Array.isArray(data) ? data : []
                    };
                },
                cache: true
            }
        }, customOptions);

        $element.select2(config);

        $element.off('select2:open.poSelect2').on('select2:open.poSelect2', function () {
            const searchField = document.querySelector('.select2-container--open .select2-search__field');
            if (searchField) {
                const selectedText = getSelect2SearchSeed($element);
                if (!searchField.value.trim() && selectedText) {
                    searchField.value = selectedText;
                    searchField.dispatchEvent(new Event('input', { bubbles: true }));
                    searchField.dispatchEvent(new Event('keyup', { bubbles: true }));
                }

                searchField.focus();
                if (typeof searchField.select === 'function') {
                    searchField.select();
                }
            }
        });
    }

    function getSelect2SearchSeed($element) {
        const selectedText = ($element.find('option:selected').text() || '').trim();
        if (!selectedText || selectedText.startsWith('--')) {
            return '';
        }

        let seed = selectedText;
        const parenIndex = seed.indexOf(' (');
        if (parenIndex > -1) {
            seed = seed.substring(0, parenIndex).trim();
        }

        const slashIndex = seed.indexOf(' / ');
        if (slashIndex > -1) {
            seed = seed.substring(0, slashIndex).trim();
        }

        return seed;
    }

    function formatSupplierResult(item) {
        if (!item || item.loading) {
            return item ? item.text : '';
        }

        const supplierCode = (item.supplierCode || item.text || '').toString();
        const supplierName = (item.supplierName || '').toString();
        const $result = $('<div class="po-supplier-select2-result"></div>');
        $('<div class="po-supplier-select2-code"></div>').text(supplierCode).appendTo($result);
        if (supplierName) {
            $('<div class="po-supplier-select2-name vni-font"></div>').text(supplierName).appendTo($result);
        }

        return $result;
    }

    function formatSupplierSelection(item) {
        return (item && (item.supplierCode || item.text)) || '';
    }

    function syncBrowserUrlToSearchState(page, filter) {
        const query = new URLSearchParams();
        const currentFilter = filter || {};

        if (currentFilter.poNo) query.set('Filter.PONo', currentFilter.poNo);
        if (currentFilter.requestNo) query.set('Filter.RequestNo', currentFilter.requestNo);
        (Array.isArray(currentFilter.statusIds) ? currentFilter.statusIds : []).forEach((statusId) => {
            if (Number.isFinite(statusId)) {
                query.append('Filter.StatusIds', String(statusId));
            }
        });
        if (currentFilter.supplierKeyword) query.set('Filter.SupplierKeyword', currentFilter.supplierKeyword);
        if (currentFilter.assessLevelId) query.set('Filter.AssessLevelId', currentFilter.assessLevelId);
        if (currentFilter.remark) query.set('Filter.Remark', currentFilter.remark);
        query.set('Filter.UseDateRange', currentFilter.useDateRange ? 'true' : 'false');
        if (currentFilter.useDateRange && currentFilter.fromDate) query.set('Filter.FromDate', currentFilter.fromDate);
        if (currentFilter.useDateRange && currentFilter.toDate) query.set('Filter.ToDate', currentFilter.toDate);
        query.set('Filter.Page', String(page || 1));
        query.set('Filter.PageSize', String(pageSize || defaultPageSize || 10));

        const nextUrl = `${window.location.pathname}${query.toString() ? `?${query.toString()}` : ''}`;
        window.history.replaceState({}, document.title, nextUrl);
    }

    // Dong bo widget date-range voi 2 field an From/To.
    function initializeSearchDateRange() {
        if (typeof window.initSimpleDateRange !== 'function') {
            return;
        }

        window.initSimpleDateRange('PoDateRange', '#Filter_FromDate', '#Filter_ToDate', {
            linkedCalendars: false
        });

        const fromDate = $('#Filter_FromDate').val();
        const toDate = $('#Filter_ToDate').val();
        if (fromDate && toDate && typeof window.setDateRangeValue === 'function') {
            window.setDateRangeValue('PoDateRange', fromDate, toDate);
        }
    }

    // Gui filter hien tai len server va ve lai danh sach.
    function performSearch(page = 1) {
        state.currentPage = page;
        const pageIndexInput = document.getElementById('PageIndex');
        const pageSizeInput = document.getElementById('PageSize');
        if (pageIndexInput) {
            pageIndexInput.value = String(state.currentPage);
        }
        if (pageSizeInput) {
            pageSizeInput.value = String(pageSize);
        }
        const token = $('input[name="__RequestVerificationToken"]').val();

        const filter = {
            poNo: $('#Filter_PONo').val() || null,
            requestNo: $('#Filter_RequestNo').val() || null,
            statusIds: getSelectedStatusIds(),
            supplierKeyword: $('#Filter_SupplierKeyword').val() || null,
            assessLevelId: $('#Filter_AssessLevelId').val() ? parseInt($('#Filter_AssessLevelId').val(), 10) : null,
            remark: $('#Filter_Remark').val() || null,
            useDateRange: $('#Filter_UseDateRange').is(':checked'),
            fromDate: $('#Filter_FromDate').val() || null,
            toDate: $('#Filter_ToDate').val() || null,
            page: state.currentPage,
            pageSize: pageSize
        };

        showLoading(true);

        $.ajax({
            url: '?handler=Search',
            type: 'POST',
            contentType: 'application/json',
            headers: { 'RequestVerificationToken': token },
            data: JSON.stringify(filter),
            success: function (response) {
                if (response.success) {
                    state.currentDataRows = response.data || [];
                    renderPurchaseOrders(response.data || []);
                    updatePagination(response.total || 0, response.page || 1, response.pageSize || pageSize, response.totalPages || 1);
                    syncBrowserUrlToSearchState(response.page || page, filter);
                    resetActions();
                } else {
                    showError('Search failed: ' + (response.message || 'Unknown error'));
                }
            },
            error: function (xhr, status, error) {
                showError('System connection error: ' + error);
            },
            complete: function () {
                showLoading(false);
            }
        });
    }

    // Ve ket qua search va giu data goc cho cac nut dong.
    function renderPurchaseOrders(items) {
        const $tbody = $(CONFIG.selectors.tbody);
        $tbody.empty();

        if (!items || items.length === 0) {
            $tbody.append('<tr><td colspan="10" class="text-center py-4">No purchase orders found</td></tr>');
            return;
        }

        const html = items.map(function (item, index) {
            const row = item.data || {};
            return `
                <tr data-index="${index}" class="purchase-order-row">
                    <td><input type="radio" name="selectedPurchaseOrder" value="${index}"></td>
                    <td style="white-space:nowrap;">
                        <a href="javascript:void(0)" class="purchase-order-link text-primary font-weight-bold" style="text-decoration:underline;">
                            ${row.poNo || ''}
                        </a>
                    </td>
                    <td>${row.poDateDisplay || ''}</td>
                    <td>${row.requestNo || ''}</td>
                    <td class="vni-font">${buildEllipsisCell(row.supplier || '', 'vni-font')}</td>
                    <td>${row.statusName || ''}</td>
                    <td>${row.purchaserCode || ''}</td>
                    <td>${row.chiefACode || ''}</td>
                    <td>${row.gDirectorCode || ''}</td>
                    <td class="vni-font">${row.remark || ''}</td>
                </tr>`;
        });

        $tbody.html(html.join(''));
    }

    $(document).off('click', '.purchase-order-row').on('click', '.purchase-order-row', function (e) {
        const isControl = $(e.target).closest('.purchase-order-link, input, button').length > 0;
        if (isControl) {
            return;
        }

        const $radio = $(this).find('input[name="selectedPurchaseOrder"]');
        if (!$radio.is(':checked')) {
            $radio.prop('checked', true).trigger('change');
        }
    });

    $(document).off('click', '.purchase-order-link').on('click', '.purchase-order-link', function (e) {
        e.preventDefault();
        const index = $(this).closest('tr').data('index');
        const item = state.currentDataRows[index];
        if (!item || !item.actions || !item.actions.canAccess) {
            alert('You have no permission to access this purchase order.');
            return;
        }

        const mode = item.actions.accessMode || 'view';
        window.location.href = buildDetailUrl(item.data.id, mode);
    });

    $(document).off('change', 'input[name="selectedPurchaseOrder"]').on('change', 'input[name="selectedPurchaseOrder"]', function () {
        const index = $(this).val();
        const item = state.currentDataRows[index];
        if (!item) {
            return;
        }

        state.selectedRow = item;
        $('#btnBackToProcessing').toggleClass('d-none', !item.actions.canBackToProcessing);
        $('#btnEstimateView').prop('disabled', !(item.actions && item.actions.canAccess));
    });

    $(document).off('click', '#pagination a.page-link').on('click', '#pagination a.page-link', function (e) {
        e.preventDefault();
        const page = $(this).data('page');
        if (page) {
            performSearch(page);
        }
    });

    $(document).off('click', '#btnAdd').on('click', '#btnAdd', function () {
        window.location.href = buildDetailUrl(null, 'add');
    });

    $(document).off('click', '#btnBackToProcessing').on('click', '#btnBackToProcessing', function () {
        if (!state.selectedRow || !state.selectedRow.actions.canBackToProcessing) {
            alert('Please select one purchase order waiting for approval.');
            return;
        }

        const targetUrl = buildDetailUrl(state.selectedRow.data.id, 'edit');
        window.location.href = targetUrl + '&openConvertModal=1';
    });

    $(document).off('click', '#btnExportExcel').on('click', '#btnExportExcel', function () {
        const params = new URLSearchParams();
        const poNo = $('#Filter_PONo').val();
        const requestNo = $('#Filter_RequestNo').val();
        const statusIds = getSelectedStatusIds();
        const supplierKeyword = $('#Filter_SupplierKeyword').val();
        const assessLevelId = $('#Filter_AssessLevelId').val();
        const remark = $('#Filter_Remark').val();
        const useDateRange = $('#Filter_UseDateRange').is(':checked');
        const fromDate = $('#Filter_FromDate').val();
        const toDate = $('#Filter_ToDate').val();

        if (poNo) params.set('Filter.PONo', poNo);
        if (requestNo) params.set('Filter.RequestNo', requestNo);
        statusIds.forEach((statusId) => {
            if (Number.isFinite(statusId)) {
                params.append('Filter.StatusIds', String(statusId));
            }
        });
        if (supplierKeyword) params.set('Filter.SupplierKeyword', supplierKeyword);
        if (assessLevelId) params.set('Filter.AssessLevelId', assessLevelId);
        if (remark) params.set('Filter.Remark', remark);
        params.set('Filter.UseDateRange', useDateRange ? 'true' : 'false');
        if (useDateRange && fromDate) params.set('Filter.FromDate', fromDate);
        if (useDateRange && toDate) params.set('Filter.ToDate', toDate);

        const query = params.toString();
        const exportUrlBase = window.purchaseOrderPage?.exportUrlBase || '?handler=ExportExcel';
        const separator = exportUrlBase.indexOf('?') >= 0 ? '&' : '?';
        const exportUrl = exportUrlBase + (query ? `${separator}${query}` : '');
        window.location.href = exportUrl;
    });

    // An nut thao tac cho den khi chon lai 1 dong.
    function resetActions() {
        state.selectedRow = null;
        $(CONFIG.selectors.actionBtns).addClass('d-none');
        $('input[name="selectedPurchaseOrder"]').prop('checked', false);
        $('#btnEstimateView').prop('disabled', true);
    }

    // Tao pager va dong tong so record ben duoi bang.
    function updatePagination(total, page, pageSize, totalPages) {
        const $pagination = $(CONFIG.selectors.pagination);
        $('#total-records-badge').text(`${total} records`);

        if (total === 0) {
            $('#pagination-info').html('<small>No records</small>');
            $pagination.empty();
            return;
        }

        const start = ((page - 1) * pageSize) + 1;
        const end = Math.min(page * pageSize, total);
        $('#pagination-info').html(`<small>Showing ${start}-${end} of ${total}</small>`);

        let html = `<li class="page-item ${page <= 1 ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${page - 1}">&laquo;</a></li>`;

        for (let i = 1; i <= totalPages; i += 1) {
            if (i === 1 || i === totalPages || (i >= page - 2 && i <= page + 2)) {
                html += `<li class="page-item ${i === page ? 'active' : ''}"><a class="page-link" href="#" data-page="${i}">${i}</a></li>`;
            } else if (i === page - 3 || i === page + 3) {
                html += '<li class="page-item disabled"><span class="page-link">...</span></li>';
            }
        }

        html += `<li class="page-item ${page >= totalPages ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${page + 1}">&raquo;</a></li>`;
        $pagination.html(html);
    }

    // Gan search form, date-range va search dau tien.
    function initializePage() {
        initializeSearchDateRange();
        initializeViewDetailDateRange();
        initializeStatusDropdown();
        initializeAjaxSelect2('#Filter_SupplierKeyword', window.purchaseOrderPage?.supplierLookupUrl || '', {
            minimumInputLength: 0,
            useSelectedTermWhenEmpty: true,
            templateResult: formatSupplierResult,
            templateSelection: formatSupplierSelection,
            escapeMarkup: function (markup) {
                return markup;
            }
        });
        syncDateRangeState();
        syncViewDetailDateRangeState();

        const queryPage = getQueryInt('Page') || getQueryInt('PageIndex') || 1;
        const queryPageSize = getQueryInt('PageSize');
        const pageSizeSelect = document.getElementById('purchaseOrderPageSize');
        const viewDetailPageSizeSelect = document.getElementById('purchaseOrderViewDetailPageSize');
        const pageSizeInput = document.getElementById('PageSize');
        const pageIndexInput = document.getElementById('PageIndex');

        if (queryPageSize) {
            pageSize = queryPageSize;
        } else if (pageSizeSelect) {
            const selectedPageSize = Number.parseInt(pageSizeSelect.value || '', 10);
            if (Number.isFinite(selectedPageSize) && selectedPageSize > 0) {
                pageSize = selectedPageSize;
            }
        }

        if (pageSizeSelect) {
            pageSizeSelect.value = String(pageSize);
            $(pageSizeSelect).off('change.poPageSize').on('change.poPageSize', function () {
                const nextPageSize = Number.parseInt(pageSizeSelect.value || '', 10);
                if (!Number.isFinite(nextPageSize) || nextPageSize <= 0 || nextPageSize === pageSize) {
                    return;
                }

                pageSize = nextPageSize;
                if (pageSizeInput) {
                    pageSizeInput.value = String(pageSize);
                }
                if (pageIndexInput) {
                    pageIndexInput.value = '1';
                }
                performSearch(1);
            });
        }

        if (pageSizeInput) {
            pageSizeInput.value = String(pageSize);
        }

        if (viewDetailPageSizeSelect) {
            viewDetailPageSizeSelect.value = String(viewDetailState.pageSize || pageSize);
            $(viewDetailPageSizeSelect).off('change.poViewDetailPageSize').on('change.poViewDetailPageSize', function () {
                const nextPageSize = Number.parseInt(viewDetailPageSizeSelect.value || '', 10);
                if (!Number.isFinite(nextPageSize) || nextPageSize <= 0 || nextPageSize === viewDetailState.pageSize) {
                    return;
                }

                viewDetailState.pageSize = nextPageSize;
                performViewDetailSearch(1);
            });
        }

        $('#purchaseOrderSearchForm').off('submit').on('submit', function (e) {
            e.preventDefault();
            if (pageIndexInput) {
                pageIndexInput.value = '1';
            }
            performSearch(1);
        });

        $('#Filter_UseDateRange').off('change').on('change', function () {
            syncDateRangeState();
        });

        $('#btnViewDetail').off('click').on('click', function () {
            resetViewDetailCriteria();
            $('#purchaseOrderViewDetailModal').modal('show');
            performViewDetailSearch(1);
        });

        $('#btnEstimateView').off('click').on('click', function () {
            if (!state.selectedRow || !state.selectedRow.data || !state.selectedRow.actions || !state.selectedRow.actions.canAccess) {
                alert('Please select one purchase order first.');
                return;
            }

            const po = state.selectedRow.data || {};
            const poNo = (po.poNo || '').toString().trim();
            $('#purchaseOrderEstimateViewTitle').text(poNo ? `Estimate View - ${poNo}` : 'Estimate View');
            showEstimateViewLoading(true);
            $('#purchaseOrderEstimateViewModal').modal('show');
            loadEstimateView(po.id);
        });

        $('#ViewDetail_UsePoDateRange').off('change').on('change', function () {
            syncViewDetailDateRangeState();
        });

        $('#ViewDetail_UseRecDateRange').off('change').on('change', function () {
            syncViewDetailDateRangeState();
        });

        $('#btnSearchViewDetail').off('click').on('click', function () {
            performViewDetailSearch(1);
        });

        $('#btnViewDetailReport').off('click').on('click', function () {
            if (!viewDetailState.lastRequest) {
                alert('Please search detail first.');
                return;
            }

            if (!viewDetailState.lastTotal) {
                alert('No detail rows to export.');
                return;
            }

            const params = buildViewDetailQueryParams(viewDetailState.lastRequest);
            const reportUrlBase = window.purchaseOrderPage?.reportDetailQuestPdfUrl || '';
            const reportUrl = reportUrlBase ? (params ? `${reportUrlBase}&${params}` : reportUrlBase) : '';

            if (!reportUrl) {
                alert('Purchase order report is not available.');
                return;
            }

            downloadPurchaseOrderPdf(reportUrl, 'PurchaseOrder_Report.pdf');
        });

        $('#btnViewDetailExportExcel').off('click').on('click', function () {
            if (!viewDetailState.lastRequest) {
                alert('Please search detail first.');
                return;
            }

            if (!viewDetailState.lastTotal) {
                alert('No detail rows to export.');
                return;
            }

            const params = buildViewDetailQueryParams(viewDetailState.lastRequest);
            const exportUrl = `?handler=ExportDetailExcel${params ? `&${params}` : ''}`;
            window.location.href = exportUrl;
        });

async function downloadPurchaseOrderPdf(reportUrl, fileName) {
    if (!reportUrl) {
        alert('Purchase order report is not available.');
        return;
    }

    try {
        const response = await fetch(reportUrl, {
            method: 'GET',
            credentials: 'same-origin'
        });

        if (!response.ok) {
            throw new Error(`Cannot export PDF. HTTP ${response.status}`);
        }

        const contentType = response.headers.get('content-type') || '';
        if (!contentType.toLowerCase().includes('pdf')) {
            throw new Error('Purchase order report did not return a PDF file.');
        }

        const blob = await response.blob();
        const blobUrl = window.URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = blobUrl;
        link.download = fileName || 'PurchaseOrder_Report.pdf';
        document.body.appendChild(link);
        link.click();
        link.remove();
        window.setTimeout(function () {
            window.URL.revokeObjectURL(blobUrl);
        }, 1000);
    } catch (error) {
        console.error(error);
        alert(error?.message || 'Cannot export PDF.');
    }
}

        $(document).off('click', '#purchaseOrderViewDetailPagination a.page-link').on('click', '#purchaseOrderViewDetailPagination a.page-link', function (e) {
            e.preventDefault();
            const page = parseInt($(this).data('page'), 10);
            if (page && page !== viewDetailState.currentPage) {
                performViewDetailSearch(page);
            }
        });

        state.currentPage = queryPage;
        performSearch(queryPage);
    }

    // Hien dong loading don gian trong luc dang search.
    function showLoading(show) {
        if (show) {
            $(CONFIG.selectors.tbody).html('<tr><td colspan="10" class="text-center py-4"><div class="spinner-border spinner-border-sm text-primary"></div></td></tr>');
        }
    }

    // Hien dong loi don gian neu search fail.
    function showError(message) {
        $(CONFIG.selectors.tbody).html(`<tr><td colspan="10" class="text-center text-danger py-4">${message}</td></tr>`);
    }

    // Dong bo widget ngay trong modal View Detail.
    function initializeViewDetailDateRange() {
        if (typeof window.initSimpleDateRange !== 'function') {
            return;
        }

        window.initSimpleDateRange('ViewDetailPoDateRange', '#ViewDetail_PoFromDate', '#ViewDetail_PoToDate', {
            linkedCalendars: false
        });

        window.initSimpleDateRange('ViewDetailRecDateRange', '#ViewDetail_RecFromDate', '#ViewDetail_RecToDate', {
            linkedCalendars: false
        });
    }

    // Bat/tat 2 o ngay theo trang thai checkbox.
    function syncDateRangeState() {
        const enabled = $('#Filter_UseDateRange').is(':checked');
        $('#PoDateRange').prop('disabled', !enabled);
        if (!enabled) {
            $('#Filter_FromDate').val('');
            $('#Filter_ToDate').val('');
            $('#PoDateRange').val('');
        } else if (typeof window.setDateRangeValue === 'function') {
            const fromDate = $('#Filter_FromDate').val();
            const toDate = $('#Filter_ToDate').val();
            if (fromDate && toDate) {
                window.setDateRangeValue('PoDateRange', fromDate, toDate);
            }
        }
    }

    // Dua filter ben search chinh sang modal View Detail de nguoi dung sua tiep.
    function prefillViewDetailCriteriaFromMain() {
        applyDefaultViewDetailPoDateRange();
        syncViewDetailDateRangeState();
    }

    function applyDefaultViewDetailPoDateRange() {
        const fromDate = $('#Filter_FromDate').val() || getDefaultDetailFromDate();
        const toDate = $('#Filter_ToDate').val() || getDefaultDetailToDate();

        $('#ViewDetail_UsePoDateRange').prop('checked', true);
        $('#ViewDetail_PoFromDate').val(fromDate);
        $('#ViewDetail_PoToDate').val(toDate);

        if (typeof window.setDateRangeValue === 'function') {
            window.setDateRangeValue('ViewDetailPoDateRange', fromDate, toDate);
        } else {
            $('#ViewDetailPoDateRange').val(`${fromDate} - ${toDate}`);
        }
    }

    function getDefaultDetailFromDate() {
        const date = new Date();
        date.setDate(date.getDate() - 30);
        return formatDateForInput(date);
    }

    function getDefaultDetailToDate() {
        return formatDateForInput(new Date());
    }

    function formatDateForInput(date) {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
    }

    // Bat/tat date-range trong modal View Detail.
    function syncViewDetailDateRangeState() {
        const poEnabled = $('#ViewDetail_UsePoDateRange').is(':checked');
        const recEnabled = $('#ViewDetail_UseRecDateRange').is(':checked');

        $('#ViewDetailPoDateRange').prop('disabled', !poEnabled);
        $('#ViewDetailRecDateRange').prop('disabled', !recEnabled);

        if (!poEnabled) {
            $('#ViewDetail_PoFromDate').val('');
            $('#ViewDetail_PoToDate').val('');
            $('#ViewDetailPoDateRange').val('');
        }

        if (!recEnabled) {
            $('#ViewDetail_RecFromDate').val('');
            $('#ViewDetail_RecToDate').val('');
            $('#ViewDetailRecDateRange').val('');
        }
    }

    // Moi lan mo popup thi chi giu PO Date theo man chinh, con lai reset ve trong.
    function resetViewDetailCriteria() {
        $('#ViewDetail_ItemCode').val('');
        $('#ViewDetail_RecQtyOp').val('=');
        $('#ViewDetail_RecQty').val('');
        $('#ViewDetail_Renovation').prop('checked', false);
        $('#ViewDetail_General').prop('checked', false);
        $('#ViewDetail_ForDeptId').val('');
        $('#ViewDetail_ItemNotInclude').val('');
        $('#ViewDetail_SupplierName').val('');

        applyDefaultViewDetailPoDateRange();
        $('#ViewDetail_UseRecDateRange').prop('checked', false);
        $('#ViewDetail_RecFromDate').val('');
        $('#ViewDetail_RecToDate').val('');
        $('#ViewDetailRecDateRange').val('');
        viewDetailState.pageSize = pageSize || viewDetailState.pageSize || 10;
        const pageSizeSelect = document.getElementById('purchaseOrderViewDetailPageSize');
        if (pageSizeSelect) {
            pageSizeSelect.value = String(viewDetailState.pageSize);
        }
    }

    // Search detail PO trong modal theo PO Date.
    function performViewDetailSearch(page = 1) {
        const token = $('input[name="__RequestVerificationToken"]').val();
        const request = buildViewDetailRequest(page);

        showViewDetailLoading(true);

        $.ajax({
            url: '?handler=SearchDetail',
            type: 'POST',
            contentType: 'application/json',
            headers: { 'RequestVerificationToken': token },
            data: JSON.stringify(request),
            success: function (response) {
                if (response.success) {
                    viewDetailState.lastRequest = { ...request };
                    viewDetailState.lastRows = Array.isArray(response.data) ? response.data : [];
                    viewDetailState.lastTotal = response.total || 0;
                    viewDetailState.currentPage = response.page || page;
                    viewDetailState.pageSize = response.pageSize || request.pageSize || pageSize || 0;
                    renderViewDetailRows(response.data || []);
                    $('#purchaseOrderViewDetailCount').text(`Total Record(s): ${(response.total || 0)}`);
                    updateViewDetailPagination(response.total || 0, response.page || page, response.pageSize || request.pageSize || pageSize || 0, response.totalPages || 1);
                } else {
                    viewDetailState.lastRequest = { ...request };
                    viewDetailState.lastRows = [];
                    viewDetailState.lastTotal = 0;
                    updateViewDetailPagination(0, 1, request.pageSize || pageSize || 0, 1);
                    showViewDetailError(response.message || 'Search detail failed.');
                }
            },
            error: function (xhr, status, error) {
                viewDetailState.lastRows = [];
                viewDetailState.lastTotal = 0;
                updateViewDetailPagination(0, 1, request.pageSize || pageSize || 0, 1);
                showViewDetailError('System connection error: ' + error);
            },
            complete: function () {
                showViewDetailLoading(false);
            }
        });
    }

    // Hien loading cho modal View Detail.
    function showViewDetailLoading(show) {
        if (show) {
            $('#purchaseOrderViewDetailRows').html('<tr><td colspan="10" class="text-center text-muted py-4">No data</td></tr>');
        }
    }

    // Hien loi trong modal View Detail.
    function showViewDetailError(message) {
        $('#purchaseOrderViewDetailRows').html(`<tr><td colspan="10" class="text-center text-danger py-4">${escapeHtml(message)}</td></tr>`);
    }

    // Ve lai bang detail ben modal View Detail.
    function renderViewDetailRows(items) {
        const $tbody = $('#purchaseOrderViewDetailRows');
        if (!items || items.length === 0) {
            $tbody.html('<tr><td colspan="10" class="text-center text-muted py-4">No detail rows</td></tr>');
            $('#purchaseOrderViewDetailCount').text('Total Record(s): 0');
            return;
        }

        const html = items.map(function (item) {
            const row = item || {};
            return `
                <tr>
                    <td>${buildEllipsisCell(row.itemCode || '')}</td>
                    <td class="tcvn3-font">${buildEllipsisCell(row.itemName || '', 'tcvn3-font')}</td>
                    <td class="text-right">${formatNumber(row.quantity || 0)}</td>
                    <td class="text-right">${formatNumber(row.unitPrice || 0)}</td>
                    <td class="text-right">${formatNumber(row.poAmount || 0)}</td>
                    <td>${escapeHtml(row.forDepartment || '')}</td>
                    <td class="vni-font">${escapeHtml(row.note || '')}</td>
                    <td class="text-right">${formatNumber(row.recQty || 0)}</td>
                    <td class="text-right">${formatNumber(row.recAmount || 0)}</td>
                    <td>${escapeHtml(row.recDateDisplay || '')}</td>
                </tr>`;
        });

        $tbody.html(html.join(''));
    }

    function showEstimateViewLoading(show) {
        if (show) {
            $('#purchaseOrderEstimateViewRows').html('<tr><td colspan="5" class="text-center text-muted py-4">Loading...</td></tr>');
        }
    }

    function showEstimateViewError(message) {
        $('#purchaseOrderEstimateViewRows').html(`<tr><td colspan="5" class="text-center text-danger py-4">${escapeHtml(message)}</td></tr>`);
    }

    function renderEstimateViewRows(items) {
        const $tbody = $('#purchaseOrderEstimateViewRows');
        if (!items || items.length === 0) {
            $tbody.html('<tr><td colspan="5" class="text-center text-muted py-4">No estimate data</td></tr>');
            return;
        }

        const html = items.map(function (item, index) {
            const row = item || {};
            return `
                <tr>
                    <td class="text-center">${index + 1}</td>
                    <td class="vni-font">${buildEllipsisCell(row.poIndexDetailName || '', 'vni-font')}</td>
                    <td class="text-center">${formatNumber(row.point || 0)}</td>
                    <td>${escapeHtml(row.theDate || '')}</td>
                    <td>${buildEllipsisCell(row.employeeName || '')}</td>
                </tr>`;
        });

        $tbody.html(html.join(''));
    }

    function loadEstimateView(poId) {
        if (!poId || poId <= 0) {
            showEstimateViewError('Please select purchase order first.');
            return;
        }

        $.ajax({
            url: '?handler=EstimateView',
            type: 'GET',
            data: { poId: poId },
            success: function (response) {
                if (response && response.success) {
                    renderEstimateViewRows(response.data || []);
                } else {
                    showEstimateViewError((response && response.message) || 'Load estimate view failed.');
                }
            },
            error: function (xhr, status, error) {
                showEstimateViewError('System connection error: ' + error);
            }
        });
    }

    function updateViewDetailPagination(total, page, pageSize, totalPages) {
        const $pagination = $('#purchaseOrderViewDetailPagination');
        const $info = $('#purchaseOrderViewDetailPaginationInfo');

        if (!total) {
            $pagination.empty();
            $info.html('<small>No records</small>');
            return;
        }

        const safePageSize = pageSize > 0 ? pageSize : (viewDetailState.pageSize || 0);
        const safePage = page > 0 ? page : 1;
        const start = ((safePage - 1) * safePageSize) + 1;
        const end = Math.min(safePage * safePageSize, total);
        $info.html(`<small>Showing ${start}-${end} of ${total}</small>`);

        let html = `<li class="page-item ${safePage <= 1 ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${safePage - 1}">&laquo;</a></li>`;
        for (let i = 1; i <= totalPages; i += 1) {
            if (i === 1 || i === totalPages || (i >= safePage - 2 && i <= safePage + 2)) {
                html += `<li class="page-item ${i === safePage ? 'active' : ''}"><a class="page-link" href="#" data-page="${i}">${i}</a></li>`;
            } else if (i === safePage - 3 || i === safePage + 3) {
                html += '<li class="page-item disabled"><span class="page-link">...</span></li>';
            }
        }
        html += `<li class="page-item ${safePage >= totalPages ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${safePage + 1}">&raquo;</a></li>`;
        $pagination.html(html);
    }

    function escapeHtml(value) {
        return String(value || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function buildEllipsisCell(input, extraClass) {
        const raw = (input || '').toString();
        const className = extraClass ? `app-table-ellipsis ${extraClass}` : 'app-table-ellipsis';
        return `<span class="${className}" title="${escapeHtml(raw)}">${escapeHtml(raw)}</span>`;
    }

    function formatNumber(value) {
        const number = Number(value || 0);
        if (Number.isNaN(number)) {
            return '0';
        }

        return number.toLocaleString('en-US', {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        });
    }

    function buildViewDetailRequest(page = 1) {
        const request = {
            itemCode: $('#ViewDetail_ItemCode').val() || null,
            recQtyOperator: $('#ViewDetail_RecQtyOp').val() || '=',
            recQtyValue: $('#ViewDetail_RecQty').val() ? parseFloat($('#ViewDetail_RecQty').val()) : null,
            renovation: $('#ViewDetail_Renovation').is(':checked'),
            general: $('#ViewDetail_General').is(':checked'),
            forDeptId: $('#ViewDetail_ForDeptId').val() ? parseInt($('#ViewDetail_ForDeptId').val(), 10) : null,
            itemNotInclude: $('#ViewDetail_ItemNotInclude').val() || null,
            supplierName: $('#ViewDetail_SupplierName').val() || null,
            usePoDateRange: $('#ViewDetail_UsePoDateRange').is(':checked'),
            poFromDate: $('#ViewDetail_PoFromDate').val() || null,
            poToDate: $('#ViewDetail_PoToDate').val() || null,
            useRecDateRange: $('#ViewDetail_UseRecDateRange').is(':checked'),
            recFromDate: $('#ViewDetail_RecFromDate').val() || null,
            recToDate: $('#ViewDetail_RecToDate').val() || null,
            page: page,
            pageSize: viewDetailState.pageSize || pageSize || 0
        };

        if (!request.usePoDateRange) {
            request.poFromDate = null;
            request.poToDate = null;
        }

        if (!request.useRecDateRange) {
            request.recFromDate = null;
            request.recToDate = null;
        }

        if (!request.recQtyValue && request.recQtyValue !== 0) {
            request.recQtyValue = null;
        }

        return request;
    }

    function buildViewDetailQueryParams(request) {
        const params = new URLSearchParams();
        const source = request || buildViewDetailRequest();

        if (source.itemCode) params.set('ItemCode', source.itemCode);
        if (source.recQtyOperator) params.set('RecQtyOperator', source.recQtyOperator);
        if (source.recQtyValue !== null && source.recQtyValue !== undefined) params.set('RecQtyValue', source.recQtyValue);
        if (source.renovation) params.set('Renovation', 'true');
        if (source.general) params.set('General', 'true');
        if (source.forDeptId) params.set('ForDeptId', source.forDeptId);
        if (source.itemNotInclude) params.set('ItemNotInclude', source.itemNotInclude);
        if (source.supplierName) params.set('SupplierName', source.supplierName);
        params.set('UsePoDateRange', source.usePoDateRange ? 'true' : 'false');
        if (source.usePoDateRange && source.poFromDate) params.set('PoFromDate', source.poFromDate);
        if (source.usePoDateRange && source.poToDate) params.set('PoToDate', source.poToDate);
        params.set('UseRecDateRange', source.useRecDateRange ? 'true' : 'false');
        if (source.useRecDateRange && source.recFromDate) params.set('RecFromDate', source.recFromDate);
        if (source.useRecDateRange && source.recToDate) params.set('RecToDate', source.recToDate);

        return params.toString();
    }

    // Tao lai URL detail va giu nguyen filter hien tai trong returnUrl.
    function buildDetailUrl(id, mode) {
        const base = window.purchaseOrderPage?.detailUrlBase || '';
        const params = new URLSearchParams();
        params.set('mode', mode || 'view');

        if (id) {
            params.set('id', id);
        }

        params.set('returnUrl', buildReturnUrl());
        return `${base}?${params.toString()}`;
    }

    // Dong goi filter hien tai de detail quay ve dung trang danh sach cu.
    function buildReturnUrl() {
        const params = new URLSearchParams();
        const poNo = $('#Filter_PONo').val();
        const requestNo = $('#Filter_RequestNo').val();
        const statusIds = getSelectedStatusIds();
        const supplierKeyword = $('#Filter_SupplierKeyword').val();
        const assessLevelId = $('#Filter_AssessLevelId').val();
        const remark = $('#Filter_Remark').val();
        const useDateRange = $('#Filter_UseDateRange').is(':checked');
        const fromDate = $('#Filter_FromDate').val();
        const toDate = $('#Filter_ToDate').val();

        if (poNo) params.set('Filter.PONo', poNo);
        if (requestNo) params.set('Filter.RequestNo', requestNo);
        statusIds.forEach((statusId) => {
            if (Number.isFinite(statusId)) {
                params.append('Filter.StatusIds', String(statusId));
            }
        });
        if (supplierKeyword) params.set('Filter.SupplierKeyword', supplierKeyword);
        if (assessLevelId) params.set('Filter.AssessLevelId', assessLevelId);
        if (remark) params.set('Filter.Remark', remark);
        params.set('Filter.UseDateRange', useDateRange ? 'true' : 'false');
        if (useDateRange && fromDate) params.set('Filter.FromDate', fromDate);
        if (useDateRange && toDate) params.set('Filter.ToDate', toDate);
        params.set('Filter.Page', String(state.currentPage || 1));
        params.set('Filter.PageSize', String(pageSize || defaultPageSize || 10));

        return window.location.pathname + (params.toString() ? `?${params.toString()}` : '');
    }

    $(document).ready(initializePage);
})();
