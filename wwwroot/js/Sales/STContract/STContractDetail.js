
var MyScan = null; // Khai báo dùng chung toàn file
$(document).ready(function () {
    // 1. Lấy Mode và ID từ hệ thống
    const urlParams = new URLSearchParams(window.location.search);
    const mode = urlParams.get('mode')?.toLowerCase() || 'add';
    // KHÔNG khai báo 'const MyScan' ở đây nữa để tránh bị giới hạn phạm vi

    // Chỉ thêm đoạn này để khởi tạo đối tượng khi trang sẵn sàng
    if (typeof WebFxScan !== 'undefined') {
        MyScan = new WebFxScan();
        initScanner(); // Gọi hàm connect
    }

    // 2. Chạy khởi tạo trang
    initializePage(mode);

  
        // 3. Xử lý sự kiện SUBMIT Form chính
    $('form').on('submit', async function (e) {
        if (mode === 'view') return true;

        e.preventDefault(); // Tạm dừng để validate

        if (validateMainForm()) {
            const isAvailable = await checkDoubleBooking();
            if (isAvailable) {
                $(this).off('submit').submit(); // OK hết thì submit thực sự
            }
        }
    });


     $('#CurrentRentRate').on('input', function () {
        let selection = window.getSelection().toString();
        if (selection !== '') return;

        let val = $(this).val().replace(/[^0-9.]/g, '');
        if (val === "") return;

        // Tạm thời lưu vị trí con trỏ để không bị nhảy khi gõ
        let n = parseFloat(val);
        $(this).val(n.toLocaleString('en-US'));

        // Gọi hàm tính Net Price luôn
        calculateNetPrice();
    });
    // 5. Đồng bộ DateRangePicker với thẻ Hidden
    $('#contractDuration').on('apply.daterangepicker', function (ev, picker) {
        $('#ContractFromDate').val(picker.startDate.format('YYYY-MM-DD'));
        $('#ContractToDate').val(picker.endDate.format('YYYY-MM-DD'));
    });
});

/* ==========================================================================
   CÁC HÀM KHỞI TẠO VÀ VALIDATION (Nội bộ)
   ========================================================================== */
function initializePage(mode) {

 
    // Khởi tạo Select2
    if (typeof window.initSelect2 === 'function') {
        window.initSelect2('#CompanyId', 'company');
        window.initSelect2('#AgentCompanyId', 'company');
    }

    // Khởi tạo Daterangepicker
    var start = $('#ContractFromDate').val();
    var end = $('#ContractToDate').val();
    if (start && end) {
        $('#contractDuration').daterangepicker({
            startDate: moment(start),
            endDate: moment(end),
            linkedCalendars: false,
            locale: { format: 'DD/MM/YYYY' }
        }, function (start, end) {
            $('#ContractFromDate').val(start.format('YYYY-MM-DD'));
            $('#ContractToDate').val(end.format('YYYY-MM-DD'));
        });
        $('#contractDuration').val(moment(start).format('DD/MM/YYYY') + ' - ' + moment(end).format('DD/MM/YYYY'));
    }

    // Load Agent Persons nếu có sẵn công ty
    var existingCompanyId = $('#AgentCompanyId').val();
    var savedPersonId = $('#Contract_AgentPersonId').val();
    if (existingCompanyId && existingCompanyId !== "0") {
        fetchAgentPersons(existingCompanyId, savedPersonId);
    }

    // Sự kiện thay đổi Agent Company
    $('#AgentCompanyId').on('select2:select change', function () {
        fetchAgentPersons($(this).val(), null);
    });

    // Chế độ xem chi tiết
    if (mode === 'view') {
        $('input, select, textarea').prop('disabled', true);
        $('#btnSave, .btn-primary, .btn-success').hide();
        $('#contractDuration').addClass('bg-light').css('cursor', 'not-allowed');

        // Khóa click vào các radio chọn service trong table
        $(document).on('click', 'input[name="selectedService"]', function (e) {
            e.preventDefault();
            return false;
        });
    }

    // Tự động load Grid dịch vụ ở Vùng 2 nếu là Edit mode
    loadServiceGrid();

   
}

function validateMainForm() {
    const fields = [
        { id: 'ContractDate', name: 'Contract Date' },
        { id: 'ApmtId', name: 'Apartment' },
        { id: 'CurrentRentRate', name: 'Rent Inc VAT' },
        { id: 'contractDuration', name: 'Period' },
        { id: 'ContractSourceID', name: 'Source' },
        { id: 'ReceivedByID', name: 'Received By' },
        { id: 'PaymentByID', name: 'Payment By' }
    ];

    for (let field of fields) {
        let $el = $('#' + field.id);
        if (!$el.val() || $el.val().toString().trim() === "" || $el.val() === "0") {
            alert("Please enter/select: " + field.name);
            focusErrorField($el);
            return false;
        }
    }

    // Check ngày From <= To
    if (new Date($('#ContractFromDate').val()) > new Date($('#ContractToDate').val())) {
        alert("Error: 'From Date' must be less than or equal to 'To Date'.");
        return false;
    }
    return true;
}

function focusErrorField($el) {
    let $tabPane = $el.closest('.tab-pane');
    if ($tabPane.length > 0 && !$tabPane.hasClass('active')) {
        $('.nav-tabs a[href="#' + $tabPane.attr('id') + '"]').tab('show');
    }
    setTimeout(() => $el.focus(), 300);
}

/* ==========================================================================
   CÁC HÀM AJAX VÀ LOGIC NGHIỆP VỤ (Global Scope - HTML gọi được)
   ========================================================================== */

