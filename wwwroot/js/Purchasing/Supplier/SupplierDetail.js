let supplierCurrentId = '';
let supplierPageViewMode = '';
let supplierPageYear = '';
let $supplierForm = $();

$(document).ready(function () {
    // 1. Lấy mode + tham số từ URL/data attribute
    $supplierForm = $('#supplierDetailForm');
    if ($supplierForm.length === 0) return;

    const urlParams = new URLSearchParams(window.location.search);
    const modeFromUrl = urlParams.get('mode')?.toLowerCase();
    const isEditMode = (($supplierForm.data('is-edit') || '') + '').toLowerCase() === 'true';
    const mode = modeFromUrl || (isEditMode ? 'edit' : 'add');

    supplierCurrentId = (($supplierForm.data('current-id') || '') + '').trim();
    supplierPageViewMode = (urlParams.get('viewMode') || '').toLowerCase();
    supplierPageYear = (urlParams.get('year') || '').trim();

    // 2. Chạy khởi tạo trang
    initializePage(mode);

    // 3. Xử lý sự kiện SUBMIT Form chính
    $('form').on('submit', async function (e) {
        if (mode === 'view') return true;

        e.preventDefault(); // Tạm dừng để validate

        if (validateMainForm()) {
            const isAvailable = await checkSupplierCodeDuplicate(supplierCurrentId);
            if (isAvailable) {
                // Giong STContract: off submit handler roi submit that.
                // Co bo sung set action theo submitter de giu dung hanh vi nut Save/Submit.
                const submitter = e.originalEvent && e.originalEvent.submitter ? e.originalEvent.submitter : null;
                if (submitter) {
                    const submitAction = submitter.getAttribute('formaction');
                    if (submitAction) {
                        $(this).attr('action', submitAction);
                    } else {
                        $(this).removeAttr('action');
                    }
                }

                $(this).off('submit').submit();
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

    // Nếu server trả lỗi Input.* thì focus field lỗi đầu tiên.
    const hasServerInputError = (($supplierForm.data('has-server-input-error') || '') + '').toLowerCase() === 'true';
    if (hasServerInputError) {
        const firstInvalidField = (($supplierForm.data('first-invalid-field') || '') + '').trim();
        if (firstInvalidField) {
            focusErrorField($('#' + firstInvalidField));
        }
    }

    // Add mode thì gợi ý mã Supplier.
    if (mode === 'add') {
        loadSuggestedSupplierCode();
    }
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

function loadSuggestedSupplierCode() {
    const $supplierCode = $('#Input_SupplierCode');
    if ($supplierCode.length === 0) return;
    if (($supplierCode.val() || '').toString().trim().length > 0) return;

    $.ajax({
        url: '?handler=SuggestSupplierCode',
        type: 'GET',
        success: function (res) {
            const suggestedCode = (res && res.supplierCode ? res.supplierCode : '').trim();
            if (!suggestedCode) return;
            if (($supplierCode.val() || '').toString().trim().length > 0) return;

            $supplierCode.val(suggestedCode);
        }
    });
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
