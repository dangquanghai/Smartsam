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
            const c = item.data;
            // Cột Contract No luôn là link màu xanh, có gạch chân
            const row = `
            <tr data-id="${c.contractID}" data-index="${index}" style="cursor:pointer">
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
            tbody.append(row);
        });
    }
    // Sử dụng Delegation để bắt sự kiện cho các link được tạo động
    $(document).on('click', '.contract-link', function (e) {
        e.preventDefault();
        e.stopPropagation();

        const rowIndex = $(this).closest('tr').data('index');
        const item = currentDataRows[rowIndex];

        if (!item || !item.actions) {
            console.error("This contract does not exist !");
            return;
        }

        const perms = item.actions;
        const contractId = item.data.contractID || item.data.ContractID;

        // --- LOGIC DỰA TRÊN DỮ LIỆU THỰC TẾ CỦA BẠN ---
        if (perms.canAccess === true) {
            // Lấy mode từ Server (edit hoặc view), nếu không có thì mặc định là view
            const mode = perms.accessMode || 'view';

            window.location.href = `/Sales/STContract/STContractDetail?id=${contractId}&mode=${mode}`;
        } else {
            // Trường hợp canAccess là false
            alert("You have no right to view this contract.");
        }
    });

    // ========== BẮT SỰ KIỆN THAY ĐỔI RADIO (QUAN TRỌNG NHẤT) ==========
    // ========== BẮT SỰ KIỆN THAY ĐỔI RADIO (CẬP NHẬT GIAI ĐOẠN 3) ==========
    $(document).on("change", "input[name='selectedContract']", function () {
        const index = $(this).val();
        const item = currentDataRows[index];

        // --- DEBUG ĐỂ KIỂM TRA QUYỀN THỰC TẾ ---
        console.log("--- DEBUG ROW SELECTION (ST CONTRACT) ---");
        console.log("Contract No:", item.data.contractNo);
        console.log("Status ID:", item.data.statusID);
        console.table(item.actions);

        // Lưu ID hợp đồng đang chọn
        selectedContractId = item.data.contractID;
        const perms = item.actions;

        // 1. CẬP NHẬT TRẠNG THÁI ENABLE/DISABLE CÁC NÚT
        // Nút Edit/View đã bỏ vì dùng link ở Contract No, ta tập trung vào các nút nghiệp vụ:

        $("#btnEditMember").prop("disabled", !perms.canEditMember); // Mã 5
        $("#btnCancel").prop("disabled", !perms.canCancel);         // Mã 6
        $("#btnChangeStatus").prop("disabled", !perms.canChangeStatus); // Mã 7
        $("#btnAdjustDate").prop("disabled", !perms.canAdjustDate);     // Mã 8
        $("#btnCopy").prop("disabled", !perms.canCopy);             // Mã 9

        // 2. CẬP NHẬT GIAO DIỆN NÚT CHANGE STATUS (Tùy chọn giúp User dễ hiểu)
        updateChangeStatusUI(item.data.statusID);
    });

    // Hàm bổ trợ để đổi màu/icon cho nút Change Status tùy theo trạng thái
    function updateChangeStatusUI(statusId) {
        const $btn = $("#btnChangeStatus");

        // Reset class về mặc định trước khi add mới
        $btn.removeClass('btn-dark btn-success btn-warning');

        switch (parseInt(statusId)) {
            case 1: // Reser -> To Living
                $btn.html('<i class="fas fa-play"></i> To Living').addClass('btn-dark');
                break;
            case 2: // Living -> Back to Reser
                $btn.html('<i class="fas fa-backward"></i> Back to Reser').addClass('btn-warning');
                break;
            case 4: // Cancelled -> Restore
            case 9: // Exception -> Restore
                $btn.html('<i class="fas fa-undo"></i> Restore to Reser').addClass('btn-success');
                break;
            default:
                $btn.html('<i class="fas fa-exchange-alt"></i> Change Status').addClass('btn-dark');
                break;
        }
    }

    function resetActions() {
        // 1. Xóa ID hợp đồng đang chọn
        selectedContractId = null;

        // 2. Khóa tất cả các nút chức năng nghiệp vụ
        // Chúng ta gom nhóm các ID mới: Member, Cancel, Status, Adjust, Copy
        $('#btnEditMember, #btnCancel, #btnChangeStatus, #btnAdjustDate, #btnCopy').prop('disabled', true);

        // 3. (Tùy chọn) Reset lại Text và Màu sắc của nút Change Status về mặc định
        const $btnStatus = $("#btnChangeStatus");
        $btnStatus.html('<i class="fas fa-exchange-alt"></i> Change Status')
            .removeClass('btn-success btn-warning')
            .addClass('btn-dark');

        console.log("Actions have been reset.");
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
        $('#btnAdd').off('click').on('click', () => window.location.href = '/Sales/STContract/STContractDetail?mode=add');
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