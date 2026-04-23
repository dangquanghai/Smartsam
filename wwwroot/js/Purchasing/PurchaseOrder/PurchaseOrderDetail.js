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

// Supplier dung lookup endpoint; PR No la dropdown thuong server-rendered.
function initializeLookupControls() {
    initializeAjaxSelect2('#SupplierID', window.purchaseOrderDetailPage?.supplierLookupUrl || '', {
        minimumInputLength: 0,
        useSelectedTermWhenEmpty: true,
        templateResult: formatSupplierResult,
        templateSelection: formatSupplierSelection,
        escapeMarkup: function (markup) {
            return markup;
        }
    });
}

// Ham nho de 2 o lookup dung chung cach khoi tao select2.
function initializeAjaxSelect2(selector, url, options) {
    const $element = $(selector);
    if (!$element.length || !url || typeof $element.select2 !== 'function') {
        return;
    }

    if ($element.hasClass('select2-hidden-accessible')) {
        $element.select2('destroy');
    }

    const customOptions = Object.assign({}, options || {});
    const useSelectedTermWhenEmpty = customOptions.useSelectedTermWhenEmpty === true;
    delete customOptions.useSelectedTermWhenEmpty;

    const config = Object.assign({
        width: '100%',
        allowClear: true,
        placeholder: $element.find('option:first').text() || '-- Select --',
        minimumInputLength: 3,
        ajax: {
            url: url,
            dataType: 'json',
            delay: 250,
            data: function (params) {
                const term = (params.term || '').trim();
                return {
                    term: term || (useSelectedTermWhenEmpty ? getSelect2SearchSeed($element) : '')
                };
            },
            processResults: function (data) {
                return {
                    results: Array.isArray(data) ? data : []
                };
            },
            cache: true
        }
    }, customOptions);

    $element.select2(config);

    $element.on('select2:open', function () {
        const searchField = document.querySelector('.select2-container--open .select2-search__field');
        if (searchField) {
            const selectedText = getSelect2SearchSeed($element);
            if (!searchField.value.trim() && selectedText) {
                searchField.value = selectedText;
                searchField.dispatchEvent(new Event('input', { bubbles: true }));
                searchField.dispatchEvent(new Event('keyup', { bubbles: true }));
            }

            searchField.focus();
            if (typeof searchField.select === 'function') {
                searchField.select();
            }
        }
    });
}

function getSelect2SearchSeed($element) {
    const selectedText = ($element.find('option:selected').text() || '').trim();
    if (!selectedText || selectedText.startsWith('--')) {
        return '';
    }

    let seed = selectedText;
    const parenIndex = seed.indexOf(' (');
    if (parenIndex > -1) {
        seed = seed.substring(0, parenIndex).trim();
    }

    const slashIndex = seed.indexOf(' / ');
    if (slashIndex > -1) {
        seed = seed.substring(0, slashIndex).trim();
    }

    return seed;
}

function formatSupplierResult(item) {
    if (!item || item.loading) {
        return item ? item.text : '';
    }

    const supplierCode = (item.supplierCode || item.text || '').toString();
    const supplierName = (item.supplierName || '').toString();
    const $result = $('<div class="po-supplier-select2-result"></div>');
    $('<div class="po-supplier-select2-code"></div>').text(supplierCode).appendTo($result);
    if (supplierName) {
        $('<div class="po-supplier-select2-name vni-font"></div>').text(supplierName).appendTo($result);
    }

    return $result;
}

