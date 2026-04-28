(function () {
    'use strict';

    let pageSize = typeof defaultPageSize !== 'undefined' ? defaultPageSize : 10;
    let selectedRequestNo = null;
    let currentPage = 1;
    let currentDataRows = [];
    let searchDetailState = {
        currentPage: 1,
        pageSize: typeof defaultPageSize !== 'undefined' ? defaultPageSize : 10,
        lastTotal: 0
    };

    function updateHistoryButtonState() {
        $('#btnViewHistory').prop('disabled', !selectedRequestNo);
    }

    function buildCurrentReturnUrl() {
        return `${window.location.pathname}${window.location.search || ''}`;
    }

    function getQueryInt(name) {
        try {
            const raw = new URLSearchParams(window.location.search).get(name);
            if (raw === null || raw === undefined || String(raw).trim() === '') {
                return null;
            }

            const parsed = Number.parseInt(String(raw).trim(), 10);
            return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
        } catch {
            return null;
        }
    }

    function initializeSearchDateRange() {
        if (typeof window.initSimpleDateRange !== 'function') {
            return;
        }

        window.initSimpleDateRange('MrDateRange', '#Filter_FromDate', '#Filter_ToDate', {
            linkedCalendars: false
        });

        const fromDate = $('#Filter_FromDate').val();
        const toDate = $('#Filter_ToDate').val();
        if (fromDate && toDate && typeof window.setDateRangeValue === 'function') {
            window.setDateRangeValue('MrDateRange', fromDate, toDate);
        }
    }

    function syncBrowserUrlToSearchState(page, filter) {
        const form = document.getElementById('mrSearchForm');
        if (!form || !window.history || typeof window.history.replaceState !== 'function') {
            return;
        }

        const params = new URLSearchParams();
        params.set('Filter.PageIndex', String(page > 0 ? page : 1));
        params.set('Filter.PageSize', String(pageSize));

        const requestNo = (filter && filter.requestNo ? filter.requestNo : '').toString().trim();
        if (requestNo) {
            params.set('Filter.RequestNo', requestNo);
        }

        const storeGroup = filter && Number.isFinite(filter.storeGroup) ? filter.storeGroup : null;
        if (storeGroup !== null) {
            params.set('Filter.StoreGroup', String(storeGroup));
        }

        const statusIds = Array.isArray(filter && filter.statusIds) ? filter.statusIds : [];
        statusIds.forEach((statusId) => {
            if (Number.isFinite(statusId)) {
                params.append('Filter.StatusIds', String(statusId));
            }
        });

        const itemCode = (filter && filter.itemCode ? filter.itemCode : '').toString().trim();
        if (itemCode) {
            params.set('Filter.ItemCode', itemCode);
        }

        params.set('Filter.IssueLessThanOrder', filter && filter.issueLessThanOrder ? 'true' : 'false');
        params.set('Filter.BuyGreaterThanZero', filter && filter.buyGreaterThanZero ? 'true' : 'false');
        params.set('Filter.AutoOnly', filter && filter.autoOnly ? 'true' : 'false');

        const fromDate = (filter && filter.fromDate ? filter.fromDate : '').toString().trim();
        if (fromDate) {
            params.set('Filter.FromDate', fromDate);
        }

        const toDate = (filter && filter.toDate ? filter.toDate : '').toString().trim();
        if (toDate) {
            params.set('Filter.ToDate', toDate);
        }

        const accordingToKeyword = (filter && filter.accordingToKeyword ? filter.accordingToKeyword : '').toString().trim();
        if (accordingToKeyword) {
            params.set('Filter.AccordingToKeyword', accordingToKeyword);
        }

        params.set('Filter.ConditionMode', (filter && filter.conditionMode) || 'allUsers');
        const query = params.toString();
        const nextUrl = query ? `${window.location.pathname}?${query}` : window.location.pathname;
        window.history.replaceState({}, '', nextUrl);
    }

    function formatLocalDate(date) {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
    }

    function buildDetailUrl(requestNo, mode) {
        const url = new URL('/Purchasing/MaterialRequest/MaterialRequestDetail', window.location.origin);
        url.searchParams.set('id', requestNo || '');
        url.searchParams.set('mode', mode || 'view');
        url.searchParams.set('returnUrl', buildCurrentReturnUrl());
        return url.toString();
    }

    // ========== SEARCH FUNCTION ==========
    function performSearch(page = 1) {
        currentPage = page;
        const token = $('input[name="__RequestVerificationToken"]').val();

        const statusIds = $('.mr-status-checkbox:checked')
            .map(function () { return Number.parseInt(this.value, 10); })
            .get()
            .filter((x) => Number.isFinite(x));

        const requestNoRaw = ($('#Filter_RequestNo').val() || '').toString().trim();

        const storeGroupRaw = ($('#Filter_StoreGroup').val() || '').toString().trim();
        const storeGroup = storeGroupRaw === '' ? null : Number(storeGroupRaw);

        const fromDateRaw = ($('#Filter_FromDate').val() || '').toString().trim();
        const toDateRaw = ($('#Filter_ToDate').val() || '').toString().trim();

        const filter = {
            requestNo: requestNoRaw || null,
            storeGroup: Number.isFinite(storeGroup) ? storeGroup : null,
            statusIds: statusIds,
            itemCode: ($('#Filter_ItemCode').val() || '').toString().trim() || null,
            issueLessThanOrder: $('#Filter_IssueLessThanOrder').is(':checked'),
            buyGreaterThanZero: $('#Filter_BuyGreaterThanZero').is(':checked'),
            autoOnly: $('#Filter_AutoOnly').is(':checked'),
            fromDate: fromDateRaw || null,
            toDate: toDateRaw || null,
            accordingToKeyword: ($('#Filter_AccordingToKeyword').val() || '').toString().trim() || null,
            conditionMode: $('input[name="Filter.ConditionMode"]:checked').val() || 'allUsers',
            page: currentPage,
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
                if (response && response.success) {
                    currentDataRows = response.data || [];
                    renderContracts(response.data);
                    updatePagination(response.total, response.page, response.pageSize, response.totalPages);
                    syncBrowserUrlToSearchState(response.page || page, filter);
                    resetActions();
                } else {
                    showError('Search failed: ' + (response && response.message ? response.message : ''));
                }
            },
            error: function (xhr, status, error) {
                showError('Error: ' + error);
            },
            complete: function () {
                showLoading(false);
            }
        });
    }

    // ========== RENDER BẢNG ==========
    function renderContracts(items) {
        const tbody = $('#mrTable tbody');
        tbody.empty();

        if (!items || items.length === 0) {
            tbody.append('<tr><td colspan="8" class="text-center py-4">No requests found</td></tr>');
            return;
        }

        items.forEach(function (item, index) {
            const r = item.data || {};
            const perms = item.actions || {};
            const requestNo = r.requestNo ?? r.RequestNo;
            const canAccess = perms.canAccess === true;
            const requestNoHtml = canAccess
                ? `<a href="javascript:void(0)" class="mr-request-link text-primary font-weight-bold" style="text-decoration:underline">
                        ${requestNo || ''}
                    </a>`
                : `<span class="font-weight-bold">
                        ${requestNo || ''}
                    </span>`;
            const row = `
            <tr data-id="${requestNo || ''}" data-index="${index}" tabindex="0" style="cursor:${canAccess ? 'pointer' : 'default'}">
                <td><input type="radio" name="selectedMr" value="${index}"></td>
                <td style="white-space:nowrap">
                    ${requestNoHtml}
                </td>
                <td>${r.dateCreateDisplay || r.DateCreateDisplay || ''}</td>
                <td class="vni-font">${buildEllipsisCell(r.accordingTo || r.AccordingTo || '')}</td>
                <td class="vni-font">${buildEllipsisCell(r.kpGroupName || r.KPGroupName || r.kPGroupName || '')}</td>
                <td>${r.materialStatusName || r.MaterialStatusName || ''}</td>
                <td>${r.prNo || r.PrNo || ''}</td>
                <td style="display:none">${r.materialStatusId || r.MaterialStatusId || ''}</td>
            </tr>`;
            tbody.append(row);
        });
    }

    function selectRowByIndex(rowIndex) {
        if (rowIndex === undefined || rowIndex === null) return;

        const $row = $(`#mrTable tbody tr[data-index='${rowIndex}']`);
        if ($row.length === 0) return;

        const $radio = $row.find("input[name='selectedMr']");
        if ($radio.length > 0) {
            $radio.prop('checked', true).trigger('change');
        }
    }

    function openDetailByRowIndex(rowIndex) {
        const item = currentDataRows[rowIndex];
        if (!item || !item.actions) {
            console.error('This request does not exist !');
            return;
        }

        const perms = item.actions;
        const requestNo = item.data.requestNo || item.data.RequestNo;

        if (perms.canAccess !== true) {
            return;
        }

        const mode = perms.accessMode || 'view';
        window.location.href = buildDetailUrl(requestNo, mode);
    }

    function escapeHtml(value) {
        return (value == null ? '' : String(value))
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function buildEllipsisCell(input) {
        const raw = (input || '').toString();
        return `<span class="app-table-ellipsis" title="${escapeHtml(raw)}">${escapeHtml(raw)}</span>`;
    }

    function renderHistoryRows(rows) {
        const $body = $('#mrHistoryBody');
        const $emptyRow = $('#mrHistoryEmptyRow');

        if ($body.length === 0) {
            return;
        }

        if (!Array.isArray(rows) || rows.length === 0) {
            $body.html('<tr id="mrHistoryEmptyRow"><td colspan="3" class="text-center text-muted">No history</td></tr>');
            return;
        }

        const html = rows.map(function (row) {
            return `<tr>
                <td>${escapeHtml(row.employeeDisplayName || '')}</td>
                <td>${escapeHtml(row.timeEffectiveDisplay || row.timeEffective || '')}</td>
                <td>${escapeHtml(row.note || '')}</td>
            </tr>`;
        }).join('');

        $body.html(html);
        if ($emptyRow.length > 0) {
            $emptyRow.remove();
        }
    }

    function loadHistory(requestNo) {
        const $modal = $('#mrHistoryModal');
        const $body = $('#mrHistoryBody');

        if (!requestNo) {
            return;
        }

        $('#mrHistoryModalLabel').text(`History Material Request: Request No: #${requestNo}`);
        $body.html('<tr><td colspan="3" class="text-center text-muted py-4">Loading...</td></tr>');

        $.ajax({
            url: '?handler=History',
            type: 'GET',
            data: { requestNo: requestNo },
            success: function (response) {
                if (!response || response.success !== true) {
                    $body.html('<tr id="mrHistoryEmptyRow"><td colspan="5" class="text-center text-muted">No history</td></tr>');
                    return;
                }

                renderHistoryRows(response.rows || []);
            },
            error: function () {
                $body.html('<tr id="mrHistoryEmptyRow"><td colspan="5" class="text-center text-muted">No history</td></tr>');
            }
        });

        $modal.modal({ backdrop: 'static', keyboard: false, show: true });
    }

    function initializeSearchDetailDateRange() {
        if (typeof window.initSimpleDateRange !== 'function') {
            return;
        }

        window.initSimpleDateRange('MrSearchDetailDateRange', '#MrSearchDetail_FromDate', '#MrSearchDetail_ToDate', {
            linkedCalendars: false
        });
    }

    function applyDefaultSearchDetailDateRange() {
        const fromDate = $('#Filter_FromDate').val() || formatLocalDate(new Date(new Date().setMonth(new Date().getMonth() - 3)));
        const toDate = $('#Filter_ToDate').val() || formatLocalDate(new Date());

        $('#MrSearchDetail_UseDateRange').prop('checked', true);
        $('#MrSearchDetail_FromDate').val(fromDate);
        $('#MrSearchDetail_ToDate').val(toDate);

        if (typeof window.setDateRangeValue === 'function') {
            window.setDateRangeValue('MrSearchDetailDateRange', fromDate, toDate);
        } else {
            $('#MrSearchDetailDateRange').val(`${fromDate} - ${toDate}`);
        }
    }

    function syncSearchDetailDateRangeState() {
        const enabled = $('#MrSearchDetail_UseDateRange').is(':checked');
        $('#MrSearchDetailDateRange').prop('disabled', !enabled);
        if (!enabled) {
            $('#MrSearchDetail_FromDate').val('');
            $('#MrSearchDetail_ToDate').val('');
            $('#MrSearchDetailDateRange').val('');
        }
    }

    function resetSearchDetailCriteria() {
        $('#MrSearchDetail_RequestNo').val($('#Filter_RequestNo').val() || '');
        $('#MrSearchDetail_StoreGroup').val($('#Filter_StoreGroup').val() || '');
        $('#MrSearchDetail_ItemCode').val($('#Filter_ItemCode').val() || '');
        $('#MrSearchDetail_ItemName').val('');
        $('#MrSearchDetail_IssuedLessThanOrder').prop('checked', false);
        applyDefaultSearchDetailDateRange();
        syncSearchDetailDateRangeState();

        const pageSizeSelect = document.getElementById('mrSearchDetailPageSize');
        searchDetailState.pageSize = pageSize || searchDetailState.pageSize || 10;
        if (pageSizeSelect) {
            pageSizeSelect.value = String(searchDetailState.pageSize);
        }
    }

    function buildSearchDetailRequest(page = 1) {
        const storeGroupRaw = ($('#MrSearchDetail_StoreGroup').val() || '').toString().trim();
        const storeGroup = storeGroupRaw === '' ? null : Number.parseInt(storeGroupRaw, 10);

        return {
            requestNo: ($('#MrSearchDetail_RequestNo').val() || '').toString().trim() || null,
            storeGroup: Number.isFinite(storeGroup) ? storeGroup : null,
            itemCode: ($('#MrSearchDetail_ItemCode').val() || '').toString().trim() || null,
            itemName: ($('#MrSearchDetail_ItemName').val() || '').toString().trim() || null,
            issuedLessThanOrder: $('#MrSearchDetail_IssuedLessThanOrder').is(':checked'),
            useDateRange: $('#MrSearchDetail_UseDateRange').is(':checked'),
            fromDate: $('#MrSearchDetail_FromDate').val() || null,
            toDate: $('#MrSearchDetail_ToDate').val() || null,
            page: page,
            pageSize: searchDetailState.pageSize || pageSize || 10
        };
    }

    function performSearchDetail(page = 1) {
        const token = $('input[name="__RequestVerificationToken"]').val();
        const request = buildSearchDetailRequest(page);
        showSearchDetailLoading(true);

        $.ajax({
            url: '?handler=SearchDetail',
            type: 'POST',
            contentType: 'application/json',
            headers: { 'RequestVerificationToken': token },
            data: JSON.stringify(request),
            success: function (response) {
                if (response && response.success) {
                    searchDetailState.currentPage = response.page || page;
                    searchDetailState.pageSize = response.pageSize || request.pageSize || pageSize || 10;
                    searchDetailState.lastTotal = response.total || 0;
                    renderSearchDetailRows(response.data || []);
                    updateSearchDetailPagination(response.total || 0, response.page || page, response.pageSize || request.pageSize || pageSize || 10, response.totalPages || 1);
                } else {
                    searchDetailState.lastTotal = 0;
                    updateSearchDetailPagination(0, 1, request.pageSize || pageSize || 10, 1);
                    showSearchDetailError((response && response.message) || 'Search detail failed.');
                }
            },
            error: function (xhr, status, error) {
                searchDetailState.lastTotal = 0;
                updateSearchDetailPagination(0, 1, request.pageSize || pageSize || 10, 1);
                showSearchDetailError('System connection error: ' + error);
            },
            complete: function () {
                showSearchDetailLoading(false);
            }
        });
    }

    function showSearchDetailLoading(show) {
        if (show) {
            $('#mrSearchDetailRows').html('<tr><td colspan="11" class="text-center text-muted py-4">Loading...</td></tr>');
        }
    }

    function showSearchDetailError(message) {
        $('#mrSearchDetailRows').html(`<tr><td colspan="11" class="text-center text-danger py-4">${escapeHtml(message)}</td></tr>`);
    }

    function renderSearchDetailRows(items) {
        const $tbody = $('#mrSearchDetailRows');
        if (!items || items.length === 0) {
            $tbody.html('<tr><td colspan="11" class="text-center text-muted py-4">No detail rows</td></tr>');
            return;
        }

        const html = items.map(function (item) {
            const row = item || {};
            return `<tr>
                <td>${escapeHtml(row.requestNo || row.RequestNo || '')}</td>
                <td>${escapeHtml(row.dateCreateDisplay || row.DateCreateDisplay || '')}</td>
                <td class="vni-font">${buildEllipsisCell(row.accordingTo || row.AccordingTo || '')}</td>
                <td class="vni-font">${buildEllipsisCell(row.kpGroupName || row.KPGroupName || '')}</td>
                <td>${buildEllipsisCell(row.itemCode || row.ItemCode || '')}</td>
                <td class="tcvn3-font">${buildEllipsisCell(row.itemName || row.ItemName || '')}</td>
                <td class="text-right">${formatDetailNumber(row.orderQty || row.OrderQty || 0)}</td>
                <td class="text-right">${formatDetailNumber(row.buyQty || row.BuyQty || 0)}</td>
                <td class="text-right">${formatDetailNumber(row.receiptQty || row.ReceiptQty || 0)}</td>
                <td class="text-right">${formatDetailNumber(row.issued || row.Issued || 0)}</td>
                <td class="vni-font">${buildEllipsisCell(row.note || row.Note || '')}</td>
            </tr>`;
        });

        $tbody.html(html.join(''));
    }

    function updateSearchDetailPagination(total, page, detailPageSize, totalPages) {
        const $pagination = $('#mrSearchDetailPagination');
        const $info = $('#mrSearchDetailPaginationInfo');

        if (!total) {
            $pagination.empty();
            $info.html('<small>No records</small>');
            return;
        }

        const safePageSize = detailPageSize > 0 ? detailPageSize : (searchDetailState.pageSize || 10);
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

    function formatDetailNumber(value) {
        const number = Number(value || 0);
        if (!Number.isFinite(number)) {
            return '0';
        }

        return number.toLocaleString('en-US', {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        });
    }

    // Sử dụng delegation để bắt sự kiện cho link được tạo động
    $(document).on('click', '.mr-request-link', function (e) {
        e.preventDefault();
        e.stopPropagation();

        const rowIndex = $(this).closest('tr').data('index');
        openDetailByRowIndex(rowIndex);
    });

    // Double click vào dòng cũng mở detail, nhưng vẫn check quyền giống click Request No.
    $(document).on('dblclick', '#mrTable tbody tr', function (e) {
        const $row = $(this);
        const rowIndex = $row.data('index');
        if (rowIndex === undefined || rowIndex === null) return;

        // Không xử lý double click trên dòng trạng thái No data / Loading...
        if ($row.find('td').length <= 1) return;

        const $radio = $row.find("input[name='selectedMr']");
        if ($radio.length > 0) {
            $radio.prop('checked', true).trigger('change');
        }

        e.preventDefault();
        openDetailByRowIndex(rowIndex);
    });

    // ========== BẮT SỰ KIỆN THAY ĐỔI RADIO (QUAN TRỌNG NHẤT) ==========
    $(document).on('change', "input[name='selectedMr']", function () {
        const index = $(this).val();
        const item = currentDataRows[index];

        if (!item) return;

        selectedRequestNo = item.data.requestNo || item.data.RequestNo;
        $('#mrTable tbody tr').removeClass('selected');
        $(this).closest('tr').addClass('selected');
        updateHistoryButtonState();
    });

    $(document).on('click focusin', '#mrTable tbody tr', function (e) {
        const $target = $(e.target);
        if ($target.is('a, input, button, label, select, option, textarea, .dropdown-toggle, .dropdown-menu, .custom-control-label')) {
            return;
        }

        const rowIndex = $(this).data('index');
        if (rowIndex === undefined || rowIndex === null) return;

        selectRowByIndex(rowIndex);
    });

    function resetActions() {
        selectedRequestNo = null;
        $('#mrTable tbody tr').removeClass('selected');
        $('input[name="selectedMr"]').prop('checked', false);
        updateHistoryButtonState();
    }

    // ========== PAGINATION (ĐÃ SỬA LỖI) ==========
    function updatePagination(total, page, pageSize, totalPages) {
        const $totalBadge = $('#total-records-badge');
        const $paginationInfo = $('#pagination-info');
        const $pagination = $('#pagination');

        $totalBadge.text(`${total} records`).addClass('badge-success');

        if (total === 0) {
            $paginationInfo.html('<small>No records</small>');
            $pagination.empty();
            return;
        }

        const start = ((page - 1) * pageSize) + 1;
        const end = Math.min(page * pageSize, total);
        $paginationInfo.html(`<small>Showing ${start}-${end} of ${total}</small>`);

        let html = '';
        html += `<li class="page-item ${page <= 1 ? 'disabled' : ''}">
                    <a class="page-link" href="#" data-page="${page - 1}">&laquo;</a>
                 </li>`;

        for (let i = 1; i <= totalPages; i++) {
            if (i === 1 || i === totalPages || (i >= page - 2 && i <= page + 2)) {
                html += `<li class="page-item ${i === page ? 'active' : ''}">
                            <a class="page-link" href="#" data-page="${i}">${i}</a>
                         </li>`;
            } else if (i === page - 3 || i === page + 3) {
                html += '<li class="page-item disabled"><span class="page-link">...</span></li>';
            }
        }

        html += `<li class="page-item ${page >= totalPages ? 'disabled' : ''}">
                    <a class="page-link" href="#" data-page="${page + 1}">&raquo;</a>
                 </li>`;

        $pagination.html(html).show();

        $pagination.find('a.page-link').click(function (e) {
            e.preventDefault();
            const p = $(this).data('page');
            if (p) performSearch(p);
        });
    }

    // ========== INITIALIZE ==========
    function initializePage() {
        console.log('Initializing page components...');

        const pageSizeSelect = document.getElementById('mrPageSize');
        const pageSizeInput = document.querySelector('#mrSearchForm input[name="Filter.PageSize"]');
        const urlPageSize = getQueryInt('Filter.PageSize');
        const hiddenPageSize = pageSizeSelect ? Number.parseInt(pageSizeSelect.value || '', 10) : 0;
        const defaultPageSizeValue = typeof defaultPageSize !== 'undefined' ? Number.parseInt(defaultPageSize, 10) : 0;
        const initialPageSize = urlPageSize || hiddenPageSize || defaultPageSizeValue;
        if (pageSizeSelect) {
            if (Number.isFinite(initialPageSize) && initialPageSize > 0) {
                pageSize = initialPageSize;
            }

            pageSizeSelect.value = String(pageSize);
            pageSizeSelect.addEventListener('change', () => {
                const nextPageSize = Number.parseInt(pageSizeSelect.value || '', 10);
                if (!Number.isFinite(nextPageSize) || nextPageSize <= 0 || nextPageSize === pageSize) {
                    return;
                }

                pageSize = nextPageSize;
                if (pageSizeInput) {
                    pageSizeInput.value = String(pageSize);
                }
                performSearch(1);
            });
        }

        if (pageSizeInput) {
            pageSizeInput.value = String(pageSize);
        }

        initConditionModeSwitcher();
        initStatusCheckboxDropdown();
        initializeSearchDateRange();
        initializeSearchDetailDateRange();
        initCreateMrPopup();
        initCreateAutoMrPopup();

        // 2. Đăng ký sự kiện nút bấm
        $('#btnAdd').off('click').on('click', function () {
            if (this.disabled) {
                return;
            }

            const requireFilterStoreGroup = (((this.dataset || {}).requireFilterStoreGroupForCreateMr) || '').toString() === 'true';
            const currentStoreGroup = ($('#Filter_StoreGroup').val() || '').toString().trim();
            if (requireFilterStoreGroup && !currentStoreGroup) {
                alert('Please choose Store Group in Filter before creating MR.');
                $('#Filter_StoreGroup').trigger('focus');
                return;
            }

            $('#createMrModal').modal({ backdrop: 'static', keyboard: false, show: true });
        });

        $('#btnCreateAuto').off('click').on('click', function () {
            $('#createAutoMrModal').modal({ backdrop: 'static', keyboard: false, show: true });
        });

        $('#btnRefresh').off('click').on('click', function () {
            performSearch(1);
        });

        $('#btnViewHistory').off('click').on('click', function () {
            if (!selectedRequestNo) {
                alert('Please select a request first.');
                return;
            }

            loadHistory(selectedRequestNo);
        });

        $('#btnSearchDetail').off('click').on('click', function () {
            resetSearchDetailCriteria();
            $('#mrSearchDetailModal').modal({ backdrop: 'static', keyboard: false, show: true });
            performSearchDetail(1);
        });

        $('#btnMrSearchDetailSearch').off('click').on('click', function () {
            performSearchDetail(1);
        });

        $(document)
            .off('keydown', '#MrSearchDetail_RequestNo, #MrSearchDetail_ItemCode, #MrSearchDetail_ItemName')
            .on('keydown', '#MrSearchDetail_RequestNo, #MrSearchDetail_ItemCode, #MrSearchDetail_ItemName', function (e) {
                if (e.key !== 'Enter') {
                    return;
                }

                e.preventDefault();
                performSearchDetail(1);
            });

        $('#MrSearchDetail_UseDateRange').off('change').on('change', function () {
            syncSearchDetailDateRangeState();
        });

        $('#mrSearchDetailPageSize').off('change').on('change', function () {
            const nextPageSize = Number.parseInt($(this).val() || '', 10);
            if (!Number.isFinite(nextPageSize) || nextPageSize <= 0 || nextPageSize === searchDetailState.pageSize) {
                return;
            }

            searchDetailState.pageSize = nextPageSize;
            performSearchDetail(1);
        });

        $(document).off('click', '#mrSearchDetailPagination a.page-link').on('click', '#mrSearchDetailPagination a.page-link', function (e) {
            e.preventDefault();
            const page = Number.parseInt($(this).data('page'), 10);
            if (Number.isFinite(page) && page > 0 && page !== searchDetailState.currentPage) {
                performSearchDetail(page);
            }
        });

        $('#btnClose').off('click').on('click', function () {
            window.location.href = '/Index';
        });

        // 3. Xử lý Form Search
        $('#mrSearchForm').off('submit').on('submit', function (e) {
            e.preventDefault();
            performSearch(1);
        });

        // 4. Tu dong search lan dau khi da khoi tao xong date range
        const initialPage = getQueryInt('Filter.PageIndex')
            || parseInt($('#mrSearchForm input[name="Filter.PageIndex"]').val() || '1', 10);
        performSearch(Number.isFinite(initialPage) && initialPage > 0 ? initialPage : 1);
        updateHistoryButtonState();
    }

    window.addEventListener('pageshow', function (event) {
        if (event.persisted) {
            initializePage();
        }
    });

    function showLoading(show) {
        if (show) $('#mrTable tbody').html('<tr><td colspan="8" class="text-center py-4"><div class="spinner-border spinner-border-sm"></div> Loading...</td></tr>');
    }

    function showError(m) {
        $('#mrTable tbody').html(`<tr><td colspan="8" class="text-center text-danger py-4">${m}</td></tr>`);
    }

    $(document).ready(initializePage);



    function initStatusCheckboxDropdown() {
        const $dropdownBtn = $('#mrStatusDropdownBtn');
        const $menu = $('.mr-status-menu');
        const $checkboxes = $('.mr-status-checkbox');
        const $selectAllBtn = $('#mrStatusSelectAllBtn');
        const $clearBtn = $('#mrStatusClearBtn');
        if ($dropdownBtn.length === 0 || $menu.length === 0 || $checkboxes.length === 0) return;

        function updateCaption() {
            const selected = $checkboxes
                .filter(':checked')
                .map(function () { return ($(this).data('label') || '').toString(); })
                .get()
                .filter((x) => x.length > 0);

            const fullCaption = selected.join('; ');
            $dropdownBtn.attr('title', fullCaption || 'All status');

            if (selected.length === 0) {
                $dropdownBtn.text('All status');
                return;
            }

            if (selected.length === 1) {
                $dropdownBtn.text(selected[0]);
                return;
            }

            $dropdownBtn.text(`${selected.length} statuses selected`);
        }

        $menu.off('click').on('click', function (event) {
            event.stopPropagation();
        });

        $checkboxes.off('change').on('change', updateCaption);
        $selectAllBtn.off('click').on('click', function () {
            $checkboxes.prop('checked', true);
            updateCaption();
        });

        $clearBtn.off('click').on('click', function () {
            $checkboxes.prop('checked', false);
            updateCaption();
        });

        updateCaption();
    }

    function initConditionModeSwitcher() {
        const $radios = $('input[name="Filter.ConditionMode"]');
        const $groups = $('[data-condition-group]');
        if ($radios.length === 0 || $groups.length === 0) return;

        function applyMode() {
            const selectedMode = $radios.filter(':checked').val() || 'allUsers';

            $groups.each(function () {
                const groupMode = (this.getAttribute('data-condition-group') || '').toString();
                const enabled = groupMode === selectedMode;
                this.classList.toggle('mr-condition-group-disabled', !enabled);

                const controls = this.querySelectorAll('input, select, textarea, button');
                controls.forEach(function (ctrl) {
                    if (ctrl.name === 'Filter.ConditionMode') return;
                    if (ctrl.name === 'Filter.ItemCode') return;
                    ctrl.disabled = !enabled;
                });
            });
        }

        $radios.off('change').on('change', applyMode);
        applyMode();
    }

    function initCreateMrPopup() {
        const $modal = $('#createMrModal');
        if ($modal.length === 0) return;

        const $searchBtn = $('#createMrSearchBtn');
        const $moveRightBtn = $('#createMrMoveRightBtn');
        const $moveLeftBtn = $('#createMrMoveLeftBtn');
        const $confirmBtn = $('#createMrConfirmBtn');
        const $keywordInput = $('#createMrKeyword');
        const $checkBalanceInput = $('#createMrCheckBalance');
        const $searchBody = $('#createMrSearchResultBody');
        const $selectedBody = $('#createMrSelectedBody');
        const $descriptionInput = $('#createMrDescription');
        const $descriptionPostInput = $('#createMrDescriptionInput');
        const $linesJsonInput = $('#createMrLinesJsonInput');
        const $storeGroupPostInput = $('#createMrStoreGroupInput');
        const $validation = $('#createMrValidation');
        const $createMrButton = $('#btnAdd');
        const requireFilterStoreGroup = (($createMrButton.data('require-filter-store-group-for-create-mr') || false).toString() === 'true');

        const selectedMap = new Map();

        function getCurrentStoreGroupValue() {
            const raw = ($('#Filter_StoreGroup').val() || '').toString().trim();
            if (!raw) return null;
            const parsed = Number.parseInt(raw, 10);
            return Number.isFinite(parsed) ? parsed : null;
        }

        function showValidation(message) {
            $validation.text(message || '');
            $validation.toggleClass('d-none', !message);
        }

        function syncConfirmState() {
            const hasItems = selectedMap.size > 0;
            const hasDescription = (($descriptionInput.val() || '').toString().trim().length > 0);
            $confirmBtn.prop('disabled', !(hasItems && hasDescription));
        }

        function readOrderQtyFromInput(input) {
            const raw = $(input).val();
            const value = Number.parseFloat((raw || '').toString().trim());
            return Number.isFinite(value) && value > 0 ? value : 1;
        }

        function collectRowItem(tr) {
            if (!tr || !tr.dataset.itemCode) return null;
            return {
                itemCode: tr.dataset.itemCode || '',
                itemName: tr.dataset.itemName || '',
                unit: tr.dataset.unit || '',
                inStock: Number.parseFloat(tr.dataset.inStock || '0') || 0,
                normQty: Number.parseFloat(tr.dataset.normQty || '0') || 0,
                normMain: Number.parseFloat(tr.dataset.normMain || '0') || 0,
                orderQty: readOrderQtyFromInput(tr.querySelector('.create-mr-order-input'))
            };
        }

        function redrawSelected() {
            renderSelectedRows($selectedBody.get(0), Array.from(selectedMap.values()));
            syncConfirmState();
        }

        function getEffectiveCreateMrStoreGroup(item) {
            const currentStoreGroup = getCurrentStoreGroupValue();
            if (Number.isFinite(currentStoreGroup) && currentStoreGroup > 0) {
                return currentStoreGroup;
            }
            return null;
        }

        async function shouldAddCreateMrItem(rowItem) {
            const effectiveStoreGroup = getEffectiveCreateMrStoreGroup(rowItem);
            if (!rowItem || !rowItem.itemCode || !effectiveStoreGroup) {
                return true;
            }

            try {
                const warning = await checkPendingIssueWarning(rowItem.itemCode, effectiveStoreGroup);
                if (!warning || warning.hasWarning !== true) {
                    return true;
                }

                return window.confirm(
                    `Warning! This item was ordered by Meterial request No:${warning.requestNos} . Sum Quantity: ${formatLookupNumber(warning.quantityNotIssued)} .Please check these Requests carefully before making the order. Do you want to order this item anyway`
                );
            } catch (error) {
                console.error(error);
                return true;
            }
        }

        $modal.off('shown.bs.modal').on('shown.bs.modal', async function () {
            selectedMap.clear();
            redrawSelected();
            showValidation('');
            $descriptionInput.val('');
            $keywordInput.val('');

            const storeGroupValue = getCurrentStoreGroupValue();
            $storeGroupPostInput.val(storeGroupValue === null ? '' : String(storeGroupValue));
            syncConfirmState();
            renderSearchRows($searchBody.get(0), [], 'Enter item code or item name, then click Search.');
        });

        $searchBtn.off('click').on('click', async function () {
            showValidation('');
            const keyword = ($keywordInput.val() || '').toString().trim();

            if (keyword.length < 3) {
                showValidation('Enter at least 3 characters for Item Code or Item Name.');
                renderSearchRows($searchBody.get(0), [], 'Enter item code or item name, then click Search.');
                return;
            }

            try {
                const items = await searchItems(keyword, $checkBalanceInput.is(':checked'));
                renderSearchRows($searchBody.get(0), items);
            } catch (err) {
                console.error(err);
                showValidation('Cannot load item list.');
                renderSearchRows($searchBody.get(0), [], 'Enter item code or item name, then click Search.');
            }
        });

        $keywordInput.off('keydown').on('keydown', function (event) {
            if (event.key === 'Enter') {
                event.preventDefault();
                $searchBtn.trigger('click');
            }
        });

        $searchBody.off('click', 'tr').on('click', 'tr', function (event) {
            const tr = event.currentTarget;
            if (!tr || !tr.dataset.itemCode) return;
            const checkbox = tr.querySelector('.create-mr-search-checkbox');
            if (checkbox && !event.target.closest('input')) checkbox.checked = !checkbox.checked;
        });

        $searchBody.off('input', '.create-mr-order-input').on('input', '.create-mr-order-input', function () {
            const tr = $(this).closest('tr').get(0);
            if (!tr || !tr.dataset.itemCode) return;
            tr.dataset.orderQty = String(readOrderQtyFromInput(this));
        });

        $searchBody.off('dblclick', 'tr').on('dblclick', 'tr', async function (event) {
            const tr = event.currentTarget;
            const rowItem = collectRowItem(tr);
            if (!rowItem) return;

            const shouldAdd = await shouldAddCreateMrItem(rowItem);
            if (!shouldAdd) {
                return;
            }

            selectedMap.set(rowItem.itemCode, rowItem);

            showValidation('');
            redrawSelected();
        });

        $moveRightBtn.off('click').on('click', async function () {
            const checkedRows = Array.from($searchBody.get(0).querySelectorAll('.create-mr-search-checkbox:checked'))
                .map(function (x) { return x.closest('tr'); })
                .filter(function (x) { return !!x; });

            if (checkedRows.length === 0) {
                showValidation('Please choose item(s) from search result.');
                return;
            }

            for (const tr of checkedRows) {
                const rowItem = collectRowItem(tr);
                if (!rowItem) continue;

                const shouldAdd = await shouldAddCreateMrItem(rowItem);
                if (!shouldAdd) {
                    continue;
                }

                selectedMap.set(rowItem.itemCode, rowItem);
            }

            showValidation('');
            redrawSelected();
        });

        $moveLeftBtn.off('click').on('click', function () {
            const selectedChecks = $selectedBody.get(0).querySelectorAll('.create-mr-selected-checkbox:checked');
            selectedChecks.forEach(function (check) {
                const tr = check.closest('tr');
                const itemCode = tr?.dataset.itemCode || '';
                if (itemCode) selectedMap.delete(itemCode);
            });
            redrawSelected();
        });

        $selectedBody.off('dblclick', 'tr').on('dblclick', 'tr', function (event) {
            const tr = event.currentTarget;
            const itemCode = tr?.dataset.itemCode || '';
            if (!itemCode) return;
            selectedMap.delete(itemCode);
            redrawSelected();
        });

        $selectedBody.off('input', '.create-mr-order-input').on('input', '.create-mr-order-input', function () {
            const tr = $(this).closest('tr').get(0);
            if (!tr || !tr.dataset.itemCode) return;
            const itemCode = tr.dataset.itemCode || '';
            const existing = selectedMap.get(itemCode);
            if (!existing) return;
            existing.orderQty = readOrderQtyFromInput(this);
            tr.dataset.orderQty = String(existing.orderQty);
            selectedMap.set(itemCode, existing);
        });

        $descriptionInput.off('input').on('input', function () {
            syncConfirmState();
        });

        $confirmBtn.off('click').on('click', function (event) {
            const selectedItems = Array.from(selectedMap.values());
            const description = ($descriptionInput.val() || '').toString().trim();

            if (selectedItems.length === 0) {
                event.preventDefault();
                showValidation('Please select at least one item.');
                return;
            }

            if (!description) {
                event.preventDefault();
                showValidation('Description is required.');
                $descriptionInput.trigger('focus');
                return;
            }

            $descriptionPostInput.val(description);
            $linesJsonInput.val(JSON.stringify(selectedItems));

            const storeGroupValue = getCurrentStoreGroupValue();
            if (storeGroupValue !== null) {
                $storeGroupPostInput.val(String(storeGroupValue));
            } else {
                if (requireFilterStoreGroup) {
                    event.preventDefault();
                    showValidation('Please choose Store Group in Filter before creating MR.');
                    return;
                }
                $storeGroupPostInput.val('');
            }

            showValidation('');
            syncConfirmState();
        });
    }

    function initCreateAutoMrPopup() {
        const $modal = $('#createAutoMrModal');
        if ($modal.length === 0) return;

        const $storeGroupSelect = $('#createAutoStoreGroup');
        const $filterStoreGroup = $('#Filter_StoreGroup');
        const $descInput = $('#createAutoDescription');
        const $form = $('#createAutoMrForm');
        const $dateRange = $('#CreateAutoDateRange');
        const $dateCreate = $('input[name="CreateAutoDateCreate"]');
        const $fromDate = $('input[name="CreateAutoFromDate"]');
        const $toDate = $('input[name="CreateAutoToDate"]');

        if ($dateRange.length > 0) {
            window.initSimpleDateRange('CreateAutoDateRange', 'input[name="CreateAutoFromDate"]', 'input[name="CreateAutoToDate"]', {
                linkedCalendars: false
            });
        }

        function setDefaults() {
            if ($filterStoreGroup.length > 0 && $storeGroupSelect.length > 0) {
                $storeGroupSelect.val($filterStoreGroup.val());
            }

            if (($descInput.val() || '').toString().trim() === '') {
                const now = new Date();
                const month = String(now.getMonth() + 1).padStart(2, '0');
                $descInput.val(`Auto MR ${month}/${now.getFullYear()}`);
            }

            if ($dateCreate.length > 0 && !$dateCreate.val()) {
                const now = new Date();
                $dateCreate.val(now.toISOString().slice(0, 10));
            }

            if ($fromDate.length > 0 && !$fromDate.val()) {
                const now = new Date();
                now.setMonth(now.getMonth() - 3);
                $fromDate.val(now.toISOString().slice(0, 10));
            }

            if ($toDate.length > 0 && !$toDate.val()) {
                const now = new Date();
                $toDate.val(now.toISOString().slice(0, 10));
            }

            if ($dateRange.length > 0) {
                const fromValue = ($fromDate.val() || '').toString().trim();
                const toValue = ($toDate.val() || '').toString().trim();
                if (fromValue && toValue) {
                    window.setDateRangeValue('CreateAutoDateRange', fromValue, toValue);
                }
            }
        }

        $modal.off('shown.bs.modal').on('shown.bs.modal', function () {
            setDefaults();
        });

        $form.off('submit').on('submit', function (e) {
            const raw = ($storeGroupSelect.val() || '').toString().trim();
            if (!raw) {
                e.preventDefault();
                alert('Please choose Store Group before creating Auto MR.');
                $storeGroupSelect.trigger('focus');
            }
        });
    }

    function searchItems(keyword, checkBalanceInStore, storeGroupId) {
        return new Promise(function (resolve, reject) {
            const url = new URL(window.location.href);
            url.searchParams.set('handler', 'SearchItems');
            if (keyword) url.searchParams.set('keyword', keyword);
            if (checkBalanceInStore) url.searchParams.set('checkBalanceInStore', 'true');
            const parsedStoreGroupId = Number.parseInt((storeGroupId || '').toString().trim(), 10);
            if (Number.isFinite(parsedStoreGroupId) && parsedStoreGroupId > 0) {
                url.searchParams.set('storeGroupId', String(parsedStoreGroupId));
            }

            $.ajax({
                url: url.toString(),
                type: 'GET',
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                success: function (json) {
                    if (json && json.success) {
                        resolve(json.data || []);
                    } else {
                        reject(new Error((json && json.message) ? json.message : 'Cannot load items.'));
                    }
                },
                error: function () {
                    reject(new Error('Cannot load items.'));
                }
            });
        });
    }

    function checkPendingIssueWarning(itemCode, storeGroupId) {
        return new Promise(function (resolve, reject) {
            const url = new URL(window.location.href);
            url.searchParams.set('handler', 'PendingIssueWarning');
            url.searchParams.set('itemCode', itemCode || '');
            url.searchParams.set('storeGroup', storeGroupId || '');

            $.ajax({
                url: url.toString(),
                type: 'GET',
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                success: function (json) {
                    if (json && json.success) {
                        resolve(json.data || { hasWarning: false });
                    } else {
                        reject(new Error((json && json.message) ? json.message : 'Cannot check pending issue warning.'));
                    }
                },
                error: function () {
                    reject(new Error('Cannot check pending issue warning.'));
                }
            });
        });
    }

    function renderSearchRows(body, items, emptyMessage) {
        body.innerHTML = '';
        if (!items || items.length === 0) {
            body.innerHTML = `<tr><td colspan="6" class="text-center text-muted">${emptyMessage || 'No data'}</td></tr>`;
            return;
        }

        items.forEach(function (item) {
            const tr = document.createElement('tr');
            tr.dataset.itemCode = item.itemCode || '';
            tr.dataset.itemName = item.itemName || '';
            tr.dataset.unit = item.unit || '';
            tr.dataset.inStock = item.inStock || 0;
            tr.dataset.normQty = item.normQty || 0;
            tr.dataset.normMain = item.normMain || 0;
            tr.dataset.orderQty = item.orderQty || 1;
            tr.innerHTML = `
                <td><input type="checkbox" class="create-mr-search-checkbox"></td>
                <td>${item.itemCode || ''}</td>
                <td class="tcvn3-font">${item.itemName || ''}</td>
                <td>${item.unit || ''}</td>`;
            const orderCell = document.createElement('td');
            orderCell.textContent = Number.isFinite(Number(item.inStock)) ? String(item.inStock || 0) : '0';
            tr.appendChild(orderCell);

            const orderInputCell = document.createElement('td');
            orderInputCell.innerHTML = `<input type="number" min="0.01" step="0.01" class="form-control form-control-sm create-mr-order-input" value="${item.orderQty && item.orderQty > 0 ? item.orderQty : 1}">`;
            tr.appendChild(orderInputCell);
            body.appendChild(tr);
        });
    }

    function renderSelectedRows(body, selectedItems) {
        body.innerHTML = '';
        if (!selectedItems || selectedItems.length === 0) {
            body.innerHTML = '<tr><td colspan="5" class="text-center text-muted">No selected item</td></tr>';
            return;
        }

        selectedItems.forEach(function (item) {
            const tr = document.createElement('tr');
            tr.dataset.itemCode = item.itemCode;
            tr.dataset.orderQty = item.orderQty || 1;
            tr.innerHTML = `
                <td><input type="checkbox" class="create-mr-selected-checkbox"></td>
                <td>${item.itemCode || ''}</td>
                <td class="tcvn3-font">${item.itemName || ''}</td>
                <td>${item.unit || ''}</td>`;
            const orderCell = document.createElement('td');
            orderCell.innerHTML = `<input type="number" min="0.01" step="0.01" class="form-control form-control-sm create-mr-order-input" value="${item.orderQty && item.orderQty > 0 ? item.orderQty : 1}">`;
            tr.appendChild(orderCell);
            body.appendChild(tr);
        });
    }
})();





