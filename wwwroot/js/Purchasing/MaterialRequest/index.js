(function () {
    'use strict';

    let pageSize = Number.parseInt(typeof defaultPageSize !== 'undefined' ? defaultPageSize : 10, 10);
    if (!Number.isFinite(pageSize) || pageSize <= 0) pageSize = 10;

    let currentPage = 1;
    let currentDataRows = [];
    let selectedRequestNo = null;

    // 1) Gọi API search và nạp dữ liệu cho lưới.
    function performSearch(page = 1) {
        currentPage = page;

        const token = getRequestVerificationToken();
        const filter = buildSearchFilter(page);

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
                    renderRequests(currentDataRows);
                    updatePagination(
                        response.total || 0,
                        response.page || 1,
                        response.pageSize || pageSize,
                        response.totalPages || 1
                    );
                    resetActions();
                } else {
                    showError((response && response.message) ? response.message : 'Search failed.');
                }
            },
            error: function () {
                showError('Search failed.');
            },
            complete: function () {
                showLoading(false);
            }
        });
    }

    // 2) Render danh sách request ra bảng.
    function renderRequests(items) {
        const $tbody = $('#mrTable tbody');
        $tbody.empty();

        if (!items || items.length === 0) {
            $tbody.append('<tr class="mr-grid-state"><td colspan="7" class="text-center text-muted">No data</td></tr>');
            return;
        }

        items.forEach(function (item, index) {
            const r = item.data || {};
            const requestNo = r.requestNo ?? r.RequestNo;
            const dateCreate = r.dateCreate ? formatDateTime(r.dateCreate) : (r.DateCreate ? formatDateTime(r.DateCreate) : '');
            const accordingTo = r.accordingTo ?? r.AccordingTo ?? '';
            const groupName = r.kPGroupName ?? r.KPGroupName ?? '';
            const statusName = r.materialStatusName ?? r.MaterialStatusName ?? (r.materialStatusId ?? r.MaterialStatusId ?? '');
            const prNo = r.prNo ?? r.PrNo ?? '';

            const rowHtml = `
                <tr class="mr-row" data-index="${index}" data-request-no="${requestNo}">
                    <td><input type="radio" name="selectedMr" value="${index}"></td>
                    <td class="mr-col-request-no"><a href="javascript:void(0)" class="mr-request-link text-primary font-weight-bold" style="text-decoration: underline;">${escapeHtml(String(requestNo || ''))}</a></td>
                    <td>${escapeHtml(dateCreate)}</td>
                    <td title="${escapeHtml(accordingTo)}">${escapeHtml(truncateText(accordingTo, 60))}</td>
                    <td title="${escapeHtml(groupName)}">${escapeHtml(truncateText(groupName, 30))}</td>
                    <td>${escapeHtml(String(statusName))}</td>
                    <td>${escapeHtml(String(prNo || ''))}</td>
                </tr>`;

            $tbody.append(rowHtml);
        });
    }

    // 3) Đăng ký sự kiện cho link, double click, radio và phân trang.
    function registerGridEvents() {
        $(document).off('click.mrRequest').on('click.mrRequest', '.mr-request-link', function (e) {
            e.preventDefault();
            const index = Number.parseInt($(this).closest('tr').data('index'), 10);
            if (Number.isNaN(index)) return;
            goToDetail(index);
        });

        $(document).off('dblclick.mrRow').on('dblclick.mrRow', '#mrTable tbody tr.mr-row', function () {
            const index = Number.parseInt($(this).data('index'), 10);
            if (Number.isNaN(index)) return;
            goToDetail(index);
        });

        $(document).off('change.mrRadio').on('change.mrRadio', 'input[name="selectedMr"]', function () {
            const index = Number.parseInt($(this).val(), 10);
            const item = currentDataRows[index];
            if (!item || !item.data) return;

            selectedRequestNo = item.data.requestNo ?? item.data.RequestNo;
            $('#mrTable tbody tr').removeClass('selected');
            $(this).closest('tr').addClass('selected');
        });

        $(document).off('click.mrPaging').on('click.mrPaging', '#pagination a.page-link', function (e) {
            e.preventDefault();
            const p = Number.parseInt($(this).data('page'), 10);
            if (!Number.isFinite(p)) return;
            if ($(this).closest('.page-item').hasClass('disabled')) return;
            if ($(this).closest('.page-item').hasClass('active')) return;
            performSearch(p);
        });
    }

    // 4) Reset trạng thái dòng đang chọn.
    function resetActions() {
        selectedRequestNo = null;
        $('#mrTable tbody tr').removeClass('selected');
        $('input[name="selectedMr"]').prop('checked', false);
    }

    // 5) Render phân trang.
    function updatePagination(total, page, size, totalPages) {
        const $badge = $('#total-records-badge');
        const $info = $('#pagination-info');
        const $pagination = $('#pagination');

        $badge.text(`${total} records`);

        if (total === 0) {
            $info.text('Showing 0 to 0 of 0 entries');
            $pagination.empty();
            return;
        }

        const start = ((page - 1) * size) + 1;
        const end = Math.min(page * size, total);
        $info.text(`Showing ${start} to ${end} of ${total} entries`);

        let html = '';
        html += `<li class="page-item ${page <= 1 ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${page - 1}">Prev</a></li>`;

        for (let i = 1; i <= totalPages; i++) {
            if (i === 1 || i === totalPages || (i >= page - 2 && i <= page + 2)) {
                html += `<li class="page-item ${i === page ? 'active' : ''}"><a class="page-link" href="#" data-page="${i}">${i}</a></li>`;
            } else if (i === page - 3 || i === page + 3) {
                html += '<li class="page-item disabled"><span class="page-link">...</span></li>';
            }
        }

        html += `<li class="page-item ${page >= totalPages ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${page + 1}">Next</a></li>`;
        $pagination.html(html);
    }

    // 6) Khởi tạo trang theo đúng thứ tự chuẩn.
    function initializePage() {
        initConditionModeSwitcher();
        initStatusCheckboxDropdown();
        initCreateMrPopup();
        initCreateAutoMrPopup();

        registerGridEvents();

        $('#mrSearchForm').off('submit').on('submit', function (e) {
            e.preventDefault();
            performSearch(1);
        });

        performSearch(1);
    }

    // 7) Hiển thị trạng thái loading/error cho lưới.
    function showLoading(show) {
        if (!show) return;
        $('#mrTable tbody').html('<tr class="mr-grid-state"><td colspan="7" class="text-center text-muted py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</td></tr>');
    }

    function showError(message) {
        $('#mrTable tbody').html(`<tr class="mr-grid-state"><td colspan="7" class="text-center text-danger py-3">${escapeHtml(message)}</td></tr>`);
    }

    // 8) Điểm vào chính của trang.
    $(document).ready(initializePage);

    // ======================== HÀM PHỤ TRỢ ========================

    function getRequestVerificationToken() {
        return ($('input[name="__RequestVerificationToken"]').first().val() || '').toString();
    }

    function buildSearchFilter(page) {
        const statusIds = $('.mr-status-checkbox:checked')
            .map(function () { return Number.parseInt(this.value, 10); })
            .get()
            .filter((x) => Number.isFinite(x));

        const requestNoRaw = ($('#RequestNo').val() || '').toString().trim();
        const requestNo = requestNoRaw === '' ? null : Number(requestNoRaw);

        const storeGroupRaw = ($('#StoreGroup').val() || '').toString().trim();
        const storeGroup = storeGroupRaw === '' ? null : Number(storeGroupRaw);

        const fromDateRaw = ($('#FromDate').val() || '').toString().trim();
        const toDateRaw = ($('#ToDate').val() || '').toString().trim();

        return {
            requestNo: Number.isFinite(requestNo) ? requestNo : null,
            storeGroup: Number.isFinite(storeGroup) ? storeGroup : null,
            statusIds: statusIds,
            itemCode: ($('#ItemCode').val() || '').toString().trim() || null,
            issueLessThanOrder: $('#IssueLessThanOrder').is(':checked'),
            buyGreaterThanZero: $('#BuyGreaterThanZero').is(':checked'),
            autoOnly: $('#AutoOnly').is(':checked'),
            fromDate: fromDateRaw || null,
            toDate: toDateRaw || null,
            accordingToKeyword: ($('#AccordingToKeyword').val() || '').toString().trim() || null,
            conditionMode: $('input[name="ConditionMode"]:checked').val() || 'allUsers',
            page: page,
            pageSize: pageSize
        };
    }

    function goToDetail(index) {
        const item = currentDataRows[index];
        if (!item || !item.data || !item.actions || !item.actions.canAccess) return;

        const requestNo = item.data.requestNo ?? item.data.RequestNo;
        const mode = item.actions.accessMode || 'view';
        const returnUrl = window.location.pathname + window.location.search;

        window.location.href = `/Purchasing/MaterialRequest/MaterialRequestDetail?id=${requestNo}&mode=${mode}&returnUrl=${encodeURIComponent(returnUrl)}`;
    }

    function initStatusCheckboxDropdown() {
        const $dropdownBtn = $('#mrStatusDropdownBtn');
        const $menu = $('.mr-status-menu');
        const $checkboxes = $('.mr-status-checkbox');
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
        updateCaption();
    }

    function initConditionModeSwitcher() {
        const $radios = $('input[name="ConditionMode"]');
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
                    if (ctrl.name === 'ConditionMode') return;
                    ctrl.disabled = !enabled;
                });
            });
        }

        $radios.off('change').on('change', applyMode);
        applyMode();
    }

    function initCreateMrPopup() {
        const $openBtn = $('#openCreateMrPopupBtn');
        const $modal = $('#createMrModal');
        if ($openBtn.length === 0 || $modal.length === 0) return;

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
            const raw = ($('#StoreGroup').val() || '').toString().trim();
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

        function redrawSelected() {
            renderSelectedRows($selectedBody.get(0), Array.from(selectedMap.values()));
            syncConfirmState();
        }

        $openBtn.off('click').on('click', async function () {
            $modal.modal({ backdrop: 'static', keyboard: false, show: true });

            selectedMap.clear();
            redrawSelected();
            showValidation('');
            $descriptionInput.val('');

            const storeGroupValue = getCurrentStoreGroupValue();
            $storeGroupPostInput.val(storeGroupValue === null ? '' : String(storeGroupValue));
            syncConfirmState();

            try {
                const items = await searchItems('', $checkBalanceInput.is(':checked'), storeGroupValue);
                renderSearchRows($searchBody.get(0), items);
            } catch (err) {
                console.error(err);
                renderSearchRows($searchBody.get(0), []);
            }
        });

        $searchBtn.off('click').on('click', async function () {
            showValidation('');
            try {
                const items = await searchItems(
                    ($keywordInput.val() || '').toString().trim(),
                    $checkBalanceInput.is(':checked'),
                    getCurrentStoreGroupValue()
                );
                renderSearchRows($searchBody.get(0), items);
            } catch (err) {
                console.error(err);
                showValidation('Cannot load item list.');
                renderSearchRows($searchBody.get(0), []);
            }
        });

        $searchBody.off('click', 'tr').on('click', 'tr', function (event) {
            const tr = event.currentTarget;
            if (!tr || !tr.dataset.itemCode) return;
            const checkbox = tr.querySelector('.create-mr-search-checkbox');
            if (checkbox && !event.target.closest('input')) checkbox.checked = !checkbox.checked;
        });

        $searchBody.off('dblclick', 'tr').on('dblclick', 'tr', function (event) {
            const tr = event.currentTarget;
            if (!tr || !tr.dataset.itemCode) return;
            const itemCode = tr.dataset.itemCode || '';

            selectedMap.set(itemCode, {
                itemCode: itemCode,
                itemName: tr.dataset.itemName || '',
                unit: tr.dataset.unit || '',
                inStock: Number.parseFloat(tr.dataset.inStock || '0') || 0,
                storeGroupId: Number.parseInt(tr.dataset.storeGroupId || '0', 10) || 0
            });

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
                const itemCode = tr.dataset.itemCode || '';
                if (!itemCode) return;

                selectedMap.set(itemCode, {
                    itemCode: itemCode,
                    itemName: tr.dataset.itemName || '',
                    unit: tr.dataset.unit || '',
                    inStock: Number.parseFloat(tr.dataset.inStock || '0') || 0,
                    storeGroupId: Number.parseInt(tr.dataset.storeGroupId || '0', 10) || 0
                });
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
        const $openBtn = $('#openCreateAutoMrPopupBtn');
        const $modal = $('#createAutoMrModal');
        if ($openBtn.length === 0 || $modal.length === 0) return;

        const $storeGroupSelect = $('#createAutoStoreGroup');
        const $filterStoreGroup = $('#StoreGroup');
        const $descInput = $('#createAutoDescription');
        const $form = $('#createAutoMrForm');

        function setDefaults() {
            if ($filterStoreGroup.length > 0 && $storeGroupSelect.length > 0) {
                $storeGroupSelect.val($filterStoreGroup.val());
            }

            if (($descInput.val() || '').toString().trim() === '') {
                const now = new Date();
                const month = String(now.getMonth() + 1).padStart(2, '0');
                $descInput.val(`Auto MR ${month}/${now.getFullYear()}`);
            }
        }

        $openBtn.off('click').on('click', function () {
            $modal.modal({ backdrop: 'static', keyboard: false, show: true });
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

    function searchItems(keyword, checkBalanceInStore, storeGroup) {
        return new Promise(function (resolve, reject) {
            const url = new URL(window.location.href);
            url.searchParams.set('handler', 'SearchItems');
            if (keyword) url.searchParams.set('keyword', keyword);
            if (checkBalanceInStore) url.searchParams.set('checkBalanceInStore', 'true');
            if (storeGroup !== null && storeGroup !== undefined && Number.isFinite(storeGroup)) {
                url.searchParams.set('storeGroup', String(storeGroup));
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

    function renderSearchRows(body, items) {
        body.innerHTML = '';
        if (!items || items.length === 0) {
            body.innerHTML = '<tr><td colspan="4" class="text-center text-muted">No data</td></tr>';
            return;
        }

        items.forEach(function (item) {
            const tr = document.createElement('tr');
            tr.dataset.itemCode = item.itemCode || '';
            tr.dataset.itemName = item.itemName || '';
            tr.dataset.unit = item.unit || '';
            tr.dataset.inStock = item.inStock || 0;
            tr.dataset.storeGroupId = item.storeGroupId || 0;
            tr.innerHTML = `
                <td><input type="checkbox" class="create-mr-search-checkbox"></td>
                <td>${escapeHtml(item.itemCode || '')}</td>
                <td>${escapeHtml(item.itemName || '')}</td>
                <td>${escapeHtml(item.unit || '')}</td>`;
            body.appendChild(tr);
        });
    }

    function renderSelectedRows(body, selectedItems) {
        body.innerHTML = '';
        if (!selectedItems || selectedItems.length === 0) {
            body.innerHTML = '<tr><td colspan="4" class="text-center text-muted">No selected item</td></tr>';
            return;
        }

        selectedItems.forEach(function (item) {
            const tr = document.createElement('tr');
            tr.dataset.itemCode = item.itemCode;
            tr.innerHTML = `
                <td><input type="checkbox" class="create-mr-selected-checkbox"></td>
                <td>${escapeHtml(item.itemCode || '')}</td>
                <td>${escapeHtml(item.itemName || '')}</td>
                <td>${escapeHtml(item.unit || '')}</td>`;
            body.appendChild(tr);
        });
    }

    function formatDateTime(value) {
        if (!value) return '';
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return '';

        const dd = String(date.getDate()).padStart(2, '0');
        const mm = String(date.getMonth() + 1).padStart(2, '0');
        const yyyy = date.getFullYear();
        const hh = String(date.getHours()).padStart(2, '0');
        const mi = String(date.getMinutes()).padStart(2, '0');

        return `${dd}/${mm}/${yyyy} ${hh}:${mi}`;
    }

    function truncateText(value, maxLength) {
        const raw = (value || '').toString();
        if (raw.length <= maxLength) return raw;
        return `${raw.substring(0, maxLength)}...`;
    }

    function escapeHtml(value) {
        return (value || '').toString()
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }
})();
