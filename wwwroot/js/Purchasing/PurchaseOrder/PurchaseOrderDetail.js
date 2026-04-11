$(document).ready(function () {
    const mode = ($('#Mode').val() || 'add').toLowerCase();
    initializePage(mode);

    $('#purchaseOrderDetailForm').on('submit', function (e) {
        if (mode === 'view') {
            return true;
        }

        e.preventDefault();
        if (validateMainForm()) {
            syncDetailsJson();
            $(this).off('submit').submit();
        }
    });
});

let purchaseOrderDetails = [];
let purchaseOrderPrLines = [];

function initializePage(mode) {
    initializeLookupControls();

    try {
        purchaseOrderDetails = ($('#DetailsJson').val() ? JSON.parse($('#DetailsJson').val()) : []).map(normalizeDetail);
    } catch (e) {
        purchaseOrderDetails = [];
    }

    renderDetailRows();
    bindMainEvents(mode);
    updateTotals();

    if (window.purchaseOrderDetailPage?.openConvertModal && window.jQuery) {
        closeOpenSelect2();
        $('#purchaseOrderConvertModal').modal('show');
    }
}

// Supplier va PR dung chung lookup endpoint.
function initializeLookupControls() {
    initializeAjaxSelect2('#SupplierID', window.purchaseOrderDetailPage?.supplierLookupUrl || '');
    initializeAjaxSelect2('#PRID', window.purchaseOrderDetailPage?.prLookupUrl || '');
}

// Ham nho de 2 o lookup dung chung cach khoi tao select2.
function initializeAjaxSelect2(selector, url) {
    const $element = $(selector);
    if (!$element.length || !url || typeof $element.select2 !== 'function') {
        return;
    }

    if ($element.hasClass('select2-hidden-accessible')) {
        $element.select2('destroy');
    }

    $element.select2({
        width: '100%',
        allowClear: true,
        placeholder: $element.find('option:first').text() || '-- Select --',
        minimumInputLength: 0,
        ajax: {
            url: url,
            dataType: 'json',
            delay: 250,
            data: function (params) {
                return {
                    term: params.term || ''
                };
            },
            processResults: function (data) {
                return {
                    results: Array.isArray(data) ? data : []
                };
            },
            cache: true
        }
    });

    $element.on('select2:open', function () {
        const searchField = document.querySelector('.select2-container--open .select2-search__field');
        if (searchField) {
            searchField.focus();
        }
    });
}

// Dong tat cac select2 dang mo truoc khi mo modal.
function closeOpenSelect2() {
    if (!$.fn.select2) {
        return;
    }

    $('.select2-hidden-accessible').each(function () {
        const $el = $(this);
        if ($el.data('select2')) {
            $el.select2('close');
        }
    });
}

// Kiem tra form truoc khi post PO header.
function validateMainForm() {
    const fields = [
        { id: 'PONo', name: 'PO No.' },
        { id: 'PODate', name: 'Order Date' },
        { id: 'SupplierID', name: 'Supplier' }
    ];

    for (let i = 0; i < fields.length; i += 1) {
        const field = fields[i];
        const $el = $('#' + field.id);
        if (!$el.val() || $el.val().toString().trim() === '' || $el.val() === '0') {
            alert('Please enter/select: ' + field.name);
            focusErrorField($el);
            return false;
        }
    }

    if (purchaseOrderDetails.length === 0) {
        alert('Please add at least one detail row.');
        return false;
    }

    for (let index = 0; index < purchaseOrderDetails.length; index += 1) {
        const row = purchaseOrderDetails[index];
        if (!row.itemID || row.quantity <= 0) {
            alert('Detail row ' + (index + 1) + ' is invalid.');
            return false;
        }
    }

    return true;
}

function focusErrorField($el) {
    setTimeout(function () {
        $el.focus();
    }, 150);
}