async function checkDoubleBooking() {
    let apartmentNo = $('#ApmtId').hasClass('select2-hidden-accessible')
        ? $('#ApmtId').select2('data')[0]?.text
        : $('#ApmtId option:selected').text();

    const fromDate = $('#ContractFromDate').val();
    const toDate = $('#ContractToDate').val();
    const contractId = $('#ContractId').val() || 0;

    if (!apartmentNo || !fromDate || !toDate) return false;

    try {
        const response = await $.ajax({
            url: '?handler=CheckApmtAvail',
            type: 'GET',
            data: { apartmentNo, fromDate, toDate, contractId }
        });

        const status = parseInt(response.status);
        if (status === 0) {
            alert(`Apartment ${apartmentNo} is occupied!`);
            return false;
        } else if (status === 2) {
            const ovlMsg = prompt("Overlap 1 day detected. Please enter reason:");
            if (!ovlMsg) return false;
            $('#OverlapNotes').val(ovlMsg);
        }
        return true;
    } catch (e) {
        alert("Availability check failed.");
        return false;
    }
}

function fetchAgentPersons(companyId, selectedPersonId) {
    var $personSelect = $('#AgentPersonId');
    $personSelect.empty().append('<option value="">-- Select Person --</option>');
    if (!companyId || companyId === "0") return;

    $.get('?handler=AgentPersons', { companyId }, function (data) {
        if (data) {
            $.each(data, function (i, item) {
                var val = item.Value || item.value;
                var txt = item.Text || item.text;
                $personSelect.append(new Option(txt, val, false, val == selectedPersonId));
            });
            $personSelect.trigger('change');
        }
    });
}

function loadHistory(id) {
    console.log("Loading history for ID:", id); // Kiểm tra xem hàm có chạy không

    if (!id || id == 0) {
        alert("Vui lòng lưu hợp đồng trước khi xem lịch sử.");
        return;
    }

    $.ajax({
        type: "GET",
        url: window.location.pathname, // Lấy đường dẫn hiện tại của Page
        data: {
            handler: "History",
            contractId: id
        },
        success: function (data) {
            console.log("Data received:", data); // Kiểm tra dữ liệu trả về

            if (data.length === 0) {
                alert("Không có lịch sử cho hợp đồng này.");
                return;
            }

            var html = '';
            $.each(data, function (i, item) {
                // Lưu ý: C# trả về Json thì các field thường bị biến thành chữ thường (camelCase)
                // e.g: EmployeeName -> employeeName
                var empName = item.employeeName || item.EmployeeName || "N/A";
                var desc = item.description || item.Description || "";
                var time = item.recordTime || item.RecordTime || "";

                html += '<tr>';
                html += '<td>' + time + '</td>';
                html += '<td>' + empName + '</td>';
                html += '<td>' + desc + '</td>';
                html += '</tr>';
            });

            $('#historyTable tbody').html(html);
            $('#historyModal').modal('show');
        },
        error: function (xhr, status, error) {
            console.error("AJAX Error:", status, error);
            alert("Lỗi khi tải lịch sử: " + error);
        }
    });
}
// 1. Hàm mở Modal khi click nút "Add From List"
function openAddFromList() {
    $('#modalSearchTenant').modal('show');
    setTimeout(() => $('#txtSearchName').focus(), 500);
}

// 2. Hàm tìm kiếm Tenant

