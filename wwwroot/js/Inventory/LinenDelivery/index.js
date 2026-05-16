(function () {
    'use strict';

    const CONFIG = {
        pageSize: typeof defaultPageSize !== 'undefined' ? defaultPageSize : 13,
        selectors: {
            tbody: '#linenDeliveryTable tbody',
            pagination: '#pagination'
        }
    };

    let state = {
        selectedDeliveryId: null,
        currentPage: 1,
        currentDataRows: []
    };

    function initializeSearchDateRange() {
        if (typeof window.initSimpleDateRange !== 'function') {
            return;
        }

        window.initSimpleDateRange('LinenDeliveryDateRange', '#Filter_FromDate', '#Filter_ToDate', {
            linkedCalendars: false
        });

        const fromDate = $('#Filter_FromDate').val();
        const toDate = $('#Filter_ToDate').val();
        if (fromDate && toDate && typeof window.setDateRangeValue === 'function') {
            window.setDateRangeValue('LinenDeliveryDateRange', fromDate, toDate);
        }
    }

    function performSearch(page = 1) {
        state.currentPage = page;

        const token = $('input[name="__RequestVerificationToken"]').val();
        const filter = {
            fromDate: $('#Filter_FromDate').val() || null,
            toDate: $('#Filter_ToDate').val() || null,
            deliveryId: parseNullableInt($('#Filter_DeliveryId').val()),
            deliveryTypeId: parseNullableInt($('#Filter_DeliveryTypeId').val()),
            supplierId: parseNullableInt($('#Filter_SupplierId').val()),
            closed: $('#Filter_Closed').is(':checked'),
            isRent: $('#Filter_IsRent').is(':checked'),
            page: state.currentPage,
            pageSize: CONFIG.pageSize
        };

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
            $tbody.append('<tr><td colspan="7" class="text-center py-4">No data</td></tr>');
            return;
        }

        const rows = items.map(function (item, index) {
            const row = item.data || {};
            const actions = item.actions || {};
            const deliveryId = row.deliveryID || row.deliveryId || '';
            const idText = encodeHtml(deliveryId);
            const dateText = encodeHtml(row.deliveryDateText || '');
            const description = encodeHtml(row.description || '');
            const typeText = encodeHtml(row.deliveryTypeName || '');
            const supplierText = encodeHtml(row.supplierName || '');
            const closeChecked = row.closed ? 'checked' : '';
            const linkClass = actions.canAccess ? 'linen-delivery-link text-primary font-weight-bold' : 'linen-delivery-link text-muted font-weight-bold';

            return `
            <tr data-index="${index}" class="linen-delivery-row">
                <td style="width:32px;"><input type="radio" name="selectedLinenDelivery" value="${index}"></td>
                <td style="white-space:nowrap">
                    <a href="javascript:void(0)" class="${linkClass}" style="text-decoration:underline">${idText}</a>
                </td>
                <td>${dateText}</td>
                <td class="vni-font">${description}</td>
                <td class="text-center"><input type="checkbox" disabled ${closeChecked}></td>
                <td class="vni-font">${typeText}</td>
                <td class="vni-font">${supplierText}</td>
            </tr>`;
        });

        $tbody.html(rows.join(''));
    }

    function initEvents() {
        $(document).off('click', '.linen-delivery-row').on('click', '.linen-delivery-row', function (e) {
            const isControl = $(e.target).closest('.linen-delivery-link, input, button').length > 0;
            if (isControl) {
                return;
            }

            const $radio = $(this).find('input[name="selectedLinenDelivery"]');
            if (!$radio.is(':checked')) {
                $radio.prop('checked', true).trigger('change');
            }
        });

        $(document).off('change', "input[name='selectedLinenDelivery']").on('change', "input[name='selectedLinenDelivery']", function (e) {
            e.stopPropagation();
            const index = $(this).val();
            const item = state.currentDataRows[index];
            state.selectedDeliveryId = item?.data?.deliveryID || item?.data?.deliveryId || null;
        });

        $(document).off('click', '.linen-delivery-link').on('click', '.linen-delivery-link', function (e) {
            e.preventDefault();
            e.stopPropagation();
            openDetail($(this).closest('tr').data('index'));
        });

        $(document).off('dblclick', '.linen-delivery-row').on('dblclick', '.linen-delivery-row', function (e) {
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
            window.location.href = '/Inventory/LinenDelivery/LinenDeliveryDetail?mode=add';
        });

        $('#linenDeliverySearchForm').off('submit').on('submit', function (e) {
            e.preventDefault();
            performSearch(1);
        });
    }

    function resetActions() {
        state.selectedDeliveryId = null;
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
            $(CONFIG.selectors.tbody).html('<tr><td colspan="7" class="text-center py-4"><div class="spinner-border spinner-border-sm text-primary"></div></td></tr>');
        }
    }

    function showError(message) {
        $(CONFIG.selectors.tbody).html(`<tr><td colspan="7" class="text-center text-danger py-4">${encodeHtml(message)}</td></tr>`);
    }

    function openDetail(index) {
        const item = state.currentDataRows[index];
        if (!item) {
            return;
        }

        if (item.actions?.canAccess) {
            const id = item.data.deliveryID || item.data.deliveryId;
            const mode = item.actions.accessMode || 'view';
            window.location.href = `/Inventory/LinenDelivery/LinenDeliveryDetail?id=${id}&mode=${mode}`;
        } else {
            alert('You do not have permission to view this linen delivery.');
        }
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