// Dua JSON post len ve cung 1 kieu du lieu.
function normalizeDetail(detail) {
    return {
        tempKey: detail.tempKey || detail.TempKey || ('tmp-' + Date.now() + '-' + Math.random().toString(16).slice(2)),
        itemID: detail.itemID || detail.ItemID || 0,
        itemCode: detail.itemCode || detail.ItemCode || '',
        itemName: detail.itemName || detail.ItemName || '',
        unit: detail.unit || detail.Unit || '',
        quantity: toNumber(detail.quantity || detail.Quantity),
        unitPrice: toNumber(detail.unitPrice || detail.UnitPrice),
        poAmount: toNumber(detail.poAmount || detail.POAmount),
        recDept: detail.recDept || detail.RecDept || '',
        recDeptName: detail.recDeptName || detail.RecDeptName || '',
        note: detail.note || detail.Note || '',
        recQty: toNumber(detail.recQty || detail.RecQty),
        recAmount: toNumber(detail.recAmount || detail.RecAmount),
        recDate: detail.recDate || detail.RecDate || null,
        mrRequestNo: detail.mRRequestNo || detail.MRRequestNo || ''
    };
}

// Gan su kien cho nut va dong detail cua PO.
function bindMainEvents(mode) {
    $('#PerVAT').on('input', updateTotals);
    $('#btnCalculateTotal').on('click', updateTotals);

    $('#btnAddDetail').on('click', function () {
        closeOpenSelect2();
        loadPrLines();
    });

    $('#btnConfirmAddDetail').on('click', function () {
        addSelectedPrLines();
    });

    $('#purchaseOrderPrCheckAll').on('change', function () {
        const isChecked = $(this).is(':checked');
        $('#purchaseOrderPrLineRows .po-pr-check').prop('checked', isChecked);
        syncPrLineCheckAllState();
    });

    $('#btnEvaluate').on('click', function () {
        closeOpenSelect2();
        $('#purchaseOrderEvaluateModal').modal('show');
    });

    $('#btnPurchaserApprove').on('click', function () {
        if (!confirm('Approve this purchase order and send it to CFO?')) {
            return;
        }

        $('#poPurchaserApproveForm').submit();
    });

    $('#btnConfirmEvaluate').on('click', function () {
        const point = $('input[name="estimatePointOption"]:checked').val();
        if (!point) {
            alert('Please select estimate point.');
            return;
        }

        if (!$('#confirmEstimateCheck').is(':checked')) {
            alert('Please confirm your estimate.');
            return;
        }

        $('#EvaluateEstimatePointInput').val(point);
        $('#poEvaluateForm').submit();
    });

    $('#btnApprove').on('click', function () {
        $('#poApproveForm').submit();
    });

    $('#btnBackToProcessingDetail').on('click', function () {
        closeOpenSelect2();
        $('#purchaseOrderConvertModal').modal('show');
    });

    $('#btnConfirmBackToProcessing').on('click', function () {
        const reason = ($('#ConvertReasonText').val() || '').trim();
        if (!reason) {
            alert('Please enter reason to convert PO.');
            $('#ConvertReasonText').focus();
            return;
        }

        $('#ConvertReasonInput').val(reason);
        $('#poBackToProcessingForm').submit();
    });

    $(document).on('input', '.po-detail-qty, .po-detail-price, .po-detail-recqty', function () {
        const $row = $(this).closest('tr');
        const rowKey = $(this).closest('tr').data('key');
        const row = purchaseOrderDetails.find(function (item) { return item.tempKey === rowKey; });
        if (!row) {
            return;
        }

        row.quantity = toNumber($(this).closest('tr').find('.po-detail-qty').val());
        row.unitPrice = toNumber($(this).closest('tr').find('.po-detail-price').val());
        row.poAmount = row.quantity * row.unitPrice;
        row.recQty = toNumber($(this).closest('tr').find('.po-detail-recqty').val());
        row.recAmount = row.recQty * row.unitPrice;
        $row.find('.po-detail-amount').val(formatNumber(row.poAmount));
        $row.find('.po-detail-recamount').val(formatNumber(row.recAmount));
        updateTotals();
    });

    $(document).on('change', '.po-detail-dept', function () {
        const rowKey = $(this).closest('tr').data('key');
        const row = purchaseOrderDetails.find(function (item) { return item.tempKey === rowKey; });
        if (!row) {
            return;
        }

        row.recDept = $(this).val() || '';
    });

    $(document).on('change', '.po-detail-note', function () {
        const rowKey = $(this).closest('tr').data('key');
        const row = purchaseOrderDetails.find(function (item) { return item.tempKey === rowKey; });
        if (!row) {
            return;
        }

        row.note = $(this).val() || '';
    });

    $(document).on('change', '.po-detail-recdate', function () {
        const rowKey = $(this).closest('tr').data('key');
        const row = purchaseOrderDetails.find(function (item) { return item.tempKey === rowKey; });
        if (!row) {
            return;
        }

        row.recDate = $(this).val() || '';
    });

    $(document).on('click', '.po-remove-row', function () {
        const rowKey = $(this).closest('tr').data('key');
        purchaseOrderDetails = purchaseOrderDetails.filter(function (item) {
            return item.tempKey !== rowKey;
        });
        renderDetailRows();
        updateTotals();
    });

    $(document).on('change', '#purchaseOrderPrLineRows .po-pr-check', function () {
        syncPrLineCheckAllState();
    });
}