function searchTenant() {
    var name = $('#txtSearchName').val();
    var cid = $('#hfContractID').val(); // Đọc ID hợp đồng hiện tại từ hidden field

    if (name.length < 2) {
        alert("Vui lòng nhập ít nhất 2 ký tự để tìm kiếm.");
        return;
    }

    // Hiển thị trạng thái đang tải
    $('#tbodyResult').html('<tr><td colspan="5" class="text-center"><i class="fas fa-spinner fa-spin"></i> Đang tìm kiếm...</td></tr>');

    $.ajax({
        url: '?handler=SearchTenants',
        type: 'GET',
        data: { name: name, currentId: cid },
        success: function (data) {
            var html = '';

            if (data && data.length > 0) {
                $.each(data, function (i, item) {
                    // 1. Kiểm tra giới tính (Trong SQL là Male, JSON sẽ là male)
                    // Nếu male = true -> Nam, false -> Nữ
                    var genderText = (item.male === true || item.Male === true) ? "Male" : "Female";

                    // 2. Lấy Tên khách hàng (JSON thường là customerName)
                    // Tôi dùng dấu || để "bắt" cả 2 trường hợp Hoa/Thường cho chắc chắn
                    var tId = item.tenantID || item.TenantID || 0;
                    var name = item.customerName || item.CustomerName || "N/A";
                    var passport = item.idPassportNo || item.IDPassportNo || "";
                    var nation = item.nationName || item.NationName || "";
                    var fPos = item.familyPos || item.FamilyPos || 1; // Mặc định là 1 nếu null

                    // 3. Xử lý ngày sinh
                    var bday = "";
                    if (item.birthday || item.Birthday) {
                        var dateObj = new Date(item.birthday || item.Birthday);
                        bday = dateObj.toLocaleDateString('vi-VN');
                    }

                    // 4. Cộng dồn vào chuỗi HTML
                    html += `<tr style="cursor:pointer" ondblclick="saveSelectedTenant(${tId})">
                    <td><span class="badge badge-secondary">${tId}</span></td>
                    <td>${name}</td>
                    <td>${genderText}</td>
                    <td>${passport}</td>
                    <td>${bday}</td>
                    <td>${nation}</td>
                    </tr>`;
                });
            } else {
                html = '<tr><td colspan="5" class="text-center text-danger">Không tìm thấy khách hàng nào phù hợp.</td></tr>';
            }

            // Ghi đè toàn bộ nội dung trong tbody bằng danh sách mới
            $('#tbodyResult').html(html);
        },
        error: function () {
            alert("Cannot connect server .");
            $('#tbodyResult').html('<tr><td colspan="5" class="text-center text-danger">System Error.</td></tr>');
        }
    });
}
// 3. Hàm lưu Tenant được chọn vào table CM_ContractTenant
function saveSelectedTenant(tId, fPos) {
    var cId = $('#hfContractID').val();
    var token = $('input:hidden[name="__RequestVerificationToken"]').val();

    $.ajax({
        url: '?handler=ImportTenant', // Handler là "ImportTenant"
        type: 'POST',
        data: {
            tenantId: tId,    // Khớp với tham số C#
            contractId: cId,  // Khớp với tham số C#
            familyPos: fPos   // Khớp với tham số C#
        },
        beforeSend: function (xhr) {
            // Tên Header bắt buộc phải là "RequestVerificationToken"
            xhr.setRequestHeader("RequestVerificationToken", token);
        },
        success: function (res) {
            if (res.success) {
                // 1. Thông báo thành công
                alert("Add member susccessfully!");

                // 2. Lệnh đóng Popup (Modal)
                // Lưu ý: Kiểm tra xem ID modal của anh là 'modalSearchTenant' hay 'tenantSearchModal'
                $('#modalSearchTenant').modal('hide');

                // 3. Xóa nội dung tìm kiếm cũ để lần sau mở lên là bảng trống
                $('#tbodyResult').html('');
                $('#txtSearchName').val('');

                // 4. Cập nhật lại danh sách ở Vùng 3
                // Nếu anh đã có hàm load danh sách thì gọi nó ở đây
                if (typeof loadTenantsGrid === "function") {
                    loadTenantsGrid();
                } else {
                    // Nếu chưa có hàm load riêng, dùng cách này để nạp lại vùng danh sách
                    // Giả sử vùng 3 của anh nằm trong một div có id="vungDanhSach"
                    // $('#vungDanhSach').load(window.location.href + ' #vungDanhSach');

                    location.reload(); // Cách nhanh nhất nhưng sẽ load lại cả trang
                }
            } else {
                alert("Error: " + res.message);
            }
        },
        error: function (xhr) {
            // Nếu không vào C#, nó sẽ nhảy vào đây. 
            // Anh xem log để biết lỗi 400 (Token) hay 404 (Sai URL)
            console.error("Status:", xhr.status);
            console.error("Response:", xhr.responseText);
        }
    });
}
function calculateNetPrice() {
    let grossStr = $('#CurrentRentRate').val().replace(/[^0-9.]/g, '');
    let gross = parseFloat(grossStr) || 0;
    let vat = parseFloat($('#PerVAT').val()) || 0;

    if (gross > 0) {
        // Công thức: Net = Gross / (1 + VAT%)
        let netPrice = Math.round(gross / (1 + (vat / 100)));
        $('#TotalPriceExcVAT').val(netPrice.toLocaleString('en-US'));
    } else {
        $('#TotalPriceExcVAT').val(0);
    }
}
// --- VÙNG 2: CONTRACT SERVICES (AJAX) ---

function openServiceModal() {
    var contractId = $('#ContractId').val();

    if (!contractId || contractId === "0") {
        alert("Please save the Contract info (Zone 1) first!");
        return;
    }

    // Đóng select2 để tránh lỗi hiển thị đè lên modal
    if ($.fn.select2) {
        $('#CompanyId, #AgentCompanyId').select2('close');
    }

    $('#formService')[0].reset();
    $('#svc_ContractID').val(contractId);

    // Gán ngày mặc định từ hợp đồng
    $('#svc_FromDate').val($('#ContractFromDate').val());
    $('#svc_ToDate').val($('#ContractToDate').val());

    $('#modalService').modal('show');
}

function saveContractService() {
    var serviceObj = {
        ContractID: parseInt($('#svc_ContractID').val()),
        ServiceID: parseInt($('#svc_ServiceID').val()),
        ServiceFromDate: $('#svc_FromDate').val(),
        ServiceToDate: $('#svc_ToDate').val(),
        ChargeInterval: parseInt($('#svc_Interval').val()),
        ChargeType: parseInt($('#svc_Type').val()),
        ChargeAmount: parseFloat($('#svc_ChargeAmount').val().replace(/,/g, '')) || 0, // BỔ SUNG DÒNG NÀY
        MaxQuantity: parseFloat($('#svc_Qty').val()) || 0,
        Notes: $('#svc_Notes').val()
    };

    if (!serviceObj.ServiceID) { alert("Please select a service!"); return; }

    $.ajax({
        url: '?handler=SaveService',
        type: 'POST',
        contentType: 'application/json',
        data: JSON.stringify(serviceObj),
        headers: { "RequestVerificationToken": $('input[name="__RequestVerificationToken"]').val() },
        success: function (res) {
            if (res.success) {
                $('#modalService').modal('hide');
                loadServiceGrid(); // Load lại lưới bằng AJAX
                if (typeof toastr !== 'undefined') toastr.success('Saved successfully!');
            } else {
                alert("Error: " + res.message);
            }
        }
    });
}

function loadServiceGrid() {
    var contractId = $('#Contract_ContractID').val() || $('#ContractId').val();
    if (!contractId || contractId == "0") return;

    $.get('?handler=ContractServices', { contractId: contractId }, function (data) {
        var $tbody = $('#tableServices tbody').empty();

        if (data && data.length > 0) {
            $.each(data, function (i, item) {
                $tbody.append(`<tr>
                    <td class="text-center">
                        <input type="radio" name="selectedService" value="${item.contractServiceID || item.ContractServiceID}" />
                    </td>
                    <td>${item.serviceName || item.ServiceName || ""}</td>
                    <td class="text-center">${item.serviceFromDate ? moment(item.serviceFromDate).format('DD/MM/YYYY') : (item.ServiceFromDate ? moment(item.ServiceFromDate).format('DD/MM/YYYY') : "")}</td>
                    <td class="text-center">${item.serviceToDate ? moment(item.serviceToDate).format('DD/MM/YYYY') : (item.ServiceToDate ? moment(item.ServiceToDate).format('DD/MM/YYYY') : "")}</td>

                    <td class="text-center">${item.chargeIntervalName || item.ChargeIntervalName || ""}</td>
                    <td class="text-center">${item.chargeTypeName || item.ChargeTypeName || ""}</td>
                    <td class="text-right">${new Intl.NumberFormat('en-US').format(item.chargeAmount || item.ChargeAmount || 0)}</td>
                    <td class="text-center">${item.maxQuantity || item.MaxQuantity || 0}</td>
                    <td><small>${item.notes || item.Notes || ""}</small></td>
                </tr>`);
            });
        } else {
            $tbody.append('<tr><td colspan="9" class="text-center text-muted">No services found</td></tr>');
        }
    });
}

