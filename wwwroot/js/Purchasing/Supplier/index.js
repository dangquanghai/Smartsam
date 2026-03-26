(function () {
    'use strict';
    // Tracking comment: keep Supplier index script marked as touched for current work.

    let pageSize = typeof defaultPageSize !== 'undefined' ? defaultPageSize : 13;
    let selectedSupplierId = null;
    let currentPage = 1;
    let currentDataRows = [];

    // ========== SEARCH FUNCTION ==========
    function performSearch(page = 1, options = {}) {
        currentPage = page;
        const token = $('input[name="__RequestVerificationToken"]').val();

        const viewMode = ($('input[name="ViewMode"]:checked').val() || 'current').toLowerCase();
        const yearRaw = ($('#Year').val() || '').toString().trim();
        const year = yearRaw === '' ? null : parseInt(yearRaw, 10);

        const filter = {
            viewMode: viewMode,
            year: viewMode === 'byyear' && Number.isFinite(year) ? year : null,
            deptId: parseNullableInt($('#DeptId').val()),
            supplierCode: ($('#SupplierCode').val() || '').trim() || null,
            supplierName: ($('#SupplierName').val() || '').trim() || null,
            business: ($('#Business').val() || '').trim() || null,
            contact: ($('#Contact').val() || '').trim() || null,
            statusId: parseNullableInt($('#StatusId').val()),
            isNew: $('#IsNew').is(':checked'),
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
                const isSuccess = !!(response && (response.success === true || response.Success === true));
                if (isSuccess) {
                    const rows = (response && (response.data || response.Data)) || [];
                    const total = (response && (response.total ?? response.Total)) || 0;
                    const current = (response && (response.page ?? response.Page)) || 1;
                    const size = (response && (response.pageSize ?? response.PageSize)) || pageSize;
                    const pages = (response && (response.totalPages ?? response.TotalPages)) || 1;

                    currentDataRows = rows;
                    renderSuppliers(currentDataRows);
                    updatePagination(total, current, size, pages);
                    resetActions();
                    if (options && options.reselectSupplierId) {
                        restoreSelectionBySupplierId(options.reselectSupplierId);
                    }
                } else {
                    const message = (response && (response.message || response.Message))
                        || 'Search failed.';
                    showError(message);
                }
            },
            error: function (xhr) {
                if (xhr && xhr.status === 403) {
                    showError('You have no permission to view supplier list.');
                    return;
                }
                showError('Cannot load supplier data.');
            },
            complete: function () {
                showLoading(false);
            }
        });
    }

    // ========== RENDER TABLE ==========
    function renderSuppliers(items) {
        const tbody = $('#supplierTable tbody');
        tbody.empty();

        if (!items || items.length === 0) {
            tbody.append('<tr class="supplier-grid-state"><td colspan="13" class="text-center py-4">No suppliers found</td></tr>');
            return;
        }

        items.forEach(function (item, index) {
            const s = item.data || {};
            const actions = item.actions || {};
            const canAccess = actions.canAccess === true;
            const supplierNameText = s.supplierName || s.SupplierName || '';
            const addressText = s.address || s.Address || '';
            const contactText = s.contact || s.Contact || '';
            const businessText = s.business || s.Business || '';

            const supplierCode = escapeHtml(s.supplierCode || s.SupplierCode || '');
            const supplierCodeHtml = canAccess
                ? `<a href="javascript:void(0)" class="supplier-link text-primary font-weight-bold" style="text-decoration:underline">${supplierCode}</a>`
                : `<span class="text-muted font-weight-bold">${supplierCode}</span>`;

            const supplierStatusId = s.status ?? s.Status ?? '';

            const row = `
            <tr data-id="${s.supplierID || s.SupplierID || 0}" data-supplier-id="${s.supplierID || s.SupplierID || 0}" data-index="${index}" style="cursor:pointer">
                <td><input type="radio" name="selectedSupplier" value="${index}"></td>
                <td>${supplierCodeHtml}</td>
                <td class="vni-font">${buildTruncatedCell(supplierNameText, 30)}</td>
                <td class="vni-font">${buildTruncatedCell(addressText, 42)}</td>
                <td>${escapeHtml(s.phone || s.Phone || '')}</td>
                <td>${escapeHtml(s.mobile || s.Mobile || '')}</td>
                <td>${escapeHtml(s.fax || s.Fax || '')}</td>
                <td class="vni-font">${buildTruncatedCell(contactText, 26)}</td>
                <td class="vni-font">${escapeHtml(s.position || s.Position || '')}</td>
                <td class="vni-font">${buildTruncatedCell(businessText, 30)}</td>
                <td>${escapeHtml(s.supplierStatusName || s.SupplierStatusName || '')}</td>
                <td style="display: none;">${escapeHtml(supplierStatusId)}</td>
                <td>${escapeHtml(s.deptCode || s.DeptCode || '')}</td>
            </tr>`;

        tbody.append(row);
        });
    }

    function getSupplierRowData($row) {
        if (!$row || $row.length === 0) return null;
        const supplierId = getSupplierIdFromRow($row);
        if (supplierId === null) return null;

        return findSupplierItemById(supplierId);
    }

    function getSupplierIdFromRow($row) {
        if (!$row || $row.length === 0) return null;

        const rawSupplierId = $row.data('supplier-id') || $row.data('id') || $row.attr('data-supplier-id') || $row.attr('data-id');
        const supplierId = Number.parseInt((rawSupplierId || '').toString(), 10);
        return Number.isFinite(supplierId) ? supplierId : null;
    }

    function findSupplierItemById(supplierId) {
        if (!Number.isFinite(supplierId)) return null;

        return currentDataRows.find(function (item) {
            const data = item && item.data ? item.data : {};
            const currentId = data.supplierID ?? data.SupplierID;
            return Number.parseInt((currentId || '').toString(), 10) === supplierId;
        }) || null;
    }

    // ========== BAT SU KIEN LINK SUPPLIER CODE ==========
    $(document).on('click', '.supplier-link', function (e) {
        e.preventDefault();
        e.stopPropagation();

        const $row = $(this).closest('tr');
        const supplierId = getSupplierIdFromRow($row);
        const item = Number.isFinite(supplierId) ? findSupplierItemById(supplierId) : null;

        if (!item || !item.actions) {
            if (supplierId) {
                window.location.href = buildDetailUrl(supplierId, 'view');
            }
            return;
        }

        if (item.actions.canAccess === true) {
            const resolvedSupplierId = item.data.supplierID || item.data.SupplierID || supplierId;
            const accessMode = (item.actions.accessMode || 'view').toString().toLowerCase();
            if (resolvedSupplierId) {
                window.location.href = buildDetailUrl(resolvedSupplierId, accessMode);
            }
        } else {
            alert('You have no right to view this supplier.');
        }
    });

    // ========== BAT SU KIEN THAY DOI RADIO (QUAN TRONG NHAT) ==========
    $(document).on('change', 'input[name="selectedSupplier"]', function () {
        const $row = $(this).closest('tr');
        const supplierIdFromRow = getSupplierIdFromRow($row);
        const item = Number.isFinite(supplierIdFromRow) ? findSupplierItemById(supplierIdFromRow) : null;
        if (!item && !supplierIdFromRow) return;

        const supplierId = item
            ? (item.data.supplierID || item.data.SupplierID || supplierIdFromRow)
            : supplierIdFromRow;
        selectedSupplierId = supplierId;

        const canCopy = item && item.actions ? item.actions.canCopy === true : false;
        const updateVisibility = (selector, hasPermission) => {
            if (hasPermission) {
                $(selector).removeClass('d-none');
            } else {
                $(selector).addClass('d-none');
            }
        };

        $('#copySelectedSupplierIdsCsvInput').val(selectedSupplierId);

        updateVisibility("#btnCopy", canCopy);

        $('#supplierTable tbody tr').removeClass('selected');
        $(this).closest('tr').addClass('selected');
    });

    $(document).on('click', '#supplierTable tbody tr', function (e) {
        if ($(e.target).closest('.supplier-link').length > 0) return;
        const radio = $(this).find('input[name="selectedSupplier"]');
        if (radio.length > 0) {
            radio.prop('checked', true).trigger('change');
        }
    });

    $(document).on('dblclick', '#supplierTable tbody tr', function () {
        const $row = $(this);
        const supplierId = getSupplierIdFromRow($row);
        const item = Number.isFinite(supplierId) ? findSupplierItemById(supplierId) : null;
        if (!item || !item.actions) {
            if (supplierId) {
                window.location.href = buildDetailUrl(supplierId, 'view');
            }
            return;
        }
        if (item.actions.canAccess !== true) return;

        const resolvedSupplierId = item.data.supplierID || item.data.SupplierID || supplierId;
        const accessMode = (item.actions.accessMode || 'view').toString().toLowerCase();
        if (resolvedSupplierId) {
            window.location.href = buildDetailUrl(resolvedSupplierId, accessMode);
        }
    });

    function buildDetailUrl(supplierId, accessMode) {
        let url = `/Purchasing/Supplier/SupplierDetail`;
        const viewMode = ($('input[name="ViewMode"]:checked').val() || 'current').toLowerCase();
        const mode = (accessMode || 'view').toString().toLowerCase();
        const query = new URLSearchParams();

        query.set('id', supplierId.toString());
        query.set('mode', viewMode === 'byyear' ? 'view' : mode);
        query.set('viewMode', viewMode);

        const yearRaw = ($('#Year').val() || '').toString().trim();
        const deptId = ($('#DeptId').val() || '').toString().trim();
        const supplierCode = ($('#SupplierCode').val() || '').toString().trim();
        const supplierName = ($('#SupplierName').val() || '').toString().trim();
        const business = ($('#Business').val() || '').toString().trim();
        const contact = ($('#Contact').val() || '').toString().trim();
        const statusId = ($('#StatusId').val() || '').toString().trim();
        const isNew = $('#IsNew').is(':checked');

        if (viewMode === 'byyear' && yearRaw !== '') query.set('year', yearRaw);
        if (deptId !== '') query.set('DeptId', deptId);
        if (supplierCode !== '') query.set('SupplierCode', supplierCode);
        if (supplierName !== '') query.set('SupplierName', supplierName);
        if (business !== '') query.set('Business', business);
        if (contact !== '') query.set('Contact', contact);
        if (statusId !== '') query.set('StatusId', statusId);
        if (isNew) query.set('IsNew', 'true');
        query.set('PageIndex', currentPage.toString());
        query.set('PageSize', pageSize.toString());

        url += `?${query.toString()}`;
        return url;
    }

    function resetActions() {
        selectedSupplierId = null;
        $('#copySelectedSupplierIdsCsvInput').val('');
        $('#btnCopy').addClass('d-none');
    }

    // ========== PAGINATION ==========
    function updatePagination(total, page, pageSizeValue, totalPages) {
        const $totalBadge = $('#total-records-badge');
        const $paginationInfo = $('#pagination-info');
        const $pagination = $('#pagination');

        $totalBadge.text(`${total} records`).addClass('badge-light');

        if (total === 0) {
            $paginationInfo.html('<small>No records</small>');
            $pagination.empty();
            return;
        }

        const start = ((page - 1) * pageSizeValue) + 1;
        const end = Math.min(page * pageSizeValue, total);
        $paginationInfo.html(`<small>Showing ${start} to ${end} of ${total} entries</small>`);

        let html = '';
        html += `<li class="page-item ${page <= 1 ? 'disabled' : ''}">
                    <a class="page-link" href="#" data-page="${page - 1}">Prev</a>
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
                    <a class="page-link" href="#" data-page="${page + 1}">Next</a>
                 </li>`;

        $pagination.html(html).show();

        $pagination.find('a.page-link').off('click').on('click', function (e) {
            e.preventDefault();
            const p = parseInt($(this).data('page'), 10);
            if (!Number.isFinite(p)) return;
            if ($(this).closest('.page-item').hasClass('disabled')) return;
            performSearch(p);
        });
    }

    // ========== COPY POPUP ==========
    function bindCopyModal() {
        const $modal = $('#copyYearModal');
        if ($modal.length === 0) return;

        $modal.off('shown.bs.modal.supplier').on('shown.bs.modal.supplier', function () {
            if (($('#copyYearInput').val() || '').toString() === '0') {
                $('#copyYearInput').val('');
            }

            $('#ConfirmCopy').prop('checked', false);
            $('#copyYearInput').removeClass('is-invalid');
            $('#copyYearValidation').addClass('d-none').hide();
            syncCopyButtonState(false);
            $('#copyYearInput').trigger('focus');
        });

        $modal.off('hidden.bs.modal.supplier').on('hidden.bs.modal.supplier', function () {
            $('#ConfirmCopy').prop('checked', false);
            $('#copyYearInput').removeClass('is-invalid');
            $('#copyYearValidation').addClass('d-none').hide();
            syncCopyButtonState(false);
        });

        $('#ConfirmCopy').off('change.supplier').on('change.supplier', function () {
            syncCopyButtonState(false);
        });

        $('#copyYearInput').off('input.supplier').on('input.supplier', function () {
            syncCopyButtonState(false);
        });

        $('#copyYearInput').off('blur.supplier').on('blur.supplier', function () {
            syncCopyButtonState(true);
        });
    }

    function syncCopyButtonState(showYearError) {
        const isYearValid = validateCopyYearInput(showYearError === true);
        const canSubmit = $('#ConfirmCopy').is(':checked') && isYearValid;
        $('#copyYearSubmitBtn').prop('disabled', !canSubmit);
    }

    function validateCopyYearInput(showError) {
        const $copyYearInput = $('#copyYearInput');
        if ($copyYearInput.length === 0) return false;

        const raw = ($copyYearInput.val() || '').toString().trim();
        const min = Number($copyYearInput.attr('min') || '2000');
        const max = Number($copyYearInput.attr('max') || '2099');
        const year = Number(raw);

        const isValid = raw.length === 4 && Number.isInteger(year) && year >= min && year <= max;
        const hasExistingError = $copyYearInput.hasClass('is-invalid');
        const shouldShowError = !isValid && (showError || hasExistingError);

        $copyYearInput.toggleClass('is-invalid', shouldShowError);
        if (shouldShowError) {
            $('#copyYearValidation').text(`Enter a year from ${min} to ${max}.`).removeClass('d-none').show();
        } else {
            $('#copyYearValidation').addClass('d-none').hide();
        }

        return isValid;
    }

    // ========== INITIALIZE ==========
    function initializePage() {
        const initialPageSize = parseInt(($('#PageSize').val() || '').toString(), 10);
        if (Number.isFinite(initialPageSize) && initialPageSize > 0) {
            pageSize = initialPageSize;
        }

        const initialPageIndex = parseInt(($('#PageIndex').val() || '').toString(), 10);
        const startPage = (Number.isFinite(initialPageIndex) && initialPageIndex > 0) ? initialPageIndex : 1;

        // 1. Dang ky su kien nut bam
        $('#btnAdd').off('click').on('click', () => {
            const viewMode = ($('input[name="ViewMode"]:checked').val() || 'current').toLowerCase();
            const query = new URLSearchParams();
            query.set('mode', 'add');
            query.set('viewMode', viewMode);

            const yearRaw = ($('#Year').val() || '').toString().trim();
            const deptId = ($('#DeptId').val() || '').toString().trim();
            const supplierCode = ($('#SupplierCode').val() || '').toString().trim();
            const supplierName = ($('#SupplierName').val() || '').toString().trim();
            const business = ($('#Business').val() || '').toString().trim();
            const contact = ($('#Contact').val() || '').toString().trim();
            const statusId = ($('#StatusId').val() || '').toString().trim();
            const isNew = $('#IsNew').is(':checked');

            if (viewMode === 'byyear' && yearRaw !== '') query.set('year', yearRaw);
            if (deptId !== '') query.set('DeptId', deptId);
            if (supplierCode !== '') query.set('SupplierCode', supplierCode);
            if (supplierName !== '') query.set('SupplierName', supplierName);
            if (business !== '') query.set('Business', business);
            if (contact !== '') query.set('Contact', contact);
            if (statusId !== '') query.set('StatusId', statusId);
            if (isNew) query.set('IsNew', 'true');
            query.set('PageIndex', currentPage.toString());
            query.set('PageSize', pageSize.toString());

            window.location.href = `/Purchasing/Supplier/SupplierDetail?${query.toString()}`;
        });

        $('#btnCopy').off('click').on('click', function () {
            if (!selectedSupplierId) return;
            syncListStateToPostForms();
            $('#copySelectedSupplierIdsCsvInput').val(selectedSupplierId);
            $('#copyYearModal').modal('show');
        });

        $('#btnExcel').off('click').on('click', function () {
            window.location.href = buildExportExcelUrl();
        });

        // 2. Xu ly mode current/byyear
        $('#viewCurrent, #viewByYear').off('change').on('change', function () {
            const showYear = $('#viewByYear').is(':checked');
            $('#Year').prop('disabled', !showYear);
            $('#yearGroup').toggleClass('show', showYear);
            if (!showYear) $('#Year').val('');
        }).trigger('change');

        // 3. Xu ly Form Search
        $('#supplierSearchForm').off('submit').on('submit', function (e) {
            e.preventDefault();
            performSearch(1);
        });

        // 4. Init popup Copy + reset action state
        bindCopyModal();
        resetActions();
        syncListStateToPostForms();

        // 5. Tu dong search lan dau khi load trang
        performSearch(startPage);
    }

    function buildExportExcelUrl() {
        const query = new URLSearchParams();
        const viewMode = ($('input[name="ViewMode"]:checked').val() || 'current').toLowerCase();
        const yearRaw = ($('#Year').val() || '').toString().trim();
        const deptId = ($('#DeptId').val() || '').toString().trim();
        const supplierCode = ($('#SupplierCode').val() || '').toString().trim();
        const supplierName = ($('#SupplierName').val() || '').toString().trim();
        const business = ($('#Business').val() || '').toString().trim();
        const contact = ($('#Contact').val() || '').toString().trim();
        const statusId = ($('#StatusId').val() || '').toString().trim();
        const isNew = $('#IsNew').is(':checked');

        query.set('handler', 'ExportExcel');
        query.set('ViewMode', viewMode);
        if (viewMode === 'byyear' && yearRaw !== '') query.set('Year', yearRaw);
        if (deptId !== '') query.set('DeptId', deptId);
        if (supplierCode !== '') query.set('SupplierCode', supplierCode);
        if (supplierName !== '') query.set('SupplierName', supplierName);
        if (business !== '') query.set('Business', business);
        if (contact !== '') query.set('Contact', contact);
        if (statusId !== '') query.set('StatusId', statusId);
        if (isNew) query.set('IsNew', 'true');

        return `?${query.toString()}`;
    }

    // Dong bo state bo loc hien tai vao cac form POST (Copy)
    // de sau redirect van giu dung filter/page user dang xem.
    function syncListStateToPostForms() {
        const state = collectListState();
        const $copyForm = $('#copyYearForm');

        Object.keys(state).forEach(function (key) {
            setHiddenFieldValue($copyForm, key, state[key]);
        });
    }

    function collectListState() {
        const viewMode = ($('input[name="ViewMode"]:checked').val() || 'current').toLowerCase();
        const yearRaw = ($('#Year').val() || '').toString().trim();
        const deptId = ($('#DeptId').val() || '').toString().trim();
        const supplierCode = ($('#SupplierCode').val() || '').toString().trim();
        const supplierName = ($('#SupplierName').val() || '').toString().trim();
        const business = ($('#Business').val() || '').toString().trim();
        const contact = ($('#Contact').val() || '').toString().trim();
        const statusId = ($('#StatusId').val() || '').toString().trim();
        const isNew = $('#IsNew').is(':checked') ? 'true' : 'false';

        return {
            ViewMode: viewMode,
            Year: viewMode === 'byyear' ? yearRaw : '',
            DeptId: deptId,
            SupplierCode: supplierCode,
            SupplierName: supplierName,
            Business: business,
            Contact: contact,
            StatusId: statusId,
            IsNew: isNew,
            PageIndex: currentPage.toString(),
            PageSize: pageSize.toString()
        };
    }

    function restoreSelectionBySupplierId(supplierId) {
        if (!supplierId) return;

        const rowIndex = currentDataRows.findIndex(function (item) {
            const data = item && item.data ? item.data : {};
            const currentId = data.supplierID || data.SupplierID;
            return Number(currentId) === Number(supplierId);
        });

        if (rowIndex < 0) return;

        const $radio = $(`#supplierTable tbody tr[data-index="${rowIndex}"] input[name="selectedSupplier"]`);
        if ($radio.length > 0) {
            $radio.prop('checked', true).trigger('change');
        }
    }

    function showPageMessage(type, message) {
        const $host = $('#supplierAjaxMessageHost');
        if ($host.length === 0) return;

        const normalizedType = (type || 'info').toString().toLowerCase();
        const alertClass = normalizedType === 'success'
            ? 'alert-success'
            : normalizedType === 'warning'
                ? 'alert-warning'
                : normalizedType === 'error'
                    ? 'alert-danger'
                    : 'alert-info';
        const iconClass = normalizedType === 'success'
            ? 'fa-check-circle'
            : normalizedType === 'warning'
                ? 'fa-exclamation-triangle'
                : normalizedType === 'error'
                    ? 'fa-ban'
                    : 'fa-info-circle';

        const html = `
            <div class="alert ${alertClass} alert-dismissible fade show mb-0" role="alert">
                <strong><i class="fas ${iconClass}"></i> Notification:</strong> ${escapeHtml(message || '')}
                <button type="button" class="close" data-dismiss="alert" aria-label="Close">
                    <span aria-hidden="true">&times;</span>
                </button>
            </div>`;

        $host.html(html).removeClass('d-none');
    }

    function setHiddenFieldValue($form, name, value) {
        if (!$form || $form.length === 0) return;

        let $field = $form.find(`input[name='${name}']`);
        if ($field.length === 0) {
            $field = $('<input>', { type: 'hidden', name: name });
            $form.append($field);
        }
        $field.val(value ?? '');
    }

    function parseNullableInt(value) {
        if (value === undefined || value === null) return null;
        const raw = value.toString().trim();
        if (raw === '') return null;
        const num = parseInt(raw, 10);
        return Number.isFinite(num) ? num : null;
    }

    function showLoading(show) {
        if (!show) return;
        $('#supplierTable tbody').html('<tr class="supplier-grid-state"><td colspan="13" class="text-center py-4"><div class="spinner-border spinner-border-sm"></div> Loading...</td></tr>');
    }

    function showError(message) {
        $('#supplierTable tbody').html(`<tr class="supplier-grid-state"><td colspan="13" class="text-center text-danger py-4">${escapeHtml(message)}</td></tr>`);
    }

    function escapeHtml(input) {
        return (input || '')
            .toString()
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function buildTruncatedCell(input, maxLength) {
        const raw = (input || '').toString();
        const display = raw.length > maxLength ? raw.substring(0, maxLength) + '...' : raw;
        return `<span title="${escapeHtml(raw)}">${escapeHtml(display)}</span>`;
    }

    $(document).ready(initializePage);
})();
