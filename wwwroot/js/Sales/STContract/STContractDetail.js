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
    $('#CurrentRentRate, #PerVAT').on('input', calculateNetPrice);

    $('#CurrentRentRate').on('blur', function () {
        let val = parseFloat($(this).val().replace(/[^0-9.]/g, '')) || 0;
        $(this).val(val.toLocaleString('en-US'));
    }).on('focus', function () {
        $(this).val($(this).val().replace(/,/g, ''));
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

function calculateNetPrice() {
    let gross = parseFloat($('#CurrentRentRate').val().replace(/[^0-9.]/g, '')) || 0;
    let vat = parseFloat($('#PerVAT').val()) || 0;
    let netPrice = gross > 0 ? Math.round(gross / (1 + (vat / 100))) : 0;
    $('#TotalPriceExcVAT').val(netPrice.toLocaleString('en-US'));
}

// --- VÙNG 2: CONTRACT SERVICES (AJAX) ---

function openServiceModal() {
    var contractId = $('#ContractId').val();

    if (!contractId || contractId === "0") {
        alert("Please save the Contract info (Zone 1) first!");
        return;
    }

    $('#formService')[0].reset();
    $('#svc_ContractID').val(contractId);
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
        MaxQuantity: parseFloat($('#svc_Qty').val()),
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
                loadServiceGrid();
                if (typeof toastr !== 'undefined') toastr.success('Saved successfully!');
            } else {
                alert("Error: " + res.message);
            }
        }
    });
}

function loadServiceGrid() {
    var contractId = $('#ContractId').val();
    if (!contractId || contractId == "0") return;

    $.get('?handler=ContractServices', { contractId }, function (data) {
        var $tbody = $('#tableServices tbody').empty();
        if (data && data.length > 0) {
            $.each(data, function (i, item) {
                $tbody.append(`<tr>
                    <td>${item.ServiceName}</td>
                    <td>${moment(item.ServiceFromDate).format('DD/MM/YYYY')}</td>
                    <td>${moment(item.ServiceToDate).format('DD/MM/YYYY')}</td>
                    <td class="text-center"><span class="badge badge-info">${item.ChargeIntervalName}</span></td>
                    <td class="text-right text-bold">${new Intl.NumberFormat('en-US').format(item.UnitPrice || 0)}</td>
                </tr>`);
            });
        } else {
            $tbody.append('<tr><td colspan="5" class="text-center text-muted">No services found</td></tr>');
        }
    });
}