function removeSelectedService() {
    var selectedId = $('input[name="selectedService"]:checked').val();

    if (!selectedId) {
        alert("Pls select a service to remove.");
        return;
    }

    if (confirm("Are you sure to remove?")) {
        $.ajax({
            type: "POST",
            url: window.location.pathname + "?handler=DeleteService",
            data: { id: selectedId },
            beforeSend: function (xhr) {
                xhr.setRequestHeader("RequestVerificationToken",
                    $('input:hidden[name="__RequestVerificationToken"]').val());
            },
            success: function (response) {
                if (response.success) {
                    loadServiceGrid(); // Thay vì reload trang, chỉ load lại grid cho mượt
                    if (typeof toastr !== 'undefined') toastr.success(response.message);
                } else {
                    alert(response.message);
                }
            },
            error: function () {
                alert("System connection error.");
            }
        });
    }
}

function loadTenantsGrid() {
    var cId = $('#hfContractID').val();
    if (!cId || cId == 0) return;

    $.ajax({
        url: '?handler=LoadContractTenants',
        type: 'GET',
        data: { contractId: cId },
        success: function (data) {
            var html = '';
            if (data && data.length > 0) {
                $.each(data, function (i, item) {
                    const fDate = (d) => d ? new Date(d).toLocaleDateString('vi-VN') : '';

                    // Lấy ID chính của dòng trong bảng CM_ContractTenant
                    var ctId = item.contractTenantID || item.ContractTenantID;

                    html += `<tr>
                        <td class="text-center">
                            <input type="radio" name="selectedTenant" value="${ctId}" class="rd-tenant">
                        </td>
                        <td>${item.Title || ''}</td>  <td>${item.CustomerName}</td>
                        <td class="text-center">${fDate(item.birthday || item.Birthday)}</td>
                        <td>${item.nationName || item.NationName || ""}</td>
                        <td>${item.idPassportNo || item.IDPassportNo || ""}</td>
                        <td><span class="badge badge-info">${item.positionName || item.PositionName || ""}</span></td>
                        <td class="text-center">${fDate(item.moveinDate || item.MoveinDate)}</td>
                        <td class="text-center text-danger"><b>${fDate(item.proposeExpDate || item.ProposeExpDate)}</b></td>
                        <td>${item.visaNo || item.VisaNo || ""}</td>
                    </tr>`;
                });
            } else {
                html = '<tr><td colspan="9" class="text-center text-muted">No tenants found.</td></tr>';
            }
            $('#tableTenants tbody').html(html);
        }
    });
}
function getSelectedTenantID() {
    var selectedId = $('input[name="selectedTenant"]:checked').val();
    if (!selectedId) {
        alert("Vui lòng chọn một khách hàng từ danh sách.");
        return null;
    }
    return selectedId;
}

// Ví dụ cho nút Remove
$('#btnRemoveTenant').click(function () {
    var id = getSelectedTenantID();
    if (id && confirm("Bạn có chắc chắn muốn xóa khách hàng này khỏi hợp đồng?")) {
        // Gọi AJAX xóa với ID này...
    }
});