// Nap dong PR vao modal de chon copy sang PO.
function loadPrLines() {
    const prId = $('#PRID').val();
    if (!prId) {
        alert('Please select PR first.');
        focusErrorField($('#PRID'));
        return;
    }

    // PO detail lay theo PR dang chon.
    // PRID la key goc cua modal nay.
    $.get(window.purchaseOrderDetailPage?.prLinesUrl || '', { prId: prId }, function (response) {
        if (!response.success) {
            alert(response.message || 'Cannot load PR lines.');
            return;
        }

        purchaseOrderPrLines = response.data || [];
        renderPrLineRows();
        syncPrLineCheckAllState();
        closeOpenSelect2();
        $('#purchaseOrderAddDetailModal').modal('show');
    });
}

// Ve lai bang detail PO tu data dang co trong RAM.
function renderDetailRows() {
    const $tbody = $('#purchaseOrderDetailRows');
    const canSave = !!window.purchaseOrderDetailPage?.canSave;
    $tbody.empty();

    if (!purchaseOrderDetails.length) {
        $tbody.html(`<tr id="purchaseOrderDetailEmptyRow"><td colspan="${canSave ? 13 : 12}" class="text-center text-muted">No detail rows</td></tr>`);
        $('#purchaseOrderDetailCount').text('0');
        return;
    }

    const deptOptions = buildDepartmentOptions();
    const html = purchaseOrderDetails.map(function (row) {
        return `
            <tr data-key="${escapeHtml(row.tempKey)}">
                <td></td>
                <td class="po-detail-itemcode">${escapeHtml(row.itemCode)}</td>
                <td class="tcvn3-font">${escapeHtml(row.itemName)}</td>
                <td>${escapeHtml(row.unit)}</td>
                <td><input type="text" class="form-control form-control-sm text-right po-detail-qty" value="${formatNumber(row.quantity)}" ${canSave ? '' : 'readonly'} /></td>
                <td><input type="text" class="form-control form-control-sm text-right po-detail-price" value="${formatNumber(row.unitPrice)}" ${canSave ? '' : 'readonly'} /></td>
                <td><input type="text" class="form-control form-control-sm text-right po-detail-amount" value="${formatNumber(row.poAmount)}" readonly /></td>
                <td>${canSave ? `<select class="form-control form-control-sm po-detail-dept">${deptOptions(row.recDept)}</select>` : escapeHtml(row.recDeptName)}</td>
                <td>${canSave ? `<input type="text" class="form-control form-control-sm vni-font po-detail-note" value="${escapeAttribute(row.note)}" />` : escapeHtml(row.note)}</td>
                <td><input type="text" class="form-control form-control-sm text-right po-detail-recqty" value="${formatNumber(row.recQty)}" ${canSave ? '' : 'readonly'} /></td>
                <td><input type="text" class="form-control form-control-sm text-right po-detail-recamount" value="${formatNumber(row.recAmount)}" readonly /></td>
                <td>${canSave ? `<input type="date" class="form-control form-control-sm po-detail-recdate" value="${formatDate(row.recDate)}" />` : escapeHtml(formatDate(row.recDate))}</td>
                ${canSave ? '<td class="text-center"><button type="button" class="btn btn-xs btn-outline-danger po-remove-row" title="Remove detail"><i class="fas fa-trash-alt"></i></button></td>' : ''}
            </tr>`;
    });

    $tbody.html(html.join(''));
    $('#purchaseOrderDetailCount').text(String(purchaseOrderDetails.length));
}

