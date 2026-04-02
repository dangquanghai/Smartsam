(function () {
    'use strict';

    let pageSize = typeof defaultPageSize !== 'undefined' ? defaultPageSize : 10;
    let selectedRequestNo = null;
    let currentPage = 1;
    let currentDataRows = [];

    function buildCurrentReturnUrl() {
        return `${window.location.pathname}${window.location.search || ''}`;
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
                <td class="vni-font" style="white-space:nowrap">${r.accordingTo || r.AccordingTo || ''}</td>
                <td class="vni-font" style="white-space:nowrap">${r.kpGroupName || r.KPGroupName || r.kPGroupName || ''}</td>
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

        const pageSizeRaw = ($('#mrSearchForm input[name="Filter.PageSize"]').val() || '').toString().trim();
        const parsedPageSize = Number.parseInt(pageSizeRaw, 10);
        if (Number.isFinite(parsedPageSize) && parsedPageSize > 0) {
            pageSize = parsedPageSize;
        }

        initConditionModeSwitcher();
        initStatusCheckboxDropdown();
        initializeSearchDateRange();
        initCreateMrPopup();
        initCreateAutoMrPopup();

        // 2. Đăng ký sự kiện nút bấm
        $('#btnAdd').off('click').on('click', function () {
            $('#createMrModal').modal({ backdrop: 'static', keyboard: false, show: true });
        });

        $('#btnCreateAuto').off('click').on('click', function () {
            $('#createAutoMrModal').modal({ backdrop: 'static', keyboard: false, show: true });
        });

        $('#btnRefresh').off('click').on('click', function () {
            performSearch(1);
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
        const initialPage = parseInt($('#mrSearchForm input[name="Filter.PageIndex"]').val() || '1', 10);
        performSearch(Number.isFinite(initialPage) && initialPage > 0 ? initialPage : 1);
    }

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
                storeGroupId: Number.parseInt(tr.dataset.storeGroupId || '0', 10) || 0,
                orderQty: readOrderQtyFromInput(tr.querySelector('.create-mr-order-input'))
            };
        }

        function redrawSelected() {
            renderSelectedRows($selectedBody.get(0), Array.from(selectedMap.values()));
            syncConfirmState();
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

        $searchBody.off('dblclick', 'tr').on('dblclick', 'tr', function (event) {
            const tr = event.currentTarget;
            const rowItem = collectRowItem(tr);
            if (!rowItem) return;

            selectedMap.set(rowItem.itemCode, rowItem);

            showValidation('');
            redrawSelected();
        });

        $moveRightBtn.off('click').on('click', function () {
            const checkedRows = Array.from($searchBody.get(0).querySelectorAll('.create-mr-search-checkbox:checked'))
                .map(function (x) { return x.closest('tr'); })
                .filter(function (x) { return !!x; });

            if (checkedRows.length === 0) {
                showValidation('Please choose item(s) from search result.');
                return;
            }

            checkedRows.forEach(function (tr) {
                const rowItem = collectRowItem(tr);
                if (!rowItem) return;

                selectedMap.set(rowItem.itemCode, rowItem);
            });

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
                const itemGroups = Array.from(new Set(
                selectedItems
                        .map(function (x) { return Number.parseInt(x.storeGroupId || 0, 10); })
                        .filter(function (x) { return Number.isFinite(x) && x > 0; })
                ));

                if (itemGroups.length === 1) {
                    $storeGroupPostInput.val(String(itemGroups[0]));
                } else {
                    event.preventDefault();
                    showValidation('Please choose Store Group before creating MR.');
                    return;
                }
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

    function searchItems(keyword, checkBalanceInStore) {
        return new Promise(function (resolve, reject) {
            const url = new URL(window.location.href);
            url.searchParams.set('handler', 'SearchItems');
            if (keyword) url.searchParams.set('keyword', keyword);
            if (checkBalanceInStore) url.searchParams.set('checkBalanceInStore', 'true');

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
            tr.dataset.storeGroupId = item.storeGroupId || 0;
            tr.dataset.orderQty = item.orderQty || 1;
            tr.innerHTML = `
                <td><input type="checkbox" class="create-mr-search-checkbox"></td>
                <td>${item.itemCode || ''}</td>
                <td class="vni-font">${item.itemName || ''}</td>
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
                <td class="vni-font">${item.itemName || ''}</td>
                <td>${item.unit || ''}</td>`;
            const orderCell = document.createElement('td');
            orderCell.innerHTML = `<input type="number" min="0.01" step="0.01" class="form-control form-control-sm create-mr-order-input" value="${item.orderQty && item.orderQty > 0 ? item.orderQty : 1}">`;
            tr.appendChild(orderCell);
            body.appendChild(tr);
        });
    }
})();