$(document).on('click', '#tableTenants tbody tr', function () {
    $(this).find('input[type="radio"]').prop('checked', true);
});
function openTenantModal(mode) {
    // 1. Reset Form và ID về mặc định (0)
    $('#formTenantDetail')[0].reset();
    $('#txtContractTenantID').val(0);
    $('#txtCustomerID').val(0);
    $('#passport_gallery, #police_gallery').empty();

    // Reset các trạng thái khóa/mở input
    $('#modalTenantDetail input, #modalTenantDetail select, #modalTenantDetail textarea').prop('disabled', false).prop('readonly', false);
    $('.upload-zone').show();
    $('#btnSaveTenantDetail').show(); // Đảm bảo nút save luôn hiện trước khi check mode

    var ctId = 0;

    // 2. Xử lý logic theo Mode
    if (mode === 'edit' || mode === 'view') {
        var selectedRadio = $('input[name="selectedTenant"]:checked');
        ctId = selectedRadio.val();

        if (!ctId || ctId == 0) {
            alert("Vui lòng chọn một khách thuê trong danh sách!");
            return;
        }

        // Gọi AJAX lấy dữ liệu cũ
        $.ajax({
            url: '?handler=GetFullTenantDetail',
            type: 'GET',
            data: { contractTenantId: ctId },
            success: function (res) {
                var info = res.info;
                if (!info) return;

                // Hàm phụ để gán ngày an toàn
                const fillDate = (selector, dateVal) => {
                    if (dateVal && typeof dateVal === 'string' && dateVal.includes('T')) {
                        $(selector).val(dateVal.split('T')[0]);
                    } else {
                        $(selector).val('');
                    }
                };

                // Đổ dữ liệu vào Input
                $('#txtContractTenantID').val(info.ContractTenantID || 0);
                $('#txtCustomerID').val(info.TenantID || 0);
                $('#txtTitle').val(info.Title || "");
                $('#txtCustomerName').val(info.CustomerName || "");

                fillDate('#txtBirthday', info.Birthday);
                fillDate('#txtPassportUntilDate', info.PassportUntilDate);
                
                $('#ddlNationality').val(info.Nationality || "").trigger('change');
                $('#txtIDPassportNo').val(info.IDPassportNo || "");
                $('#txtCompany').val(info.Company || "");
                $('#txtVATCode').val(info.VATCode || "");
                $('#txtAddress').val(info.Address || "");

                $('#ddlTenantType').val(info.TenantType || "1");
                $('#ddlFamilyPos').val(info.FamilyPos || 0);
                $('#chkIsMoveOut').prop('checked', info.IsMoveOut === true);
                $('#txtVisaNo').val(info.VisaNo || "");

                
                fillDate('#txtVisaDate', info.VisaDate);
                fillDate('#txtVisaExpDate', info.VisaExpDate);

                fillDate('#txtEntryDate', info.EntryDate);
                fillDate('#txtLastRegDate', info.LastRegDate);
                
                fillDate('#txtPermitExpDate', info.PermitExpDate); // Đã sửa lại đúng biến
                fillDate('#txtProposeExpDate', info.ProposeExpDate); // Đã sửa lại đúng biến

                $('#ddlArrivalPort').val(info.ArrivalPort || 0);
                

                
                // Kiểm tra tất cả các khả năng có thể xảy ra của tên trường
                var adCardValue = info.A_DCardNo || info.a_DCardNo || info.ADCardNo || info.adCardNo || "";

                $('#txtADCardNo').val(adCardValue);


                $('#txtSponsor').val(info.Sponsor || "");
                $('#txtNotes').val(info.Notes || "");

                // Xử lý hình ảnh
                renderGalleries(res.passports, res.police);

                // Xử lý hiển thị theo Mode
                if (mode === 'view') {
                    $('#modalTenantDetailTitle').text("View Tenant Detail");
                    $('#btnSaveTenantDetail').hide();
                    $('#modalTenantDetail input, #modalTenantDetail select, #modalTenantDetail textarea').prop('disabled', true);
                    $('.upload-zone').hide();
                } else {
                    $('#modalTenantDetailTitle').text("Edit Tenant Detail");
                    $('#btnSaveTenantDetail').show();
                    $('#modalTenantDetail input, #modalTenantDetail select, #modalTenantDetail textarea').prop('disabled', false);
                    $('.upload-zone').show();
                }

                $('#modalTenantDetail').modal('show');
            }
        });
    }
    else if (mode === 'add') {
        // Chế độ thêm mới: Không gọi AJAX, chỉ hiện Modal trống
        $('#modalTenantDetailTitle').text("Add New Tenant");
        $('#txtContractTenantID').val(0); // Đảm bảo chắc chắn là 0 để C# chạy lệnh INSERT
        $('.upload-zone').hide(); // Thường ẩn upload ảnh khi chưa có ID khách
        $('#modalTenantDetail').modal('show');
    }
}

async function initScanner() {
    try {
        if (!MyScan) return;
        // Thử kết nối tới Service Local
        await MyScan.connect({ ip: "127.0.0.1", port: "17778" });
        await MyScan.init();
        console.log("✅ Scanner Service connected!");
    } catch (e) {
        console.warn("⚠️ Scanner service not found at port 17778");
    }
}
// Hàm con để tạo HTML cho từng tấm ảnh
function createImgItem(doc) {
    // doc.FilePath là đường dẫn lưu trong DB (vừa tạo ở bảng CM_ContractTenant_Doc)
    return `
        <div class="col-md-4 col-sm-6 mb-3" id="doc_item_${doc.id}">
            <div class="card shadow-sm border">
                <div class="card-body p-1 text-center">
                    <a href="${doc.FilePath}" target="_blank">
                        <img src="${doc.FilePath}" class="img-fluid rounded" style="height:150px; object-fit:contain; width:100%;">
                    </a>
                </div>
                <div class="card-footer p-1 d-flex justify-content-between align-items-center bg-light">
                    <small class="text-muted ml-1">${new Date(doc.UploadDate).toLocaleDateString('vi-VN')}</small>
                    <button type="button" class="btn btn-xs btn-outline-danger" title="Xóa ảnh" onclick="deleteDoc(${doc.id})">
                        <i class="fas fa-trash-alt"></i>
                    </button>
                </div>
            </div>
        </div>`;
}

