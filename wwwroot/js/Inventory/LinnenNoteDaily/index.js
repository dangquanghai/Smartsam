(function () {
    'use strict';

    const CONFIG = {
        pageSize: typeof defaultPageSize !== 'undefined' ? defaultPageSize : 13,
        selectors: {
            tbody: '#linnenNoteTable tbody',
            pagination: '#pagination'
        }
    };

    let state = {
        selectedNoteId: null,
        currentPage: 1,
        currentDataRows: []
    };

    function syncBrowserUrlToSearchState(page, filter) {
        const currentFilter = filter || {};
        const query = new URLSearchParams();

        if (currentFilter.description) {
            query.set('Filter.Description', currentFilter.description);
        }
        if (currentFilter.fromDate) {
            query.set('Filter.FromDate', currentFilter.fromDate);
        }
        if (currentFilter.toDate) {
            query.set('Filter.ToDate', currentFilter.toDate);
        }
        query.set('Filter.Page', String(page || 1));
        query.set('Filter.PageSize', String(CONFIG.pageSize));

        const nextUrl = `${window.location.pathname}${query.toString() ? `?${query.toString()}` : ''}`;
        window.history.replaceState({}, document.title, nextUrl);
    }

    function buildCurrentReturnUrl() {
        return `${window.location.pathname}${window.location.search || ''}`;
    }

    function buildDetailUrl(noteId, mode) {
        const url = new URL('/Inventory/LinnenNoteDaily/LinnenNoteDailyDetail', window.location.origin);
        if (noteId) {
            url.searchParams.set('id', String(noteId));
        }
        url.searchParams.set('mode', mode || 'view');
        url.searchParams.set('returnUrl', buildCurrentReturnUrl());
        return url.toString();
    }

    function getInitialPage() {
        const currentUrl = new URL(window.location.href);
        const page = Number.parseInt(currentUrl.searchParams.get('Filter.Page') || '1', 10);
        return Number.isFinite(page) && page > 0 ? page : 1;
    }

    function initializeSearchDateRange() {
        if (typeof window.initSimpleDateRange !== 'function') {
            return;
        }

        window.initSimpleDateRange('LinnenNoteDateRange', '#Filter_FromDate', '#Filter_ToDate', {
            linkedCalendars: false
        });

        const fromDate = $('#Filter_FromDate').val();
        const toDate = $('#Filter_ToDate').val();
        if (fromDate && toDate && typeof window.setDateRangeValue === 'function') {
            window.setDateRangeValue('LinnenNoteDateRange', fromDate, toDate);
        }
    }

    function performSearch(page = 1) {
        state.currentPage = page;
        const token = $('input[name="__RequestVerificationToken"]').val();

        const filter = {
            description: $('#Filter_Description').val() || null,
            fromDate: $('#Filter_FromDate').val() || null,
            toDate: $('#Filter_ToDate').val() || null,
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
                    state.currentDataRows = response.data || [];
                    renderLinnenNotes(state.currentDataRows);
                    initializeLegacyTooltips(CONFIG.selectors.tbody);
                    updatePagination(response.total, response.page, response.pageSize, response.totalPages);
                    syncBrowserUrlToSearchState(response.page, filter);
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

    function renderLinnenNotes(items) {
        const $tbody = $(CONFIG.selectors.tbody);
        $tbody.empty();

        if (!items || items.length === 0) {
            $tbody.append('<tr><td colspan="8" class="text-center py-4">No data</td></tr>');
            return;
        }

        const rows = items.map(function (item, index) {
            const row = item.data || {};
            const actions = item.actions || {};
            const idText = encodeHtml(row.id || '');
            const dateText = encodeHtml(row.dateCreateText || '');
            const closeChecked = row.isClose ? 'checked' : '';
            const rentChecked = row.isRent ? 'checked' : '';
            const startChecked = row.start ? 'checked' : '';
            const detailCount = encodeHtml(row.detailCount || 0);
            const linkClass = actions.canAccess ? 'linnen-note-link text-primary font-weight-bold' : 'linnen-note-link text-muted font-weight-bold';

            return `
            <tr data-index="${index}" class="linnen-note-row">
                <td style="width:32px;"><input type="radio" name="selectedLinnenNote" value="${index}"></td>
                <td style="white-space:nowrap">
                    <a href="javascript:void(0)" class="${linkClass}" style="text-decoration:underline">${idText}</a>
                </td>
                <td>${dateText}</td>
                <td class="vni-font">${buildEllipsisCell(row.description || '', 'vni-font')}</td>
                <td class="text-center"><input type="checkbox" disabled ${closeChecked}></td>
                <td class="text-center"><input type="checkbox" disabled ${rentChecked}></td>
                <td class="text-center"><input type="checkbox" disabled ${startChecked}></td>
                <td class="text-right">${detailCount}</td>
            </tr>`;
        });

        $tbody.html(rows.join(''));
    }

    function initEvents() {
        $(document).off('click', '.linnen-note-row').on('click', '.linnen-note-row', function (e) {
            const isControl = $(e.target).closest('.linnen-note-link, input, button').length > 0;
            if (isControl) {
                return;
            }

            const $radio = $(this).find('input[name="selectedLinnenNote"]');
            if (!$radio.is(':checked')) {
                $radio.prop('checked', true).trigger('change');
            }
        });

        $(document).off('change', "input[name='selectedLinnenNote']").on('change', "input[name='selectedLinnenNote']", function (e) {
            e.stopPropagation();
            const index = $(this).val();
            const item = state.currentDataRows[index];
            state.selectedNoteId = item?.data?.id || null;
        });

        $(document).off('click', '.linnen-note-link').on('click', '.linnen-note-link', function (e) {
            e.preventDefault();
            e.stopPropagation();
            openDetail($(this).closest('tr').data('index'));
        });

        $(document).off('dblclick', '.linnen-note-row').on('dblclick', '.linnen-note-row', function (e) {
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
            window.location.href = buildDetailUrl('', 'add');
        });

        $('#linnenNoteSearchForm').off('submit').on('submit', function (e) {
            e.preventDefault();
            performSearch(1);
        });
    }

    function resetActions() {
        state.selectedNoteId = null;
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
        performSearch(getInitialPage());
    }

    function showLoading(show) {
        if (show) {
            $(CONFIG.selectors.tbody).html('<tr><td colspan="8" class="text-center py-4"><div class="spinner-border spinner-border-sm text-primary"></div></td></tr>');
        }
    }

    function showError(message) {
        $(CONFIG.selectors.tbody).html(`<tr><td colspan="8" class="text-center text-danger py-4">${encodeHtml(message)}</td></tr>`);
    }

    function openDetail(index) {
        const item = state.currentDataRows[index];
        if (!item) {
            return;
        }

        if (item.actions?.canAccess) {
            const id = item.data.id;
            const mode = item.actions.accessMode || 'view';
            window.location.href = buildDetailUrl(id, mode);
        } else {
            alert('You do not have permission to view this pantry linen note.');
        }
    }

    function encodeHtml(value) {
        return $('<div>').text(value).html();
    }

    function buildEllipsisCell(input, extraClass) {
        const raw = (input || '').toString();
        const className = extraClass ? `app-table-ellipsis ${extraClass}` : 'app-table-ellipsis';
        const tooltipFont = extraClass ? ` data-tooltip-font="${encodeHtml(extraClass)}"` : '';
        return `<span class="${className}" data-toggle="tooltip"${tooltipFont} title="${encodeHtml(raw)}">${encodeHtml(raw)}</span>`;
    }

    function initializeLegacyTooltips(container) {
        const $container = $(container || document);
        const $tooltips = $container.find('[data-toggle="tooltip"]');
        if (!$tooltips.length || typeof $tooltips.tooltip !== 'function') {
            return;
        }

        $tooltips.tooltip('dispose').tooltip({
            container: 'body',
            trigger: 'hover',
            boundary: 'window'
        });

        $tooltips.off('inserted.bs.tooltip.linnenNoteFont').on('inserted.bs.tooltip.linnenNoteFont', function () {
            const fontClass = ($(this).data('tooltip-font') || '').toString().trim();
            if (!fontClass) {
                return;
            }

            const tooltipId = $(this).attr('aria-describedby');
            if (tooltipId) {
                $(`#${tooltipId}`).find('.tooltip-inner').addClass(fontClass);
            }
        });
    }

    $(document).ready(initializePage);
})();