// Ve bang chon dong PR trong modal.
function renderPrLineRows() {
    const $tbody = $('#purchaseOrderPrLineRows');
    if (!purchaseOrderPrLines.length) {
        $tbody.html('<tr><td colspan="8" class="text-center text-muted">No PR detail rows</td></tr>');
        return;
    }

    const html = purchaseOrderPrLines.map(function (row, index) {
        return `
            <tr data-index="${index}">
                <td class="text-center"><input type="checkbox" class="po-pr-check" /></td>
                <td>${escapeHtml(row.itemCode || row.ItemCode || '')}</td>
                <td class="tcvn3-font po-pr-itemname">${escapeHtml(row.itemName || row.ItemName || '')}</td>
                <td>${escapeHtml(row.unit || row.Unit || '')}</td>
                <td class="text-right">${formatNumber(row.quantity || row.Quantity || 0)}</td>
                <td class="text-right">${formatNumber(row.unitPrice || row.UnitPrice || 0)}</td>
                <td class="vni-font">${escapeHtml(row.remark || row.Remark || '')}</td>
                <td class="vni-font">${escapeHtml(row.supplierText || row.SupplierText || '')}</td>
            </tr>`;
    });

    $tbody.html(html.join(''));
    syncPrLineCheckAllState();
}

// Copy dong PR da chon sang danh sach PO.
function addSelectedPrLines() {
    const selectedIndexes = [];
    $('#purchaseOrderPrLineRows .po-pr-check:checked').each(function () {
        selectedIndexes.push(parseInt($(this).closest('tr').data('index'), 10));
    });

    if (!selectedIndexes.length) {
        alert('Please select at least one PR line.');
        return;
    }

    const supplierId = $('#SupplierID').val();
    const supplierText = ($('#SupplierID option:selected').text() || '').trim();
    const expectedSupplierId = supplierId ? parseInt(supplierId, 10) : 0;

    selectedIndexes.forEach(function (index) {
        const source = purchaseOrderPrLines[index];
        if (!source) {
            return;
        }

        purchaseOrderDetails.push(normalizeDetail({
            ItemID: source.itemID || source.ItemID || 0,
            ItemCode: source.itemCode || source.ItemCode || '',
            ItemName: source.itemName || source.ItemName || '',
            Unit: source.unit || source.Unit || '',
            Quantity: source.quantity || source.Quantity || 0,
            UnitPrice: source.unitPrice || source.UnitPrice || 0,
            POAmount: toNumber(source.quantity || source.Quantity || 0) * toNumber(source.unitPrice || source.UnitPrice || 0),
            Note: source.remark || source.Remark || '',
            MRRequestNo: $('#PRID option:selected').text() || '',
            SupplierID: expectedSupplierId || null,
            SupplierText: supplierText
        }));
    });

    renderDetailRows();
    updateTotals();
    $('#purchaseOrderAddDetailModal').modal('hide');
}