$('#btnSaveTenantDetail').click(function () {
    // Hàm phụ để kiểm tra nếu chuỗi rỗng thì trả về null cho Server dễ đọc
    const cleanDate = (val) => (val && val.trim() !== "") ? val : null;
    const cleanInt = (val) => {
        var res = parseInt(val);
        return isNaN(res) ? 0 : res;
    };

    var tenantData = {
        // CỰC KỲ QUAN TRỌNG: Phải có ID này để Server biết Update hay Insert
        ContractTenantID: cleanInt($('#txtContractTenantID').val()),
        // Ép kiểu số cho các trường ID/Dropdown
        ContractID: cleanInt($('#hfContractID').val()),
        TenantID: cleanInt($('#txtCustomerID').val()),

        Title: $('#txtTitle').val(),
        CustomerName: $('#txtCustomerName').val(),
        Male: $('#ddlMale').val() === "true",
        Birthday: cleanDate($('#txtBirthday').val()),
        TenantType: cleanInt($('#ddlTenantType').val()),
        Nationality: parseInt($('#ddlNationality').val()) || 0,
        IDPassportNo: $('#txtIDPassportNo').val(),
        PassportUntilDate: cleanDate($('#txtPassportUntilDate').val()),
        Company: $('#txtCompany').val(),
        VATCode: $('#txtVATCode').val(),
        Address: $('#txtAddress').val(),

        FamilyPos: cleanInt($('#ddlFamilyPos').val()),
        IsMoveOut: $('#chkIsMoveOut').is(':checked'),
        VisaNo: $('#txtVisaNo').val(),
        VisaDate: cleanDate($('#txtVisaDate').val()),
        VisaExpDate: cleanDate($('#txtVisaExpDate').val()),
        EntryDate: cleanDate($('#txtEntryDate').val()),
        ArrivalPort: cleanInt($('#ddlArrivalPort').val()),

        PermitExpDate: cleanDate($('#txtPermitExpDate').val()), 
        ProposeExpDate: cleanDate($('#txtProposeExpDate').val()), 
        
        ADCardNo: $('#txtADCardNo').val(),
        LastRegDate: cleanDate($('#txtLastRegDate').val()),
        Sponsor: $('#txtSponsor').val(),
        Notes: $('#txtNotes').val(),
        
    };

    console.log("Data gửi lên Server:", tenantData); // Anh nhấn F12 để xem dữ liệu có chuẩn không

    $.ajax({
        url: '?handler=SaveTenantDetail',
        type: 'POST',
        contentType: 'application/json; charset=utf-8',
        headers: { "RequestVerificationToken": $('input[name="__RequestVerificationToken"]').val() },
        data: JSON.stringify(tenantData),
        success: function (res) {
            if (res.success) {
                alert(res.message);
                $('#modalTenantDetail').modal('hide');
                loadTenantsGrid();
            } else {
                alert("Error: " + res.message);
            }
        },
        error: function (xhr) {
            // Nếu model bị null, nó thường trả về lỗi 400 hoặc 500
            console.error("Lỗi Ajax:", xhr.responseText);
        }
    });
});

let allDocs = []; // Chứa danh sách { FilePath, DocTypeID, ... }
let currentIndex = 0;

function renderGalleries(docs) {
    allDocs = docs || [];
    currentIndex = 0;
    showImage();
}

function showImage() {
    if (allDocs.length > 0) {
        let doc = allDocs[currentIndex];
        $('#current_img_display').attr('src', doc.FilePath).show();
        $('#no_img_overlay').hide();
        $('#ddlDocTypeID').val(doc.DocTypeID); // Tự động cập nhật Select Box theo loại hình
        $('#img_index_label').text(`${currentIndex + 1} / ${allDocs.length}`);
    } else {
        $('#current_img_display').hide();
        $('#no_img_overlay').show();
        $('#img_index_label').text("0 / 0");
    }
}

function moveImg(step) {
    if (allDocs.length === 0) return;
    currentIndex += step;
    if (currentIndex < 0) currentIndex = allDocs.length - 1;
    if (currentIndex >= allDocs.length) currentIndex = 0;
    showImage();
}

// Khi tiếp tân thay đổi Select Box, cập nhật DocTypeID cho ảnh đó
$('#ddlDocTypeID').on('change', function () {
    if (allDocs[currentIndex]) {
        allDocs[currentIndex].DocTypeID = $(this).val();
        // Bạn có thể gọi API cập nhật ngay hoặc lưu tất cả khi nhấn Save
    }
});

