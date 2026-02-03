(function () {
    'use strict';

    let pageSize = typeof defaultPageSize !== 'undefined' ? defaultPageSize : 12;
    let selectedContractId = null;
    let currentPage = 1;
    let currentDataRows = []; // Lưu trữ dữ liệu và quyền của trang hiện tại

    // ========== SEARCH FUNCTION ==========
    function performSearch(page = 1) {
        currentPage = page;
        const token = $('input[name="__RequestVerificationToken"]').val();

        const filter = {
            statusID: $('#Filter_StatusID').val() ? parseInt($('#Filter_StatusID').val()) : null,
            apartmentId: $('#Filter_ApartmentId').val() ? parseInt($('#Filter_ApartmentId').val()) : null,
            
            dateRangeIn: $('#DateRangeIn').val() || null,  // Chuỗi "dd/MM/yyyy - dd/MM/yyyy"
            dateRangeOut: $('#DateRangeOut').val() || null, // Chuỗi "dd/MM/yyyy - dd/MM/yyyy"

            companyId: $('#CompanyId').val() ? parseInt($('#CompanyId').val()) : null,
            agentCompanyId: $('#AgentCompanyId').val() ? parseInt($('#AgentCompanyId').val()) : null,
            contractNo: $('#Filter_ContractNo').val() || null,
            customerName: $('#Filter_CustomerName').val() || null,
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
                if (response.success) {
                    currentDataRows = response.data; // Lưu dữ liệu kèm actions
                    renderContracts(response.data);
                    // Cập nhật phân trang với đầy đủ 4 tham số từ Server
                    updatePagination(response.total, response.page, response.pageSize, response.totalPages);
                    resetActions();
                } else {
                    showError('Search failed: ' + response.message);
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

    // ========== RENDER TABLE ==========
    function renderContracts(items) {
        const tbody = $('table tbody');
        tbody.empty();

        if (!items || items.length === 0) {
            tbody.append('<tr><td colspan="8" class="text-center py-4">No contracts found</td></tr>');
            return;
        }

        items.forEach(function (item, index) {
            // item lúc này có cấu trúc { data: {...}, actions: {...} } từ server gởi xuống
            const c = item.data;
            const row = `
                <tr data-id="${c.contractID}" data-index="${index}" style="cursor:pointer">
                    <td><input type="radio" name="selectedContract" value="${index}"></td>
                    <td style="white-space:nowrap">${c.contractNo}</td>
                    <td>${c.apartmentNo || ''}</td>
                    <td style="white-space:nowrap">${c.customerName || ''}</td>
                    <td>${c.companyName || ''}</td>
                    <td>${c.contractFromDateDisplay || ''}</td>
                    <td>${c.contractToDateDisplay || ''}</td>
                    <td>
                        <span class="badge badge-info">${c.statusName}</span>
                    </td>
                </tr>`;
            tbody.append(row);
        });

        // Event click vào dòng để chọn Radio
        tbody.find('tr').on('click', function () {
            const idx = $(this).find('input[type="radio"]').val();
            $(this).find('input').prop('checked', true).trigger('change');
            $('tr').removeClass('selected');
            $(this).addClass('selected');
        });
    }

    // ========== BẮT SỰ KIỆN THAY ĐỔI RADIO (QUAN TRỌNG NHẤT) ==========
    $(document).on("change", "input[name='selectedContract']", function () {
        const index = $(this).val();
        const item = currentDataRows[index];

        // --- ĐOẠN DEBUG QUAN TRỌNG ---
        console.log("--- DEBUG ROW SELECTION ---");
        console.log("Contract No:", item.data.contractNo);
        console.log("Status ID:", item.data.statusID);
        console.log("Permissions received from Server:");
        console.table(item.actions); // Hiển thị bảng quyền: canEdit, canCancel, canGenBill...
        // ---

        selectedContractId = item.data.contractID;
        const perms = item.actions;

        // Bật/Tắt nút bấm dựa trên logic quyền + trạng thái đã tính ở Server
        $("#btnView").prop("disabled", !perms.canView);
        $("#btnEdit").prop("disabled", !perms.canEdit);
        $("#btnCancel").prop("disabled", !perms.canCancel);
        $("#btnCopy").prop("disabled", !perms.canCopy);
        $("#btnToLiving").prop("disabled", !perms.canToLiving);
        $("#btnCopy").prop("disabled", false); // Copy thường cho phép mọi trạng thái
    });

    function resetActions() {
        selectedContractId = null;
        $('#btnView, #btnEdit, #btnCopy, #btnCancel, #btnGenBill').prop('disabled', true);
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
        // Nút Back
        html += `<li class="page-item ${page <= 1 ? 'disabled' : ''}">
                    <a class="page-link" href="#" data-page="${page - 1}">&laquo;</a>
                 </li>`;

        // Vẽ các nút số (giới hạn hiển thị 5 trang xung quanh trang hiện tại)
        for (let i = 1; i <= totalPages; i++) {
            if (i === 1 || i === totalPages || (i >= page - 2 && i <= page + 2)) {
                html += `<li class="page-item ${i === page ? 'active' : ''}">
                            <a class="page-link" href="#" data-page="${i}">${i}</a>
                         </li>`;
            } else if (i === page - 3 || i === page + 3) {
                html += `<li class="page-item disabled"><span class="page-link">...</span></li>`;
            }
        }

        // Nút Next
        html += `<li class="page-item ${page >= totalPages ? 'disabled' : ''}">
                    <a class="page-link" href="#" data-page="${page + 1}">&raquo;</a>
                 </li>`;

        $pagination.html(html).show();

        // Event click phân trang
        $pagination.find('a.page-link').click(function (e) {
            e.preventDefault();
            const p = $(this).data('page');
            if (p) performSearch(p);
        });
    }

    // ========== INITIALIZE ==========
    function initializePage() {
        console.log('Initializing page components...');
        
        // 1. SELECT2 CHO COMPANY 
        window.initSelect2('#CompanyId', 'company');
        window.initSelect2('#AgentCompanyId', 'company');

        // 2. Đăng ký sự kiện nút bấm
        $('#btnAdd').off('click').on('click', () => window.location.href = '/Sales/STContract/Add');
        $('#btnEdit').off('click').on('click', () => window.location.href = `/Sales/STContract/Detail?id=${selectedContractId}&mode=edit`);
        $('#btnView').off('click').on('click', () => window.location.href = `/Sales/STContract/Detail?id=${selectedContractId}`);
        $('#btnCopy').off('click').on('click', () => window.location.href = `/Sales/STContract/Create?copyFromId=${selectedContractId}`);

        // 3. Xử lý Form Search
        $('form').off('submit').on('submit', function (e) {
            e.preventDefault();
            performSearch(1);
        });

        // 4. Tự động Search lần đầu khi load trang
        performSearch(1);
    }
    function showLoading(show) {
        if (show) $('table tbody').html('<tr><td colspan="8" class="text-center py-4"><div class="spinner-border spinner-border-sm"></div> Loading...</td></tr>');
    }

    function showError(m) {
        $('table tbody').html(`<tr><td colspan="8" class="text-center text-danger py-4">${m}</td></tr>`);
    }

    $(document).ready(initializePage);
})();