function formatSupplierSelection(item) {
    return (item && (item.supplierCode || item.text)) || '';
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

function buildPurchaseOrderQuestPdfUrl() {
    const baseUrl = window.purchaseOrderDetailPage?.questPdfUrl || '';
    const reportId = window.purchaseOrderDetailPage?.reportId || 0;
    if (!baseUrl || !reportId) {
        return '';
    }

    return appendQueryParam(baseUrl, 'id', reportId);
}

function buildPurchaseOrderQuestPdfPreviewUrl() {
    const baseUrl = window.purchaseOrderDetailPage?.questPdfPreviewUrl || '';
    const reportId = window.purchaseOrderDetailPage?.reportId || 0;
    if (!baseUrl || !reportId) {
        return '';
    }

    return appendQueryParam(baseUrl, 'id', reportId);
}

function appendQueryParam(baseUrl, name, value) {
    if (!baseUrl) {
        return '';
    }

    const url = new URL(baseUrl, window.location.origin);
    url.searchParams.set(name, String(value ?? ''));
    return `${url.pathname}${url.search}`;
}

let activePurchaseOrderReportPreviewObjectUrl = '';

async function loadPurchaseOrderPdfPreview(frame, url, getCurrentUrl) {
    if (!frame || !url) {
        return null;
    }

    const response = await fetch(url, {
        method: 'GET',
        credentials: 'same-origin',
        headers: { 'X-Requested-With': 'XMLHttpRequest' }
    });

    if (!response.ok) {
        throw new Error('Cannot load report preview.');
    }

    const contentType = String(response.headers.get('content-type') || '').toLowerCase();
    if (!contentType.includes('application/pdf')) {
        const text = await response.text();
        throw new Error(text || 'Report preview did not return a PDF.');
    }

    const blob = await response.blob();
    if (typeof getCurrentUrl === 'function' && getCurrentUrl() !== url) {
        return null;
    }

    const previewUrl = URL.createObjectURL(blob);
    frame.src = previewUrl;
    return previewUrl;
}

function clearPurchaseOrderPdfPreview(frame) {
    if (frame) {
        frame.removeAttribute('src');
    }

    if (activePurchaseOrderReportPreviewObjectUrl) {
        URL.revokeObjectURL(activePurchaseOrderReportPreviewObjectUrl);
        activePurchaseOrderReportPreviewObjectUrl = '';
    }
}

// Mo modal preview bao cao PDF bang iframe, giong luong PR.
function openPurchaseOrderReportPreviewModal() {
    const previewUrl = buildPurchaseOrderQuestPdfPreviewUrl();
    if (!previewUrl) {
        alert('Purchase order report preview is not available.');
        return;
    }

    const $modal = $('#purchaseOrderReportPreviewModal');
    const $loading = $('#purchaseOrderReportPreviewLoading');
    const frame = document.getElementById('purchaseOrderReportPreviewFrame');

    if (!$modal.length || !frame) {
        alert('Report preview modal is not available.');
        return;
    }

    $loading.show();
    clearPurchaseOrderPdfPreview(frame);

    closeOpenSelect2();
    $modal.modal('show');

    loadPurchaseOrderPdfPreview(frame, previewUrl, function () {
        return buildPurchaseOrderQuestPdfPreviewUrl();
    })
        .then(function (objectUrl) {
            if (objectUrl) {
                activePurchaseOrderReportPreviewObjectUrl = objectUrl;
            }
            $loading.hide();
        })
        .catch(function (error) {
            $loading.hide();
            if ($modal.length) {
                $modal.modal('hide');
            }
            alert(error?.message || 'Cannot load PDF preview.');
        });
}


async function downloadPurchaseOrderQuestPdf() {
    const reportUrl = buildPurchaseOrderQuestPdfUrl();
    if (!reportUrl) {
        alert('Purchase order QuestPDF report is not available.');
        return;
    }

    const poNo = (window.purchaseOrderDetailPage?.reportFileName || 'purchase_order').trim() || 'purchase_order';
    const fileName = `PurchaseOrder_No_${poNo.replace(/[\\/:*?"<>|]+/g, '_')}.pdf`;
    const response = await fetch(reportUrl, {
        method: 'GET',
        credentials: 'same-origin'
    });

    if (!response.ok) {
        throw new Error(`Cannot generate QuestPDF report. HTTP ${response.status}`);
    }

    const contentType = response.headers.get('content-type') || '';
    if (!contentType.toLowerCase().includes('application/pdf')) {
        const text = await response.text();
        throw new Error(text || 'QuestPDF report is not available.');
    }

    const blob = await response.blob();
    const url = window.URL.createObjectURL(blob);
    try {
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
    } finally {
        window.URL.revokeObjectURL(url);
    }
}

function getAllowedAttachmentExtensions() {
    return String(window.purchaseOrderDetailPage?.allowedAttachmentExtensions || '')
        .split(',')
        .map(function (item) {
            return item.trim().toLowerCase();
        })
        .filter(Boolean);
}

function getMaxAttachmentSizeBytes() {
    const maxMb = Number.parseInt(window.purchaseOrderDetailPage?.maxAttachmentSizeMb, 10);
    return Number.isFinite(maxMb) && maxMb > 0 ? maxMb * 1024 * 1024 : 0;
}

function normalizeAttachmentName(fileName) {
    return String(fileName || '').trim();
}

function updateAttachmentDeleteButton() {
    const $button = $('#btnPurchaseOrderAttachmentDelete');
    if (!$button.length) {
        return;
    }

    $button.prop('disabled', $('#purchaseOrderAttachmentModal .po-attachment-selector:checked').length === 0);
}

function validateAttachmentUpload() {
    const input = document.getElementById('purchaseOrderAttachmentUpload');
    const errorEl = document.getElementById('purchaseOrderAttachmentError');

    if (!input || !errorEl || !input.files || input.files.length === 0) {
        if (errorEl) {
            errorEl.textContent = '';
            errorEl.style.display = 'none';
        }
        return true;
    }

    const allowedExtensions = getAllowedAttachmentExtensions();
    const maxSizeBytes = getMaxAttachmentSizeBytes();
    const files = Array.from(input.files);

    for (const file of files) {
        const fileName = normalizeAttachmentName(file.name);
        const extension = `.${String(fileName.split('.').pop() || '').toLowerCase()}`;

        if (allowedExtensions.length > 0 && !allowedExtensions.includes(extension)) {
            errorEl.textContent = `Attached file extension is invalid for '${fileName}'. Allowed: ${window.purchaseOrderDetailPage?.allowedAttachmentExtensions || ''}`;
            errorEl.style.display = 'block';
            input.value = '';
            return false;
        }

        if (maxSizeBytes > 0 && file.size > maxSizeBytes) {
            errorEl.textContent = `Attached file '${fileName}' size must not exceed ${window.purchaseOrderDetailPage?.maxAttachmentSizeMb || 0} MB.`;
            errorEl.style.display = 'block';
            input.value = '';
            return false;
        }
    }

    errorEl.textContent = '';
    errorEl.style.display = 'none';
    return true;
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
        isPersisted: detail.isPersisted === true || detail.IsPersisted === true,
        itemID: detail.itemID || detail.ItemID || 0,
        prDetailId: detail.prDetailId || detail.PrDetailId || detail.mrDetailId || detail.MRDetailID || 0,
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
    const attachmentModal = $('#purchaseOrderAttachmentModal');
    const attachmentInput = document.getElementById('purchaseOrderAttachmentUpload');
    const attachmentError = document.getElementById('purchaseOrderAttachmentError');
    const reportPreviewModal = $('#purchaseOrderReportPreviewModal');
    const reportPreviewFrame = document.getElementById('purchaseOrderReportPreviewFrame');
    $('#PerVAT').on('input', updateTotals);
    $('#btnCalculateTotal').on('click', updateTotals);

    $('#btnOpenPurchaseOrderReport').on('click', function () {
        openPurchaseOrderReportPreviewModal();
    });

    $('#btnExportPurchaseOrderReportPdf').on('click', function () {
        downloadPurchaseOrderQuestPdf().catch(function (error) {
            console.error(error);
            alert(error?.message || 'Cannot export QuestPDF.');
        });
    });

    if (window.jQuery && reportPreviewModal.length) {
        reportPreviewModal.on('hidden.bs.modal', function () {
            clearPurchaseOrderPdfPreview(reportPreviewFrame);
            $('#purchaseOrderReportPreviewLoading').show();
        });
    }

    $('#btnOpenPurchaseOrderAttachments').on('click', function () {
        closeOpenSelect2();
        if (attachmentError) {
            attachmentError.textContent = '';
            attachmentError.style.display = 'none';
        }
        if (window.jQuery && attachmentModal.length) {
            attachmentModal.modal('show');
        }
    });

    $('#btnAddDetail').on('click', function () {
        closeOpenSelect2();
        syncPurchaseOrderDetailsFromGrid();
        loadPrLines();
    });

    $('#btnConfirmAddDetail').on('click', function () {
        addSelectedPrLines();
    });

    $('#purchaseOrderPrCheckAll').on('change', function () {
        const isChecked = $(this).is(':checked');
        $('#purchaseOrderPrLineRows .po-pr-check:not(:disabled)').prop('checked', isChecked);
        syncPrLineCheckAllState();
        syncPrLineActionState();
    });

    $('#btnEvaluate').on('click', function () {
        closeOpenSelect2();
        const currentEstimatePoint = toNumber(window.purchaseOrderDetailPage?.currentEstimatePoint || 0);
        $('input[name="estimatePointOption"]').prop('checked', false);
        if (currentEstimatePoint > 0) {
            $(`input[name="estimatePointOption"][value="${currentEstimatePoint}"]`).prop('checked', true);
        }
        $('#confirmEstimateCheck').prop('checked', false);
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

    $('#btnPurchaseOrderAttachmentUpload').on('click', function (event) {
        if (!validateAttachmentUpload()) {
            event.preventDefault();
        }
    });

    $('#purchaseOrderAttachmentUpload').on('change', function () {
        validateAttachmentUpload();
    });

    $('#purchaseOrderAttachmentModal').on('change', '.po-attachment-selector', function () {
        updateAttachmentDeleteButton();
    });

    $('#purchaseOrderAttachmentModal').on('shown.bs.modal', function () {
        updateAttachmentDeleteButton();
        if (attachmentInput && attachmentInput.files && attachmentInput.files.length > 0) {
            validateAttachmentUpload();
        }
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
        syncPrLineActionState();
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
    $.get(window.purchaseOrderDetailPage?.prLinesUrl || '', {
        prId: prId,
        currentPoId: window.purchaseOrderDetailPage?.reportId || 0
    }, function (response) {
        if (!response.success) {
            alert(response.message || 'Cannot load PR lines.');
            return;
        }

        purchaseOrderPrLines = response.data || [];
        renderPrLineRows();
        syncPrLineCheckAllState();
        syncPrLineActionState();
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
        $tbody.html('<tr id="purchaseOrderDetailEmptyRow"><td colspan="12" class="text-center text-muted">No detail rows</td></tr>');
        $('#purchaseOrderDetailCount').text('0');
        return;
    }

    const deptOptions = buildDepartmentOptions();
    const html = purchaseOrderDetails.map(function (row, index) {
        return `
            <tr data-key="${escapeHtml(row.tempKey)}">
                ${canSave ? `<td class="po-detail-action-cell"><div class="po-detail-action-wrap"><button type="button" class="btn btn-xs btn-outline-danger border po-remove-row" data-remove-temp-key="${escapeHtml(row.tempKey)}" title="Remove"><i class="fas fa-trash"></i></button></div></td>` : `<td class="po-detail-stt-cell text-center">${index + 1}</td>`}
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
        const remainingQty = getAvailablePrLineQuantity(row);
        const isDisabled = remainingQty <= 0;
        return `
            <tr data-index="${index}" class="${isDisabled ? 'po-pr-row-disabled' : ''}">
                <td class="text-center"><input type="checkbox" class="po-pr-check" ${isDisabled ? 'disabled' : ''} /></td>
                <td>${escapeHtml(row.itemCode || row.ItemCode || '')}</td>
                <td class="tcvn3-font po-pr-itemname">${escapeHtml(row.itemName || row.ItemName || '')}</td>
                <td>${escapeHtml(row.unit || row.Unit || '')}</td>
                <td class="text-right">${formatNumber(remainingQty)}</td>
                <td class="text-right">${formatNumber(row.unitPrice || row.UnitPrice || 0)}</td>
                <td class="vni-font">${escapeHtml(row.remark || row.Remark || '')}</td>
                <td class="vni-font">${escapeHtml(row.supplierText || row.SupplierText || '')}</td>
            </tr>`;
    });

    $tbody.html(html.join(''));
    syncPrLineCheckAllState();
    syncPrLineActionState();
}

function getAvailablePrLineQuantity(row) {
    const totalQuantity = toNumber(row.quantity || row.Quantity || 0);
    const prDetailId = toNumber(row.prDetailId || row.PrDetailId || 0);
    const alreadySelectedQuantity = purchaseOrderDetails.reduce(function (sum, detail) {
        const detailPrDetailId = toNumber(detail.prDetailId || detail.PrDetailId || 0);
        if (detailPrDetailId !== prDetailId) {
            return sum;
        }

        return sum + toNumber(detail.quantity || detail.Quantity || 0);
    }, 0);

    return Math.max(totalQuantity - alreadySelectedQuantity, 0);
}

// Dong bo lai state detail tu bang hien tai truoc khi mo popup PR.
function syncPurchaseOrderDetailsFromGrid() {
    const refreshedDetails = [];

    $('#purchaseOrderDetailRows tr[data-key]').each(function () {
        const $row = $(this);
        const rowKey = $row.data('key');
        const existing = purchaseOrderDetails.find(function (item) {
            return item.tempKey === rowKey;
        }) || {};

        refreshedDetails.push(normalizeDetail({
            tempKey: rowKey,
            isPersisted: existing.isPersisted === true || existing.IsPersisted === true,
            itemID: existing.itemID || existing.ItemID || 0,
            prDetailId: existing.prDetailId || existing.PrDetailId || 0,
            itemCode: existing.itemCode || existing.ItemCode || '',
            itemName: existing.itemName || existing.ItemName || '',
            unit: existing.unit || existing.Unit || '',
            quantity: $row.find('.po-detail-qty').length ? toNumber($row.find('.po-detail-qty').val()) : toNumber(existing.quantity || existing.Quantity || 0),
            unitPrice: $row.find('.po-detail-price').length ? toNumber($row.find('.po-detail-price').val()) : toNumber(existing.unitPrice || existing.UnitPrice || 0),
            poAmount: $row.find('.po-detail-amount').length ? toNumber($row.find('.po-detail-amount').val()) : toNumber(existing.poAmount || existing.POAmount || 0),
            recDept: $row.find('.po-detail-dept').length ? ($row.find('.po-detail-dept').val() || '') : (existing.recDept || existing.RecDept || ''),
            recDeptName: existing.recDeptName || existing.RecDeptName || '',
            note: $row.find('.po-detail-note').length ? ($row.find('.po-detail-note').val() || '') : (existing.note || existing.Note || ''),
            recQty: $row.find('.po-detail-recqty').length ? toNumber($row.find('.po-detail-recqty').val()) : toNumber(existing.recQty || existing.RecQty || 0),
            recAmount: $row.find('.po-detail-recamount').length ? toNumber($row.find('.po-detail-recamount').val()) : toNumber(existing.recAmount || existing.RecAmount || 0),
            recDate: $row.find('.po-detail-recdate').length ? ($row.find('.po-detail-recdate').val() || null) : (existing.recDate || existing.RecDate || null),
            mrRequestNo: existing.mrRequestNo || existing.MRRequestNo || ''
        }));
    });

    purchaseOrderDetails = refreshedDetails;
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

        const availableQuantity = getAvailablePrLineQuantity(source);
        if (availableQuantity <= 0) {
            return;
        }

        purchaseOrderDetails.push(normalizeDetail({
            ItemID: source.itemID || source.ItemID || 0,
            ItemCode: source.itemCode || source.ItemCode || '',
            ItemName: source.itemName || source.ItemName || '',
            Unit: source.unit || source.Unit || '',
            Quantity: availableQuantity,
            UnitPrice: source.unitPrice || source.UnitPrice || 0,
            POAmount: availableQuantity * toNumber(source.unitPrice || source.UnitPrice || 0),
            Note: source.remark || source.Remark || '',
            MRRequestNo: $('#PRID option:selected').text() || '',
            PrDetailId: source.prDetailId || source.PrDetailId || 0,
            IsPersisted: false,
            SupplierID: expectedSupplierId || null,
            SupplierText: supplierText
        }));
    });

    renderDetailRows();
    updateTotals();
    $('#purchaseOrderAddDetailModal').modal('hide');
    syncPrLineActionState();
}

// Dong bo checkbox Check All theo tung dong PR.
function syncPrLineCheckAllState() {
    const $checkAll = $('#purchaseOrderPrCheckAll');
    const $checks = $('#purchaseOrderPrLineRows .po-pr-check');

    if (!$checkAll.length) {
        return;
    }

    const $enabledChecks = $checks.not(':disabled');
    if (!$enabledChecks.length) {
        $checkAll.prop('checked', false);
        $checkAll.prop('indeterminate', false);
        $checkAll.prop('disabled', true);
        return;
    }

    const checkedCount = $enabledChecks.filter(':checked').length;
    $checkAll.prop('disabled', false);
    $checkAll.prop('checked', checkedCount > 0 && checkedCount === $enabledChecks.length);
    $checkAll.prop('indeterminate', checkedCount > 0 && checkedCount < $enabledChecks.length);
}

function syncPrLineActionState() {
    const hasSelectedRows = $('#purchaseOrderPrLineRows .po-pr-check:checked').not(':disabled').length > 0;
    $('#btnConfirmAddDetail').prop('disabled', !hasSelectedRows);
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
            isPersisted: row.isPersisted === true || row.IsPersisted === true,
            itemID: row.itemID,
            prDetailId: row.prDetailId,
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
