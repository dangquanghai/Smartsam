(function () {
    'use strict';

    const CONFIG = {
        pageSize: typeof defaultPageSize !== 'undefined' ? defaultPageSize : 13,
        selectors: {
            tbody: 'table tbody',
            pagination: '#pagination',
            actionBtns: '#btnEditMember, #btnCancel, #btnChangeStatus, #btnAdjustDate, #btnCopy, #btnDeposit, #btnCheckIn, #btnCheckOut'
        }
    };

    let state = {
        selectedContractId: null,
        currentPage: 1,
        currentDataRows: []
    };

    function performSearch(page = 1) {
        state.currentPage = page;
        const token = $('input[name="__RequestVerificationToken"]').val();

        const filter = {
            statusID: $('#Filter_StatusID').val() ? parseInt($('#Filter_StatusID').val()) : null,
            apartmentId: $('#Filter_ApartmentId').val() ? parseInt($('#Filter_ApartmentId').val()) : null,
            dateRangeIn: $('#DateRangeIn').val() || null,
            dateRangeOut: $('#DateRangeOut').val() || null,
            companyId: $('#CompanyId').val() ? parseInt($('#CompanyId').val()) : null,
            agentCompanyId: $('#AgentCompanyId').val() ? parseInt($('#AgentCompanyId').val()) : null,
            contractNo: $('#Filter_ContractNo').val() || null,
            customerName: $('#Filter_CustomerName').val() || null,
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
                    renderContracts(response.data);
                    updatePagination(response.total, response.page, response.pageSize, response.totalPages);
                    resetActions();
                } else {
                    showError('Tìm kiếm thất bại: ' + response.message);
                }
            },
            error: (xhr, status, error) => showError('Lỗi kết nối hệ thống: ' + error),
            complete: () => showLoading(false)
        });
    }

    function renderContracts(items) {
        const $tbody = $(CONFIG.selectors.tbody);
        $tbody.empty();

        if (!items || items.length === 0) {
            $tbody.append('<tr><td colspan="8" class="text-center py-4">Không tìm thấy hợp đồng nào</td></tr>');
            return;
        }

        const rows = items.map((item, index) => {
            const c = item.data;
            return `
            <tr data-index="${index}" class="contract-row">
                <td><input type="radio" name="selectedContract" value="${index}"></td>
                <td style="white-space:nowrap">
                    <a href="javascript:void(0)" class="contract-link text-primary font-weight-bold" style="text-decoration:underline">
                        ${c.contractNo}
                    </a>
                </td>
                <td>${c.apartmentNo || ''}</td>
                <td style="white-space:nowrap">${c.customerName || ''}</td>
                <td>${c.companyName || ''}</td>
                <td>${c.contractFromDateDisplay || ''}</td>
                <td>${c.contractToDateDisplay || ''}</td>
                <td><span class="badge badge-info">${c.statusName}</span></td>
            </tr>`;
        });
        $tbody.html(rows.join(''));
    }

    function initEvents() {

        // --- A. CLICK DÒNG (Đã tối ưu thêm loại trừ) ---
        $(document).on('click', '.contract-row', function (e) {
            // Loại trừ: nút xóa select2, vùng select2, link, các ô input ngày, các nút clear của daterange
            const isControl = $(e.target).closest('.btn-clear-select2, .select2-container, .contract-link, input, button, .cancelBtn, .applyBtn').length > 0;
            if (isControl) return;

            const $radio = $(this).find('input[name="selectedContract"]');
            if (!$radio.is(':checked')) {
                $radio.prop('checked', true).trigger('change');
            }
        });

        // --- B. XỬ LÝ NÚT XÓA SELECT2 & DATERANGE ---
        $(document).on('click', '.btn-clear-select2', function (e) {
            e.preventDefault();
            e.stopPropagation();

            const targetSelector = $(this).data('target');
            const $el = $(targetSelector);

            if ($el.length) {
                // Xử lý cho cả Select2 và Input thường (như DateRange)
                $el.val(null).trigger('change');
                if ($el.hasClass('select2-hidden-accessible')) {
                    $el.val(null).trigger('change');
                }
            }
        });

        // --- C. XỬ LÝ QUYỀN KHI CHỌN RADIO ---
        $(document).on("change", "input[name='selectedContract']", function (e) {
            e.stopPropagation(); // Tránh bị nổi bọt lên TR
            const index = $(this).val();
            const item = state.currentDataRows[index];
            if (!item) return;

            state.selectedContractId = item.data.contractID;
            const perms = item.actions;

            const toggle = (selector, hasPerm) => $(selector).toggleClass('d-none', !hasPerm);

            toggle("#btnEditMember", perms.canEditMember);
            toggle("#btnCancel", perms.canCancel);
            toggle("#btnChangeStatus", perms.canChangeStatus);
            toggle("#btnAdjustDate", perms.canAdjustDate);
            toggle("#btnCopy", perms.canCopy);
            toggle("#btnDeposit", perms.canCreateDeposit);
            toggle("#btnCheckIn", perms.canCheckIn);
            toggle("#btnCheckOut", perms.canCheckOut);

            updateChangeStatusUI(item.data.statusID);
        });

        // --- D. LINK CHI TIẾT ---
        $(document).on('click', '.contract-link', function (e) {
            e.preventDefault();
            e.stopPropagation(); // Quan trọng
            const index = $(this).closest('tr').data('index');
            const item = state.currentDataRows[index];

            if (item?.actions.canAccess) {
                const mode = item.actions.accessMode || 'view';
                window.location.href = `/Sales/STContract/STContractDetail?id=${item.data.contractID}&mode=${mode}`;
            } else {
                alert("Bạn không có quyền xem chi tiết hợp đồng này.");
            }
        });

        // --- E. PHÂN TRANG ---
        $(document).on('click', '#pagination a.page-link', function (e) {
            e.preventDefault();
            const p = $(this).data('page');
            if (p) performSearch(p);
        });

        // --- F. TOOLBAR NÚT BẤM ---
        $('#btnAdd').on('click', () => window.location.href = '/Sales/STContract/STContractDetail?mode=add');

        $('#btnCopy, #btnDeposit').on('click', function () {
            if (!state.selectedContractId) return alert("Vui lòng chọn một hợp đồng!");
            const action = this.id === 'btnCopy' ? 'Create?copyFromId=' : 'Deposit?id=';
            window.location.href = `/Sales/STContract/${action}${state.selectedContractId}`;
        });

        // --- G. FORM SEARCH ---
        $('form').on('submit', function (e) {
            e.preventDefault();
            performSearch(1);
        });
    }

    function updateChangeStatusUI(statusId) {
        const $btn = $("#btnChangeStatus");
        if (!$btn.length) return;

        $btn.removeClass('btn-dark btn-success btn-warning');

        const statusMap = {
            1: { txt: 'To Living', icon: 'fa-play', cls: 'btn-dark' },
            2: { txt: 'Back to Reser', icon: 'fa-backward', cls: 'btn-warning' },
            4: { txt: 'Restore', icon: 'fa-undo', cls: 'btn-success' },
            9: { txt: 'Restore', icon: 'fa-undo', cls: 'btn-success' }
        };

        const config = statusMap[parseInt(statusId)] || { txt: 'Change Status', icon: 'fa-exchange-alt', cls: 'btn-dark' };
        $btn.html(`<i class="fas ${config.icon}"></i> ${config.txt}`).addClass(config.cls);
    }

    function resetActions() {
        state.selectedContractId = null;
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
        $('#pagination-info').html(`<small>Hiển thị ${start}-${end} / ${total}</small>`);

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

    function showLoading(show) {
        if (show) $(CONFIG.selectors.tbody).html('<tr><td colspan="8" class="text-center py-4"><div class="spinner-border spinner-border-sm text-primary"></div></td></tr>');
    }

    function showError(m) {
        $(CONFIG.selectors.tbody).html(`<tr><td colspan="8" class="text-center text-danger py-4">${m}</td></tr>`);
    }

    $(document).ready(function () {
        if (typeof window.initSelect2 === 'function') {
            window.initSelect2('#CompanyId', 'company');
            window.initSelect2('#AgentCompanyId', 'company');
        }
        initEvents();
        performSearch(1);
    });

})();