// Hàm nhận dạng OCR
function recognizeOCR() {
    if (allDocs.length === 0) return;

    let currentPath = allDocs[currentIndex].FilePath;
    let type = $('#ddlDocTypeID').val();

    // Hiệu ứng loading
    $('#btnReconize').html('<i class="fas fa-spinner fa-spin"></i> Processing...');

    $.post('/Sales/STContract/STContractDetail?handler=ReconizeDocument', {
        filePath: currentPath,
        docType: type
    }, function (res) {
        if (res.success) {
            // Đổ dữ liệu trích xuất vào các trường bên trái
            if (res.data.fullName) $('#txtCustomerName').val(res.data.fullName);
            if (res.data.passportNo) $('#txtIDPassportNo').val(res.data.passportNo);
            if (res.data.birthday) $('#txtBirthday').val(res.data.birthday.split('T')[0]);
            // ... thêm các trường khác
            alert("Trích xuất dữ liệu thành công!");
        } else {
            alert("Không thể nhận dạng hình ảnh này.");
        }
        $('#btnReconize').html('<i class="fas fa-magic"></i> Recognize');
    });
}
/*
async function callWebSDKScan() {
    try {
        if (typeof Swal !== 'undefined') Swal.fire({ title: 'Đang chuẩn bị...', allowOutsideClick: false, didOpen: () => Swal.showLoading() });

        // 1. Lấy danh sách thiết bị
        const res = await MyScan.getDeviceList();
        const devices = res?.data?.options || [];
        if (devices.length === 0) throw new Error("Không tìm thấy máy scan.");

        // Lấy tên máy thực tế (Có thể là "Plustek A62" hoặc "USB Video Device")
        const dName = devices[0].deviceName;

        // 2. Cấu hình CHUẨN (Sử dụng cả 2 định dạng Key để đảm bảo tương thích)
        const config = {
            "device-name": dName,
            "source": "Auto",        // Để driver tự quyết định nguồn
            "paper-size": "Auto",    // Để driver tự nhận khổ giấy
            "mode": "color",
            "resolution": 300,
            "recognize-type": "passport"
        };

        // 3. Khởi tạo
        console.log("Đang mở thiết bị:", dName);
      
        // 1. Gửi lệnh mở máy
        const setRes = await MyScan.setScanner(config);

        if (setRes.result) {
            // 2. Cho máy 1.5s - 2s để "thở" và khởi động motor
            await new Promise(r => setTimeout(r, 2000));

            // 3. Lúc này mới ra lệnh quét
            const scanRes = await MyScan.scan();

            if (scanRes.result) {
                console.log("Quét thành công!");
                // Gọi hàm hiển thị ảnh và dữ liệu ở đây
                displayData(scanRes.data[0]);
            }
        }
        // 5. Gọi lệnh quét (KHÔNG truyền callback vào trong hàm scan)
        console.log("Bắt đầu kéo giấy...");
        const scanRes = await MyScan.scan();


        if (typeof Swal !== 'undefined') Swal.close();

        // 6. Kiểm tra kết quả và xử lý dữ liệu
        if (scanRes && scanRes.result) {
            // Dữ liệu nằm trong scanRes.data (thường là một mảng file)
            const fileList = scanRes.data;

            if (fileList && fileList.length > 0) {
                const file = fileList[0];

                // Đổ ảnh base64
                if (file.base64) {
                    $('#current_img_display').attr('src', "data:image/jpeg;base64," + file.base64).show();
                    $('#no_img_overlay').hide();
                }

                // Xử lý OCR Passport
                if (file.ocrText) {
                    console.log("Dữ liệu Passport thô:", file.ocrText);
                    // Lưu ý: file.ocrText có thể là String JSON, cần parse nếu cần
                    // var ocrData = JSON.parse(file.ocrText);
                }
                alert("Quét thành công!");
            }
        } else {
            // Xử lý lỗi đặc thù mã số 3
            if (scanRes.error == 3) {
                alert("Lỗi 3: Thiết bị chưa sẵn sàng. \nAnh hãy: \n1. Đặt hộ chiếu vào khe máy. \n2. Tắt app DocAction đang chạy ngầm.");
            } else {
                alert("Lỗi: " + scanRes.message);
            }
        }

    } catch (err) {
        if (typeof Swal !== 'undefined') Swal.close();
        alert("Lỗi hệ thống: " + err.message);
    }
}
*/
async function callWebSDKScan() {
    try {
        // 1. Hiển thị Loading (Sử dụng SweetAlert2 như code cũ của anh)
        if (typeof Swal !== 'undefined') {
            Swal.fire({
                title: 'Đang kết nối máy scan...',
                text: 'Vui lòng đặt hộ chiếu sẵn vào khe máy',
                allowOutsideClick: false,
                didOpen: () => Swal.showLoading()
            });
        }

        // 2. Lấy danh sách thiết bị thực tế
        const resList = await MyScan.getDeviceList();
        const devices = resList?.data?.options || [];

        if (devices.length === 0) {
            throw new Error("Không tìm thấy máy scan Plustek A62. Vui lòng kiểm tra cáp USB.");
        }

        const dName = devices[0].deviceName;
        console.log("Đang điều khiển thiết bị:", dName);

        // 3. Cấu hình tối ưu (Sử dụng cả 2 định dạng Key để tránh lỗi Driver)
        const config = {
            "device-name": dName,
            "source": "Auto",       // Để Auto để tránh lỗi kẹt khay giấy
            "paper-size": "Auto",   // Để Auto cho dòng máy Passport
            "recognize-type": "passport",
            "resolution": 300,
            "mode": "color",
            "autoScan": false
        };

        // 4. Gửi lệnh mở thiết bị (Set Scanner)
        const setRes = await MyScan.setScanner(config);
        if (!setRes || !setRes.result) {
            throw new Error("Không thể mở thiết bị. Lỗi: " + (setRes?.message || "Unknown"));
        }

        // 5. KHOẢNG NGHỈ VÀNG: Đợi motor và cảm biến sẵn sàng
        // Anh đã xác nhận 2s là con số ổn định nhất cho dòng A62
        console.log("Đang đợi motor khởi động...");
        await new Promise(r => setTimeout(r, 2000));

        // 6. Thực hiện lệnh quét
        console.log("Máy bắt đầu kéo giấy...");
        const scanRes = await MyScan.scan();
        if (typeof Swal !== 'undefined') Swal.close();

        if (scanRes && scanRes.result) {
            const file = scanRes.data[0];
            if (file && file.base64) {

                // --- ĐOẠN SỬA LỖI ERR_INVALID_URL TẠI ĐÂY ---
                let rawBase64 = file.base64.trim();
                let finalSrc = "";

                // Kiểm tra nếu chuỗi đã có sẵn tiêu đề data:image...
                if (rawBase64.startsWith("data:image")) {
                    finalSrc = rawBase64; // Dùng luôn
                } else {
                    finalSrc = "data:image/jpeg;base64," + rawBase64; // Chỉ thêm nếu chưa có
                }

                const imgElement = $('#current_img_display');
                imgElement.attr('src', finalSrc);
                imgElement.show();
                $('#no_img_overlay').hide();
                // --------------------------------------------

                console.log("Đã hiển thị ảnh thành công.");

                // Đổ dữ liệu vào Form (nếu có)
                if (file.ocrText) {
                    console.log("Dữ liệu Passport nhận được:", file.ocrText);

                    // Họ và tên (Ghép Familyname và Givenname)
                    const fullName = (ocr.Familyname + " " + ocr.Givenname).trim();
                    $('#txtFullName').val(fullName); // Giả sử ID ô Họ tên là txtFullName

                    // Số hộ chiếu
                    $('#txtPassportNo').val(ocr.DocumentNo);

                    // Quốc tịch
                    $('#txtNationality').val(ocr.Nationality);

                    // Ngày sinh (Định dạng gốc thường là YYMMDD, ví dụ 630730)
                    if (ocr.Birthday) {
                        $('#txtDOB').val(formatOCRDate(ocr.Birthday));
                    }

                    // Ngày hết hạn
                    if (ocr.Dateofexpiry) {
                        $('#txtExpiryDate').val(formatOCRDate(ocr.Dateofexpiry));
                    }

                    // Giới tính (M -> Nam, F -> Nữ)
                    if (ocr.Sex === "M") {
                        $('#selectGender').val("Male").trigger('change');
                    } else if (ocr.Sex === "F") {
                        $('#selectGender').val("Female").trigger('change');
                    }
                }
            }
        }
    } catch (err) {
        console.error("Lỗi:", err);
    }
}

