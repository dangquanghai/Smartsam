(function () {
    'use strict';

    const CONFIG = {
        pageSize: typeof defaultPageSize !== 'undefined' ? defaultPageSize : 13,
        selectors: {
            tbody: 'table tbody',
            pagination: '#pagination',
            actionBtns: '#btnEdit'
        }
    };

    let state = {
        selectedLinenId: null,
        selectedAccessMode: 'view',
        currentPage: 1,
        currentDataRows: []
    };

    function performSearch(page = 1) {
        state.currentPage = page;
        const token = $('input[name="__RequestVerificationToken"]').val();

        const filter = {
            isLinen: $('#Filter_IsLinen').is(':checked'),
            isUniform: $('#Filter_IsUniform').is(':checked'),
            isRegular: $('#Filter_IsRegular').is(':checked'),
            isService: $('#Filter_IsService').is(':checked'),
            page: state.currentPage,
            pageSize: CONFIG.pageSize
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
                    state.currentDataRows = response.data;
                    renderLinenList(response.data);
                    updatePagination(response.total, response.page, response.pageSize, response.totalPages);
                    resetActions();
                } else {
                    showError('Search failed: ' + response.message);
                }
            },
            error: (xhr, status, error) => showError('System connection error: ' + error),
            complete: () => showLoading(false)
        });
    }

    function renderLinenList(items) {
        const $tbody = $(CONFIG.selectors.tbody);
        $tbody.empty();

        if (!items || items.length === 0) {
            $tbody.append('<tr><td colspan="7" class="text-center py-4">No data</td></tr>');
            return;
        }

        const rows = items.map((item, index) => {
            const row = item.data;
            return `
            <tr data-index="${index}" class="linen-row">
                <td><input type="radio" name="selectedLinen" value="${index}"></td>
                <td style="white-space:nowrap">
                    <a href="javascript:void(0)" class="linen-link text-primary font-weight-bold" style="text-decoration:underline">
                        ${escapeHtml(row.linnenCode)}
                    </a>
                </td>
                <td class="linen-check-cell"><input type="checkbox" disabled ${row.isLinen ? 'checked' : ''}></td>
                <td class="linen-check-cell"><input type="checkbox" disabled ${row.isUniform ? 'checked' : ''}></td>
                <td>${escapeHtml(row.ecoWashHcmc || '')}</td>
                <td class="linen-check-cell"><input type="checkbox" disabled ${row.regular ? 'checked' : ''}></td>
                <td class="linen-check-cell"><input type="checkbox" disabled ${row.isOrder ? 'checked' : ''}></td>
            </tr>`;
        });
        $tbody.html(rows.join(''));
    }

    $(document).on('click', '.linen-row', function (e) {
        const isControl = $(e.target).closest('.linen-link, input, button').length > 0;
        if (isControl) return;

        const $radio = $(this).find('input[name="selectedLinen"]');
        if (!$radio.is(':checked')) {
            $radio.prop('checked', true).trigger('change');
        }
    });

    $(document).on("change", "input[name='selectedLinen']", function (e) {
        e.stopPropagation();
        const index = $(this).val();
        const item = state.currentDataRows[index];
        if (!item) return;

        state.selectedLinenId = item.data.id;
        state.selectedAccessMode = item.actions.accessMode || 'view';

        $('#btnEdit').toggleClass('d-none', !item.actions.canEdit);
    });

    $(document).on('click', '.linen-link', function (e) {
        e.preventDefault();
        e.stopPropagation();
        const index = $(this).closest('tr').data('index');
        const item = state.currentDataRows[index];

        if (item?.actions.canAccess) {
            const mode = item.actions.accessMode || 'view';
            window.location.href = buildDetailUrl(item.data.id, mode);
        } else {
            alert("You do not have permission to view this Linen item.");
        }
    });

    $(document).on('dblclick', '.linen-row', function (e) {
        e.preventDefault();
        const index = $(this).data('index');
        const item = state.currentDataRows[index];

        if (item?.actions.canAccess) {
            const mode = item.actions.accessMode || 'view';
            window.location.href = buildDetailUrl(item.data.id, mode);
        }
    });

    $(document).on('click', '#pagination a.page-link', function (e) {
        e.preventDefault();
        const p = $(this).data('page');
        if (p) performSearch(p);
    });

    $('#btnAdd').off('click').on('click', function () {
        window.location.href = buildDetailUrl(null, 'add');
    });

    $('#btnEdit').off('click').on('click', function () {
        if (!state.selectedLinenId) {
            alert("Please select a Linen item.");
            return;
        }

        window.location.href = buildDetailUrl(state.selectedLinenId, state.selectedAccessMode);
    });

    $('#btnExportExcel').off('click').on('click', function () {
        const exportUrl = buildExportExcelUrl();
        window.location.href = exportUrl;
    });

    $('form').off('submit').on('submit', function (e) {
        e.preventDefault();
        performSearch(1);
    });

    function resetActions() {
        state.selectedLinenId = null;
        state.selectedAccessMode = 'view';
        $(CONFIG.selectors.actionBtns).addClass('d-none');
    }

    function updatePagination(total, page, pageSize, totalPages) {
        const $pg = $(CONFIG.selectors.pagination);
        $('#total-records-badge').text(`${total} records`).addClass('badge-success');

        if (total === 0) {
            $('#pagination-info').html('<small>No records</small>');
            $pg.empty();
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
                html += `<li class="page-item disabled"><span class="page-link">...</span></li>`;
            }
        }

        html += `<li class="page-item ${page >= totalPages ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${page + 1}">&raquo;</a></li>`;
        $pg.html(html);
    }

    function initializePage() {
        resetActions();
        performSearch(1);
    }

    function showLoading(show) {
        if (show) $(CONFIG.selectors.tbody).html('<tr><td colspan="7" class="text-center py-4"><div class="spinner-border spinner-border-sm text-primary"></div></td></tr>');
    }

    function showError(m) {
        $(CONFIG.selectors.tbody).html(`<tr><td colspan="7" class="text-center text-danger py-4">${escapeHtml(m)}</td></tr>`);
    }

    function buildDetailUrl(id, mode) {
        const base = window.linenListPage?.detailUrl || '/Inventory/LinenList/LinenListDetail';
        const query = new URLSearchParams();
        if (id) {
            query.set('id', id);
        }
        query.set('mode', mode);
        return `${base}?${query.toString()}`;
    }

    function buildExportExcelUrl() {
        const base = window.linenListPage?.exportUrlBase || '?handler=ExportExcel';
        const query = new URLSearchParams();
        query.set('IsLinen', $('#Filter_IsLinen').is(':checked'));
        query.set('IsUniform', $('#Filter_IsUniform').is(':checked'));
        query.set('IsRegular', $('#Filter_IsRegular').is(':checked'));
        query.set('IsService', $('#Filter_IsService').is(':checked'));
        const joiner = base.includes('?') ? '&' : '?';
        return `${base}${joiner}${query.toString()}`;
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    $(document).ready(initializePage);
})();
