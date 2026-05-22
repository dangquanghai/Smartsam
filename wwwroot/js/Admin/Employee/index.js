$(function () {
    // 1. Khởi tạo các biến toàn cục lấy từ View
    const defaultPageSize = parseInt($('#DefaultPageSize').val()) || 25;
    let currentPage = 1;

    // 2. Sự kiện nút Search
    $('form').on('submit', function (e) {
        e.preventDefault();
        currentPage = 1; // Reset về trang 1 khi search mới
        loadEmployeeData();
    });

    // 3. Load dữ liệu mặc định khi vừa vào trang
    loadEmployeeData();

    // --- HÀM CHÍNH: LOAD DATA QUA AJAX ---
    function loadEmployeeData() {
        const formData = {
            // Đảm bảo nếu rỗng thì gửi null thay vì chuỗi rỗng ""
            EmpCode: $('#Filter_EmpCode').val() || null,
            EmpName: $('#Filter_EmpName').val() || null,
            DepartmentId: $('#Filter_DepartmentId').val() ? parseInt($('#Filter_DepartmentId').val()) : null,
            IsActive: $('#chkActive').is(':checked'),
            IsHeadDept: $('#chkHead').is(':checked'),
            Page: parseInt(currentPage) || 1,
            PageSize: parseInt(defaultPageSize) || 25
        };

        $.ajax({
            url: '?handler=Search',
            type: 'POST',
            contentType: 'application/json',
            headers: { "RequestVerificationToken": $('input[name="__RequestVerificationToken"]').val() },
            data: JSON.stringify(formData),
            success: function (res) {
                if (res.success) {
                    renderTable(res.data);
                    renderPagination(res.total, res.page, res.pageSize);
                    $('#total-records-badge').text(res.total + ' records');
                } else {
                    toastr.error(res.message);
                }
            }
        });
    }

    // --- HÀM VẼ BẢNG (RENDER TABLE) ---
    function renderTable(items) {
        const $tbody = $('#employee-table-body');
        $tbody.empty();

        if (items.length === 0) {
            $tbody.append('<tr><td colspan="6" class="text-center">No data found</td></tr>');
            return;
        }

        items.forEach((item, index) => {
            const emp = item.data;
            const acts = item.actions;

            // 1. Biện luận Link cho EmpCode: Ưu tiên Edit -> View -> Text
            let empCodeDisplay = emp.empCode;

            if (acts.canEdit) {
                // Nếu có quyền Edit (mã 4), mặc định mở chế độ edit
                const editUrl = `/Admin/Employee/EmployeeDetail?id=${emp.employeeID}&mode=edit`;
                empCodeDisplay = `<a href="${editUrl}" class="text-bold text-primary" title="Edit Employee">${emp.empCode}</a>`;
            }
            else if (acts.canView) {
                // Nếu không có quyền Edit nhưng có quyền View (mã 2), mở chế độ view
                const viewUrl = `/Admin/Employee/EmployeeDetail?id=${emp.employeeID}&mode=view`;
                empCodeDisplay = `<a href="${viewUrl}" class="text-bold text-info" title="View Detail">${emp.empCode}</a>`;
            }

            // 2. Build dòng TR (giữ nguyên các phần khác)
            const row = `
        <tr class="employee-row" data-id="${emp.employeeID}" data-acts='${JSON.stringify(acts)}'>
            <td><input type="radio" name="selectedEmp" value="${emp.employeeID}"></td>
            <td>${empCodeDisplay}</td>
            <td>${emp.empName}</td>
            <td>${emp.departmentName}</td>
            <td class="text-center">
                ${emp.headDept ? '<span class="badge badge-warning"><i class="fas fa-star"></i> Head</span>' : ''}
            </td>
            <td class="text-center">${emp.isSystem ? '<i class="fas fa-check text-primary"></i>' : ''}</td>
            <td class="text-center">
                <span class="badge ${emp.isActive ? 'badge-success' : 'badge-danger'}">
                    ${emp.isActive ? 'Active' : 'Inactive'}
                </span>
            </td>
        </tr>`;
            $tbody.append(row);
        });

        // Sự kiện Click dòng để hiện/ẩn nút Action
        $('.employee-row').on('click', function () {
            $(this).find('input[type="radio"]').prop('checked', true);
            const acts = $(this).data('acts');
            updateActionButtons(acts);
        });
    }

    // --- HÀM CẬP NHẬT NÚT BẤM DỰA TRÊN QUYỀN (ACTIONS) ---
    function updateActionButtons(acts) {
        // Hiện nút Edit nếu có quyền canEdit (mã 4)
        if (acts.canEdit) $('#btnEdit').removeClass('d-none'); else $('#btnEdit').addClass('d-none');

        // Hiện nút Delete nếu có quyền canDelete (mã 6)
        if (acts.canDelete) $('#btnDelete').removeClass('d-none'); else $('#btnDelete').addClass('d-none');

        // Thêm các nút khác tương tự...
    }

    // --- HÀM PHÂN TRANG (PAGINATION) ---
    function renderPagination(total, page, pageSize) {
        const totalPages = Math.ceil(total / pageSize);
        const $pager = $('#pagination');
        $pager.empty();

        if (totalPages <= 1) return;

        for (let i = 1; i <= totalPages; i++) {
            const activeClass = i === page ? 'active' : '';
            $pager.append(`<li class="page-item ${activeClass}"><a class="page-link" href="javascript:void(0)" onclick="changePage(${i})">${i}</a></li>`);
        }
    }

    // Window function để gọi từ HTML
    window.changePage = function (page) {
        currentPage = page;
        loadEmployeeData();
    };
});