function formatOCRDate(dateStr) {
    if (!dateStr || dateStr.length !== 6) return "";

    let year = dateStr.substring(0, 2);
    let month = dateStr.substring(2, 4);
    let day = dateStr.substring(4, 6);

    // Logic đoán thế kỷ (nếu > 40 thì là 19xx, ngược lại là 20xx)
    let fullYear = (parseInt(year) > 40) ? "19" + year : "20" + year;

    return `${fullYear}-${month}-${day}`;
}
function processScanResults(dataList) {
    if (!dataList || dataList.length === 0) return;
    const file = dataList[0];

    // Đổ dữ liệu OCR
    if (file.ocrText) {
        if (file.ocrText.FullName) $('#txtCustomerName').val(file.ocrText.FullName);
        if (file.ocrText.DocumentNo) $('#txtIDPassportNo').val(file.ocrText.DocumentNo);
    }

    // Hiển thị ảnh
    if (file.base64) {
        const imgBase = "data:image/jpeg;base64," + file.base64;
        $('#current_img_display').attr('src', imgBase).show();
        $('#no_img_overlay').hide();
    }
}
/*
async function callWebSDKScan() {
    if (!MyScan) {
        alert("Thư viện scan.js chưa được tải hoặc bị lỗi!");
        return;
    }

    try {
        // 1. Hiện Loading
        if (typeof Swal !== 'undefined') {
            Swal.fire({
                title: 'Đang kết nối máy scan...',
                text: 'Vui lòng đặt Passport/ID vào máy',
                allowOutsideClick: false,
                didOpen: () => { Swal.showLoading(); }
            });
        }

        // 2. Kết nối (Bỏ qua nếu đã kết nối)
        try {
            await MyScan.connect({ ip: "127.0.0.1", port: "17778" });
        } catch (e) {
            console.log("Sử dụng kết nối hiện có.");
        }

        // 3. Lấy thiết bị theo đúng tài liệu (CommonReturn.data.options)
        const response = await MyScan.getDeviceList();
        console.log("Dữ liệu thô từ tài liệu:", response);

        // Theo tài liệu: kết quả nằm trong response.data.options
        let devices = [];
        if (response && response.data && response.data.options) {
            devices = response.data.options;
        }

        if (devices.length === 0) {
            if (typeof Swal !== 'undefined') Swal.close();
            alert("Không tìm thấy máy scan! (Thiết bị chưa được cắm hoặc Server chưa nhận)");
            return;
        }

        // Lấy thiết bị đầu tiên
        const firstDevice = devices[0];
        const dName = firstDevice.deviceName; // Tài liệu ghi rõ là 'deviceName'

        console.log("Đã tìm thấy máy scan:", dName);

        const scannerConfig = {
            deviceName: dName,
            source: "ADF-Front", // Tùy chỉnh theo máy Plustek của anh
            paperSize: "A4",
            resolution: 300,
            mode: "color"
        };

        await MyScan.setScanner(scannerConfig);

        // 4. Quét
        const scanRes = await MyScan.scan();
        if (typeof Swal !== 'undefined') Swal.close();

        if (scanRes && scanRes.result && scanRes.data && scanRes.data.length > 0) {
            const file = scanRes.data[0];

            // Đổ dữ liệu OCR vào Form (Nếu có)
            if (file.ocrText) {
                if (file.ocrText.FullName) $('#txtCustomerName').val(file.ocrText.FullName);
                if (file.ocrText.DocumentNo) $('#txtIDPassportNo').val(file.ocrText.DocumentNo);
            }

            // Hiển thị ảnh vào khung có sẵn của anh
            if (file.base64) {
                const imgBase = "data:image/jpeg;base64," + file.base64;
                $('#current_img_display').attr('src', imgBase).show();
                $('#no_img_overlay').hide();

                // Nếu anh dùng mảng allDocs để quản lý slider ảnh
                if (typeof allDocs !== 'undefined') {
                    allDocs.push({ FilePath: imgBase, DocTypeID: $('#ddlDocTypeID').val() || 1 });
                    currentIndex = allDocs.length - 1;
                    $('#img_index_label').text(`${currentIndex + 1} / ${allDocs.length}`);
                }
            }
        } else {
            alert("Quá trình quét không có dữ liệu trả về.");
        }
    } catch (err) {
        if (typeof Swal !== 'undefined') Swal.close();
        console.error("Lỗi thực thi:", err);
        // Alert này giúp anh biết lỗi ở đâu mà không làm "treo" các element khác
        alert("Lỗi máy scan: " + err.message);
    }
}
*/
function displayScanImage(base64) {
    $('#current_img_display').attr('src', base64).show();
    $('#no_img_overlay').hide();
    // Thêm vào mảng allDocs để có thể bấm next/back nếu cần
    allDocs.push({ FilePath: base64, DocTypeID: $('#ddlDocTypeID').val() || 1 });
    currentIndex = allDocs.length - 1;
    $('#img_index_label').text(`${currentIndex + 1} / ${allDocs.length}`);
}
function fillScanDataToForm(data) {
    // Dùng chính các ID anh đã chuẩn hóa
    if (data.FullName) $('#txtCustomerName').val(data.FullName);
    if (data.IDNumber) $('#txtIDPassportNo').val(data.IDNumber);
    if (data.Birthday) $('#txtBirthday').val(data.Birthday); // Cần format YYYY-MM-DD
    if (data.ExpiryDate) $('#txtPassportUntilDate').val(data.ExpiryDate);
    if (data.PersonalNumber) $('#txtADCardNo').val(data.PersonalNumber);

    // Ghi chú tự động
    $('#txtNotes').val("Scanned at " + new Date().toLocaleString());
}