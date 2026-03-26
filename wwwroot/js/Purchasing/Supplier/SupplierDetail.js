let supplierCurrentId = '';
let supplierPageViewMode = '';
let supplierPageYear = '';
let supplierLastAction = 'save';
// Tracking comment: keep Supplier detail script marked as touched for current work.

$(document).ready(function () {
    // 1. Lấy mode + tham số từ URL
    const urlParams = new URLSearchParams(window.location.search);
    const modeFromUrl = urlParams.get('mode')?.toLowerCase();
    const mode = modeFromUrl || 'add';

    supplierCurrentId = (urlParams.get('id') || '').trim();
    supplierPageViewMode = (urlParams.get('viewMode') || '').toLowerCase();
    supplierPageYear = (urlParams.get('year') || '').trim();

    // 2. Chạy khởi tạo trang
    initializePage(mode);

    $('#btnSave').on('click', function () {
        supplierLastAction = 'save';
    });

    $('#btnSubmit').on('click', function () {
        supplierLastAction = 'submit';
    });

    $('#btnReuse').on('click', function () {
        reuseSupplierAjax();
    });

    // 3. Xử lý sự kiện SUBMIT Form chính
    $('form').on('submit', async function (e) {
        if (mode === 'view') return true;

        e.preventDefault(); // Tạm dừng để validate

        if (validateMainForm()) {
            const isAvailable = await checkSupplierCodeDuplicate(supplierCurrentId);
            if (isAvailable) {
                const $form = $(this);
                const $action = $('<input type="hidden" name="action" />').val(supplierLastAction || 'save');
                $form.append($action);
                this.submit();
                $action.remove();
            } else {
                alert('Supplier code already exists.');
                focusErrorField($('#Input_SupplierCode'));
            }
        }
    });
});

/* ===========================================================================
   CÁC HÀM KHỞI TẠO VÀ VALIDATION (Nội bộ)
   =========================================================================== */

function initializePage(mode) {
    // Load approval history 
    loadApprovalHistory(supplierCurrentId, supplierPageViewMode, supplierPageYear);
}

function validateMainForm() {
    const fields = [
        { id: 'Input_SupplierCode', name: 'Supplier Code' },
        { id: 'Input_SupplierName', name: 'Supplier Name' },
        { id: 'Input_Address', name: 'Address' }
    ];

    for (let field of fields) {
        let $el = $('#' + field.id);
        if (!$el.val() || $el.val().toString().trim() === '' || $el.val() === '0') {
            alert('Please enter/select: ' + field.name);
            focusErrorField($el);
            return false;
        }
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

/* ===========================================================================
   CÁC HÀM AJAX VÀ LOGIC NGHIỆP VỤ (Global Scope - HTML gọi được)
   =========================================================================== */

async function checkSupplierCodeDuplicate(currentId) {
    const supplierCode = ($('#Input_SupplierCode').val() || '').toString().trim();
    if (!supplierCode) return false;

    try {
        const response = await $.ajax({
            url: '?handler=CheckSupplierCode',
            type: 'GET',
            data: {
                supplierCode: supplierCode,
                id: currentId || null
            }
        });

        return !(response && response.exists === true);
    } catch {
        alert('Check supplier code failed.');
        return false;
    }
}

function loadApprovalHistory(currentId, pageViewMode, pageYear) {
    const $tbody = $('#approvalHistoryBody');
    if ($tbody.length === 0) return;

    const noHistoryHtml = '<tr id="no-history-row"><td colspan="3" class="text-center text-muted">No history</td></tr>';
    const escapeHistoryText = function (value) {
        return (value || '')
            .toString()
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/\"/g, '&quot;')
            .replace(/'/g, '&#39;');
    };

    if (!currentId || !Number.isFinite(Number(currentId))) {
        $tbody.html(noHistoryHtml);
        return;
    }

    $.ajax({
        url: '?handler=ApprovalHistory',
        type: 'GET',
        data: {
            supplierId: Number(currentId),
            viewMode: pageViewMode || null,
            year: pageYear || null
        },
        success: function (res) {
            const rows = res && res.success && Array.isArray(res.data) ? res.data : [];
            if (!rows || rows.length === 0) {
                $tbody.html(noHistoryHtml);
                return;
            }

            let html = '';
            $.each(rows, function (_, row) {
                html += `<tr>
                    <td>${escapeHistoryText(row.action)}</td>
                    <td>${escapeHistoryText(row.userName)}</td>
                    <td>${escapeHistoryText(row.actionDate)}</td>
                </tr>`;
            });
            $tbody.html(html);
        },
        error: function () {
            $tbody.html(noHistoryHtml);
        }
    });
}

function reuseSupplierAjax() {
    const token = $('input[name="__RequestVerificationToken"]').val();
    if (!token) {
        alert('Cannot reuse supplier because request token is missing.');
        return;
    }

    if (!supplierCurrentId || !Number.isFinite(Number(supplierCurrentId))) {
        alert('Invalid supplier.');
        return;
    }

    if (!confirm('Reset this disapproved supplier ?')) {
        return;
    }

    $.ajax({
        url: '?handler=ReuseAjax',
        type: 'POST',
        data: {
            id: Number(supplierCurrentId),
            viewMode: supplierPageViewMode || null,
            year: supplierPageYear || null
        },
        headers: { RequestVerificationToken: token },
        success: function (res) {
            const isSuccess = !!(res && (res.success === true || res.Success === true));
            const message = (res && (res.message || res.Message)) || 'Re Use completed.';

            if (!isSuccess) {
                alert(message);
                return;
            }

            window.location.href = (res && (res.redirectUrl || res.RedirectUrl)) || window.location.href;
        },
        error: function (xhr) {
            const message = (xhr && xhr.responseJSON && (xhr.responseJSON.message || xhr.responseJSON.Message))
                || 'Cannot reuse supplier.';
            alert(message);
        }
    });
}
