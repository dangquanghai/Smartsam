(function () {
    'use strict';

    const CONFIG = {
        pageSize: typeof defaultPageSize !== 'undefined' ? defaultPageSize : 13,
        selectors: {
            tbody: '#linenReceivingTable tbody',
            pagination: '#pagination'
        }
    };

    let state = {
        selectedReceiveId: null,
        selectedActions: null,
        currentPage: 1,
        currentDataRows: []
    };

    function initializeSearchDateRange() {
        if (typeof window.initSimpleDateRange !== 'function') {
            return;
        }

        window.initSimpleDateRange('LinenReceivingDateRange', '#Filter_FromDate', '#Filter_ToDate', {
            linkedCalendars: false
        });

        const fromDate = $('#Filter_FromDate').val();
        const toDate = $('#Filter_ToDate').val();
        if (fromDate && toDate && typeof window.setDateRangeValue === 'function') {
            window.setDateRangeValue('LinenReceivingDateRange', fromDate, toDate);
        }
    }

    function performSearch(page = 1) {
        state.currentPage = page;

        const token = $('input[name="__RequestVerificationToken"]').val();
        const filter = {
            fromDate: $('#Filter_FromDate').val() || null,
            toDate: $('#Filter_ToDate').val() || null,
            receiveId: parseNullableInt($('#Filter_ReceiveId').val()),
            isLocked: $('#Filter_IsLocked').is(':checked'),
            page: state.currentPage,
            pageSize: CONFIG.pageSize
        };

        syncSearchQuery(filter);
        showLoading(true);

        $.ajax({
            url: '?handler=Search',
            type: 'POST',
            contentType: 'application/json',
            headers: { RequestVerificationToken: token },
            data: JSON.stringify(filter),
            success: function (response) {
                if (response.success) {
                    state.currentDataRows = response.data || [];
                    renderRows(state.currentDataRows);

                    updatePagination(response.total, response.page, response.pageSize, response.totalPages);
                    resetActions();
                } else {
                    showError('Search failed: ' + response.message);
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

    function renderRows(items) {
        const $tbody = $(CONFIG.selectors.tbody);
        $tbody.empty();

        if (!items || items.length === 0) {
            $tbody.append('<tr><td colspan="6" class="text-center py-4">No data</td></tr>');
            return;
        }

        const rows = items.map(function (item, index) {
            const row = item.data || {};
            const actions = item.actions || {};
            const receiveId = row.receiveID || row.receiveId || '';
            const lockChecked = row.isLocked ? 'checked' : '';
            const linkClass = actions.canAccess ? 'linen-receiving-link text-primary font-weight-bold' : 'linen-receiving-link text-muted font-weight-bold';

            return `
            <tr data-index="${index}" class="linen-receiving-row">
                <td style="width:32px;"><input type="radio" name="selectedLinenReceiving" value="${index}"></td>
                <td style="white-space:nowrap">
                    <a href="javascript:void(0)" class="${linkClass}" style="text-decoration:underline">${encodeHtml(receiveId)}</a>
                </td>
                <td>${encodeHtml(row.receiveDateText || '')}</td>
                <td class="vni-font">${encodeHtml(row.description || '')}</td>
                <td class="text-center"><input type="checkbox" disabled ${lockChecked}></td>
                <td class="vni-font">${encodeHtml(row.deliveryInfor || '')}</td>
            </tr>`;
        });

        $tbody.html(rows.join(''));
    }

    function initEvents() {
        $(document).off('click', '.linen-receiving-row').on('click', '.linen-receiving-row', function (e) {
            const isControl = $(e.target).closest('.linen-receiving-link, input, button').length > 0;
            if (isControl) {
                return;
            }

            const $radio = $(this).find('input[name="selectedLinenReceiving"]');
            if (!$radio.is(':checked')) {
                $radio.prop('checked', true).trigger('change');
            }
        });

        $(document).off('change', "input[name='selectedLinenReceiving']").on('change', "input[name='selectedLinenReceiving']", function (e) {
            e.stopPropagation();
            const index = $(this).val();
            const item = state.currentDataRows[index];
            state.selectedReceiveId = item?.data?.receiveID || item?.data?.receiveId || null;
            state.selectedActions = item?.actions || null;
            updateActionButtons();
        });

        $(document).off('click', '.linen-receiving-link').on('click', '.linen-receiving-link', function (e) {
            e.preventDefault();
            e.stopPropagation();
            openDetail($(this).closest('tr').data('index'));
        });

        $(document).off('dblclick', '.linen-receiving-row').on('dblclick', '.linen-receiving-row', function (e) {
            e.preventDefault();
            openDetail($(this).data('index'));
        });

        $(document).off('click', '#pagination a.page-link').on('click', '#pagination a.page-link', function (e) {
            e.preventDefault();
            const page = $(this).data('page');
            if (page) {
                performSearch(page);
            }
        });

        $('#btnAdd').off('click').on('click', function () {
            window.location.href = `/Inventory/LinenReceiving/LinenReceivingDetail?mode=add&returnUrl=${encodeURIComponent(getCurrentListUrl())}`;
        });

        $('#btnDelete').off('click').on('click', deleteSelected);

        $('#btnPrint').off('click').on('click', function () {
            if (!state.selectedReceiveId) {
                alert('Please select linen receiving.');
                return;
            }

            window.location.href = `/Inventory/LinenReport?reportType=receive&descriptionId=${encodeURIComponent(state.selectedReceiveId)}&lockedReportType=receive&popup=true`;
        });

        $('#linenReceivingSearchForm').off('submit').on('submit', function (e) {
            e.preventDefault();
            performSearch(1);
        });
    }

    function resetActions() {
        state.selectedReceiveId = null;
        state.selectedActions = null;
        updateActionButtons();
    }

    function updateActionButtons() {
        const hasSelection = !!state.selectedReceiveId;
        $('#btnDelete').prop('disabled', !hasSelection || !state.selectedActions?.canEdit);
        $('#btnPrint').prop('disabled', !hasSelection || !state.selectedActions?.canAccess);
    }

    function updatePagination(total, page, pageSize, totalPages) {
        const $pagination = $(CONFIG.selectors.pagination);
        $('#total-records-badge').text(`${total} records`).addClass('badge-success');

        if (total === 0) {
            $('#pagination-info').html('<small>No records</small>');
            $pagination.empty();
            return;
        }

        const start = ((page - 1) * pageSize) + 1;
        const end = Math.min(page * pageSize, total);
        $('#pagination-info').html(`<small>Showing ${start}-${end} / ${total}</small>`);

        let html = `<li class="page-item ${page <= 1 ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${page - 1}">&laquo;</a></li>`;

        for (let i = 1; i <= totalPages; i++) {
            if (i === 1 || i === totalPages || (i >= page - 2 && i <= page + 2)) {
                html += `<li class="page-item ${i === page ? 'active' : ''}"><a class="page-link" href="#" data-page="${i}">${i}</a></li>`;
            } else if (i === page - 3 || i === page + 3) {
                html += '<li class="page-item disabled"><span class="page-link">...</span></li>';
            }
        }

        html += `<li class="page-item ${page >= totalPages ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${page + 1}">&raquo;</a></li>`;
        $pagination.html(html);
    }

    function initializePage() {
        initEvents();
        initializeSearchDateRange();
        performSearch(1);
    }

    function showLoading(show) {
        if (show) {
            $(CONFIG.selectors.tbody).html('<tr><td colspan="6" class="text-center py-4"><div class="spinner-border spinner-border-sm text-primary"></div></td></tr>');
        }
    }

    function showError(message) {
        $(CONFIG.selectors.tbody).html(`<tr><td colspan="6" class="text-center text-danger py-4">${encodeHtml(message)}</td></tr>`);
    }

    function openDetail(index) {
        const item = state.currentDataRows[index];
        if (!item) {
            return;
        }

        if (item.actions?.canAccess) {
            const id = item.data.receiveID || item.data.receiveId;
            const mode = item.actions.accessMode || 'view';
            window.location.href = `/Inventory/LinenReceiving/LinenReceivingDetail?id=${id}&mode=${mode}&returnUrl=${encodeURIComponent(getCurrentListUrl())}`;
        } else {
            alert('You do not have permission to view this Linen Receiving.');
        }
    }

    function deleteSelected() {
        if (!state.selectedReceiveId) {
            alert('Please select linen receiving.');
            return;
        }

        if (!window.confirm('Are you sure to delete this receiving?')) {
            return;
        }

        const token = $('input[name="__RequestVerificationToken"]').val();
        $.ajax({
            url: '?handler=Delete',
            type: 'POST',
            contentType: 'application/json',
            headers: { RequestVerificationToken: token },
            data: JSON.stringify({ receiveId: state.selectedReceiveId }),
            success: function (response) {
                if (response?.success) {
                    performSearch(state.currentPage);
                } else {
                    alert(response?.message || 'Delete failed.');
                }
            },
            error: function (xhr) {
                alert(xhr.responseJSON?.message || 'Delete failed.');
            }
        });
    }

    function syncSearchQuery(filter) {
        const url = new URL(window.location.href);
        setQueryValue(url, 'Filter.FromDate', filter.fromDate);
        setQueryValue(url, 'Filter.ToDate', filter.toDate);
        setQueryValue(url, 'Filter.ReceiveId', filter.receiveId);
        if (filter.isLocked) {
            url.searchParams.set('Filter.IsLocked', 'true');
        } else {
            url.searchParams.delete('Filter.IsLocked');
        }

        window.history.replaceState({}, '', `${url.pathname}${url.search}`);
    }

    function setQueryValue(url, key, value) {
        if (value === null || value === undefined || value === '') {
            url.searchParams.delete(key);
            return;
        }

        url.searchParams.set(key, value);
    }

    function getCurrentListUrl() {
        return `${window.location.pathname}${window.location.search}`;
    }

    function parseNullableInt(value) {
        if (value === null || value === undefined || value === '') {
            return null;
        }

        const parsed = parseInt(value, 10);
        return Number.isNaN(parsed) ? null : parsed;
    }

    function encodeHtml(value) {
        return $('<div>').text(value).html();
    }

    $(document).ready(initializePage);
})();
