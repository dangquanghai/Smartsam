$(document).ready(function () {
    // 1. Lấy Mode và ID từ hệ thống
    const urlParams = new URLSearchParams(window.location.search);
    const mode = urlParams.get('mode')?.toLowerCase() || 'add';

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

    // 4. Các sự kiện tính toán tự động
    /*
    $('#CurrentRentRate, #PerVAT').on('input', calculateNetPrice);

    $('#CurrentRentRate').on('blur', function () {
        let val = parseFloat($(this).val().replace(/[^0-9.]/g, '')) || 0;
        $(this).val(val.toLocaleString('en-US'));
    }).on('focus', function () {
        $(this).val($(this).val().replace(/,/g, ''));
    });
    */
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
                        <td><strong>${item.customerName || item.CustomerName || ""}</strong></td>
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
    var ctId = $('input[name="selectedTenant"]:checked').val();
    if (!ctId) {
        alert("Please select a tenant from the list first!");
        return;
    }

    // Reset về Tab đầu tiên khi mở modal
    $('#tenantTab a[href="#info"]').tab('show');

    $.ajax({
        url: '?handler=GetFullTenantDetail',
        type: 'GET',
        data: { contractTenantId: ctId },
        success: function (res) {
            // LƯU Ý: res bây giờ gồm { info: ..., docs: [...] }
            var info = res.info;
            var docs = res.docs;

            // 1. Mapping dữ liệu từ CM_Customer & CM_ContractTenant (vào Tab 1)
            $('#txtCustomerID').val(info.customerID);
            $('#txtTitle').val(info.title);
            $('#txtCustomerName').val(info.customerName);
            $('#ddlMale').val(info.male ? "true" : "false");
            $('#txtBirthday').val(info.birthday ? new Date(info.birthday).toLocaleDateString('vi-VN') : '');
            $('#txtNationality').val(info.nationName);
            $('#txtIDPassportNo').val(info.idPassportNo);
            $('#txtPassportUntilDate').val(info.passportUntilDate ? new Date(info.passportUntilDate).toLocaleDateString('vi-VN') : '');
            $('#txtCompany').val(info.company);
            $('#txtVATCode').val(info.vatCode);
            $('#txtAddress').val(info.address);

            $('#ddlFamilyPos').val(info.familyPos);
            $('#chkIsMoveOut').prop('checked', info.isMoveOut);
            $('#txtVisaNo').val(info.visaNo);
            $('#txtVisaExpDate').val(info.visaExpDate ? info.visaExpDate.split('T')[0] : '');
            $('#txtEntryDate').val(info.entryDate ? info.entryDate.split('T')[0] : '');
            $('#ddlArrivalPort').val(info.arrivalPort);
            $('#txtProposeExpDate').val(info.proposeExpDate ? info.proposeExpDate.split('T')[0] : '');
            $('#txtPermitExpDate').val(info.permitExpDate ? info.permitExpDate.split('T')[0] : '');
            $('#txtADCardNo').val(info.a_DCardNo);
            $('#txtLastRegDate').val(info.lastRegDate ? info.lastRegDate.split('T')[0] : '');
            var note = info.notes || ""; // Tránh bị hiện chữ "null"
            $('#txtNotes').val(note);
            $('#txtSponsor').val(info.sponsor);

            // 2. Xử lý Hình ảnh (Vào Tab 2 & Tab 3)
            var htmlPassport = '';
            var htmlPolice = '';

            if (docs && docs.length > 0) {
                $.each(docs, function (i, doc) {
                    var imgItem = `
                        <div class="col-md-3 mb-2">
                            <div class="card shadow-sm">
                                <a href="${doc.filePath}" target="_blank">
                                    <img src="${doc.filePath}" class="card-img-top img-thumbnail" style="height:150px; object-fit:cover">
                                </a>
                                <div class="card-footer p-1 text-center">
                                    <button type="button" class="btn btn-xs btn-danger" onclick="deleteDoc(${doc.id})">
                                        <i class="fas fa-trash"></i>
                                    </button>
                                </div>
                            </div>
                        </div>`;

                    if (doc.docType == 1) { // 1 là Passport
                        htmlPassport += imgItem;
                    } else if (doc.docType == 2) { // 2 là Công an
                        htmlPolice += imgItem;
                    }
                });
            }

            // Đổ vào gallery, nếu trống thì hiện thông báo nhẹ
            $('#passport_gallery').html(htmlPassport || '<div class="col-12 text-muted p-3">Chưa có hình Passport.</div>');
            $('#police_gallery').html(htmlPolice || '<div class="col-12 text-muted p-3">Chưa có hình đăng ký công an.</div>');

            // 3. Hiển thị Modal
            $('#modalTenantDetail').modal('show');
        }
    });
}
$('#btnSaveTenantDetail').click(function () {
    // Thu thập dữ liệu từ các control trong Modal
    var tenantData = {
        ContractID: $('#hfContractID').val(), // Lấy từ hidden field của trang chính
        TenantID: $('#txtCustomerID').val() || 0,
        CustomerName: $('#txtCustomerName').val(),
        Title: $('#txtTitle').val(),
        Male: $('#ddlMale').val() === "true",
        Birthday: $('#txtBirthday').val(),
        Nationality: $('#ddlNationality').val(), // Nếu anh dùng Select2/Dropdown
        IDPassportNo: $('#txtIDPassportNo').val(),
        FamilyPos: $('#ddlFamilyPos').val(),
        IsMoveOut: $('#chkIsMoveOut').is(':checked'),
        VisaNo: $('#txtVisaNo').val(),
        VisaExpDate: $('#txtVisaExpDate').val(),
        EntryDate: $('#txtEntryDate').val(),
        Notes: $('#txtNotes').val(),
        Sponsor: $('#txtSponsor').val()
    };

    $.ajax({
        url: '?handler=SaveTenantDetail',
        type: 'POST',
        contentType: 'application/json',
        headers: { "RequestVerificationToken": $('input[name="__RequestVerificationToken"]').val() },
        data: JSON.stringify(tenantData),
        success: function (res) {
            if (res.success) {
                alert(res.message);
                $('#modalTenantDetail').modal('hide');
                loadTenantsGrid(); // Load lại lưới ở Vùng 3
            } else {
                alert("Error: " + res.message);
            }
        }
    });
});