// Dong bo checkbox Check All theo tung dong PR.
function syncPrLineCheckAllState() {
    const $checkAll = $('#purchaseOrderPrCheckAll');
    const $checks = $('#purchaseOrderPrLineRows .po-pr-check');

    if (!$checkAll.length) {
        return;
    }

    if (!$checks.length) {
        $checkAll.prop('checked', false);
        $checkAll.prop('indeterminate', false);
        $checkAll.prop('disabled', true);
        return;
    }

    const checkedCount = $checks.filter(':checked').length;
    $checkAll.prop('disabled', false);
    $checkAll.prop('checked', checkedCount > 0 && checkedCount === $checks.length);
    $checkAll.prop('indeterminate', checkedCount > 0 && checkedCount < $checks.length);
}

// Tinh lai VAT va tong tien sau khi sua dong detail.
function updateTotals() {
    let beforeVat = 0;
    for (let i = 0; i < purchaseOrderDetails.length; i += 1) {
        const row = purchaseOrderDetails[i];
        row.poAmount = row.quantity * row.unitPrice;
        row.recAmount = row.recQty * row.unitPrice;
        beforeVat += row.poAmount;
    }

    const perVat = toNumber($('#PerVAT').val());
    const vat = beforeVat * perVat / 100;
    const afterVat = beforeVat + vat;

    $('#BeforeVAT').val(formatNumber(beforeVat));
    $('#VAT').val(formatNumber(vat));
    $('#AfterVAT').val(formatNumber(afterVat));
    syncDetailsJson();
}

// Day detail hien tai vao field JSON an truoc khi submit.
function syncDetailsJson() {
    const payload = purchaseOrderDetails.map(function (row) {
        const recDate = (row.recDate || '').toString().trim();
        return {
            tempKey: row.tempKey,
            itemID: row.itemID,
            itemCode: row.itemCode,
            itemName: row.itemName,
            unit: row.unit,
            quantity: row.quantity,
            unitPrice: row.unitPrice,
            poAmount: row.poAmount,
            recDept: row.recDept ? parseInt(row.recDept, 10) : null,
            recDeptName: row.recDeptName,
            note: row.note,
            recQty: row.recQty,
            recAmount: row.recAmount,
            recDate: recDate ? recDate : null,
            mrRequestNo: row.mrRequestNo
        };
    });
    $('#DetailsJson').val(JSON.stringify(payload));
}

// Tao option dropdown phong ban tu list tren server.
function buildDepartmentOptions() {
    const options = window.purchaseOrderDetailPage?.departmentOptions || [];
    return function (selectedValue) {
        let html = '<option value="">-- Select --</option>';
        for (let i = 0; i < options.length; i += 1) {
            const option = options[i];
            const optionValue = String(option.value || '');
            const selected = String(selectedValue || '') === optionValue ? ' selected' : '';
            html += `<option value="${escapeAttribute(optionValue)}"${selected}>${escapeHtml(option.text || '')}</option>`;
        }
        return html;
    };
}

// Ham nho de chuyen gia tri ve so.
function toNumber(value) {
    const normalized = String(value || '').replace(/,/g, '').trim();
    const parsed = parseFloat(normalized);
    return Number.isFinite(parsed) ? parsed : 0;
}

// Dinh dang so de hien thi trong bang.
function formatNumber(value) {
    return toNumber(value).toLocaleString('en-US', { minimumFractionDigits: 0, maximumFractionDigits: 2 });
}

// Doi ngay ve dang yyyy-MM-dd cho input type=date.
function formatDate(value) {
    if (!value) {
        return '';
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return '';
    }

    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${date.getFullYear()}-${month}-${day}`;
}

// Escape text truoc khi dua vao HTML.
function escapeHtml(value) {
    return String(value || '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

// Escape text khi dung trong attribute HTML.
function escapeAttribute(value) {
    return escapeHtml(value);
}
