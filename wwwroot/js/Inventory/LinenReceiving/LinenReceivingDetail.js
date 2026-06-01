(function () {
    'use strict';

    let pageDirty = false;
    let activeReportObjectUrl = '';

    function initializePage() {
        bindFormSubmit();
        bindGridEvents();
        bindHeaderEvents();
        bindPrintEvents();
        bindDirtyTracking();
        initReportModal();
        recalcAllRows();
        pageDirty = false;
    }

    function bindFormSubmit() {
        $('#linenReceivingDetailForm').off('submit').on('submit', function (e) {
            e.preventDefault();
            if (!validateMainForm()) {
                return;
            }

            if (shouldConfirmDeliverySelection()) {
                if (!window.confirm('Are you sure to select this Delevery !')) {
                    return;
                }
            }

            pageDirty = false;
            $(this).off('submit').submit();
        });
    }

    function shouldConfirmDeliverySelection() {
        const currentSendId = $('#Header_SendID').val() || '';
        const initialSendId = (window.linenReceivingPage?.sendId ?? '').toString();
        return currentSendId !== '' && currentSendId !== initialSendId;
    }

    function validateMainForm() {
        if (!$('#Header_SendID').val()) {
            alert('Delivery is required.');
            $('#Header_SendID').trigger('focus');
            return false;
        }

        const rows = collectDetails();
        for (let i = 0; i < rows.length; i++) {
            const row = rows[i];
            const $row = $('#linenReceivingDetailTable tbody tr').eq(i);
            if (!row.sendID) {
                alert('Delivery is required for every detail row.');
                $row.find('.lr-delivery').trigger('focus');
                return false;
            }

            if (!row.locationID) {
                alert('Location is required for every detail row.');
                $row.find('.lr-location').trigger('focus');
                return false;
            }

            if (!row.linnenID) {
                alert('Linen is required for every detail row.');
                $row.find('.lr-linen').trigger('focus');
                return false;
            }
        }

        $('#DetailsJson').val(JSON.stringify(rows));
        return true;
    }

    function bindGridEvents() {
        $('#btnAddDetailRow').off('click').on('click', function () {
            addDetailRow();
            pageDirty = true;
        });

        $(document).off('click', '.js-remove-row').on('click', '.js-remove-row', function () {
            $(this).closest('tr').remove();
            ensureEmptyRow();
            pageDirty = true;
        });

        $(document).off('change', '.lr-linen').on('change', '.lr-linen', function () {
            const $row = $(this).closest('tr');
            refreshRowPriceFromLinen($row);
            recalcRow($row);
            pageDirty = true;
        });

        $(document).off('input change', '.lr-quantity').on('input change', '.lr-quantity', function () {
            const $row = $(this).closest('tr');
            refreshRowPriceFromLinen($row);
            recalcRow($row);
            pageDirty = true;
        });

        $(document).off('input change', '.lr-price').on('input change', '.lr-price', function (e) {
            if (e.type === 'change') {
                $(this).val(formatDecimal(parseDecimal($(this).val())));
            }
            recalcRow($(this).closest('tr'));
            pageDirty = true;
        });
    }

    function bindHeaderEvents() {
        $('#Header_SendID').off('change').on('change', syncDeliveryRent);
    }

    function bindDirtyTracking() {
        $('#linenReceivingDetailForm')
            .off('change.linenReceivingDirty input.linenReceivingDirty')
            .on('change.linenReceivingDirty input.linenReceivingDirty', ':input', function () {
                const type = ($(this).attr('type') || '').toLowerCase();
                if (type === 'hidden') {
                    return;
                }

                pageDirty = true;
            });
    }

    function bindPrintEvents() {
        $('#btnPrintLinenReceivingDetail').off('click').on('click', function () {
            if (pageDirty) {
                alert('Please save receiving before previewing report.');
                return;
            }

            if (!window.linenReceivingPage?.reportPdfUrl || !window.linenReceivingPage?.receiveId) {
                alert('Report preview is not available.');
                return;
            }

            $('#linenReceivingReportModal').modal('show');
            previewReportPdf();
        });

        $('#btnPreviewLinenReceivingReport').off('click').on('click', previewReportPdf);
    }

    function initReportModal() {
        $('#linenReceivingReportModal').off('hidden.bs.modal').on('hidden.bs.modal', function () {
            clearReportPreview(document.getElementById('linenReceivingReportFrame'));
        });
    }

    function previewReportPdf() {
        const reportPdfUrl = buildReportPdfUrl();
        const frame = document.getElementById('linenReceivingReportFrame');
        const $loading = $('#linenReceivingReportLoading');
        const linenCode = ($('#linenReceivingReportLinenCode').val() || '').toString();
        const description = ($('#linenReceivingReportDescription option:selected').text() || '').trim();
        const linenLabel = ($('#linenReceivingReportLinenCode option:selected').text() || 'All').trim();

        if (!reportPdfUrl || !frame) {
            alert('Report preview is not available.');
            return;
        }

        $('#linenReceivingReportMeta').text(`Receive | ${description}${linenCode ? ` | ${linenLabel}` : ''}`);
        $loading.show();
        clearReportPreview(frame);

        loadPdfPreview(frame, reportPdfUrl)
            .then(function (objectUrl) {
                activeReportObjectUrl = objectUrl;
                $loading.hide();
            })
            .catch(function (error) {
                $loading.hide();
                alert(error?.message || 'Cannot load report preview.');
            });
    }

    function buildReportPdfUrl() {
        const baseUrl = window.linenReceivingPage?.reportPdfUrl || '';
        if (!baseUrl) {
            return '';
        }

        const url = new URL(baseUrl, window.location.origin);
        const linenCode = ($('#linenReceivingReportLinenCode').val() || '').toString().trim();
        if (linenCode) {
            url.searchParams.set('linenCode', linenCode);
        } else {
            url.searchParams.delete('linenCode');
        }

        return `${url.pathname}${url.search}`;
    }

    async function loadPdfPreview(frame, url) {
        const response = await fetch(url, {
            method: 'GET',
            credentials: 'same-origin',
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        });

        if (!response.ok) {
            const contentType = String(response.headers.get('content-type') || '').toLowerCase();
            if (contentType.includes('application/json')) {
                const result = await response.json();
                throw new Error(result?.message || 'Cannot load report preview.');
            }

            throw new Error('Cannot load report preview.');
        }

        const contentType = String(response.headers.get('content-type') || '').toLowerCase();
        if (!contentType.includes('application/pdf')) {
            throw new Error('Cannot load report preview.');
        }

        const blob = await response.blob();
        const previewUrl = URL.createObjectURL(blob);
        frame.src = previewUrl;
        return previewUrl;
    }

    function clearReportPreview(frame) {
        if (frame) {
            frame.removeAttribute('src');
        }

        if (activeReportObjectUrl) {
            URL.revokeObjectURL(activeReportObjectUrl);
            activeReportObjectUrl = '';
        }
    }

    function syncDeliveryRent() {
        const sendId = $('#Header_SendID').val() || '';
        if (!sendId) {
            applyRent(false);
            return;
        }

        $.ajax({
            url: `${window.location.pathname}?handler=DeliveryInfo`,
            type: 'GET',
            data: { id: sendId },
            success: function (response) {
                if (!response || response.success !== true) {
                    return;
                }

                applyRent(response.isRent === true);
            }
        });
    }

    function applyRent(isRent) {
        $('#Header_IsRent').prop('checked', isRent);

        const $description = $('#Header_Description');
        const current = $description.val() || '';
        const bracketPos = current.indexOf('(');
        const baseText = bracketPos >= 0 ? current.substring(0, bracketPos) : current;
        const nextText = isRent ? `${baseText}(Rent)` : baseText;

        if (current !== nextText) {
            $description.val(nextText);
            pageDirty = true;
        }
    }

    function addDetailRow() {
        $('.linen-receiving-empty-row').remove();

        const headerSendId = ($('#Header_SendID').val() || window.linenReceivingPage?.sendId || '').toString();
        const deliveries = (window.linenReceivingPage?.rowTemplateOptions?.deliveries || []).map(function (item) {
            const selected = item.value && item.value.toString() === headerSendId ? ' selected' : '';
            return `<option value="${encodeHtml(item.value)}"${selected}>${encodeHtml(item.text)}</option>`;
        }).join('');

        const locations = (window.linenReceivingPage?.rowTemplateOptions?.locations || []).map(function (item) {
            return `<option value="${encodeHtml(item.value)}">${encodeHtml(item.text)}</option>`;
        }).join('');

        const linens = (window.linenReceivingPage?.rowTemplateOptions?.linens || []).map(function (item) {
            return `<option value="${encodeHtml(item.value)}" data-price="${encodeHtml(item.price)}">${encodeHtml(item.text)}</option>`;
        }).join('');

        const html = `
        <tr class="linen-receiving-detail-row" data-id="0">
            <td class="linen-receiving-action-cell">
                <div class="linen-receiving-action-wrap">
                    <button type="button" class="btn btn-xs btn-outline-danger border js-remove-row" title="Remove">
                        <i class="fas fa-trash"></i>
                    </button>
                </div>
            </td>
            <td><select class="form-control form-control-sm lr-delivery vni-font">${deliveries}</select></td>
            <td><select class="form-control form-control-sm lr-location vni-font">${locations}</select></td>
            <td><select class="form-control form-control-sm lr-linen vni-font">${linens}</select></td>
            <td class="text-center"><input type="checkbox" class="lr-express" /></td>
            <td class="text-center"><input type="checkbox" class="lr-child" /></td>
            <td><input type="number" step="0.01" min="0" class="form-control form-control-sm text-right lr-quantity" value="0.00" /></td>
            <td><input type="text" inputmode="decimal" class="form-control form-control-sm text-right lr-price" value="0.00" /></td>
            <td><input type="text" inputmode="decimal" class="form-control form-control-sm text-right lr-amount" value="0.00" readonly /></td>
            <td class="linen-receiving-note-cell"><input type="text" maxlength="100" class="form-control form-control-sm lr-note vni-font" value="" /></td>
        </tr>`;

        $('#linenReceivingDetailTable tbody').append(html);
    }

    function collectDetails() {
        const rows = [];

        $('#linenReceivingDetailTable tbody tr.linen-receiving-detail-row').each(function () {
            const $row = $(this);
            rows.push({
                id: parseInt($row.data('id') || 0, 10),
                receiveID: parseInt($('#Header_ReceiveID').val() || $('#Header_ReceiveID').attr('value') || 0, 10),
                sendID: parseNullableInt($row.find('.lr-delivery').val()) || parseNullableInt($('#Header_SendID').val()),
                locationID: parseNullableInt($row.find('.lr-location').val()),
                linnenID: parseNullableInt($row.find('.lr-linen').val()),
                express: $row.find('.lr-express').is(':checked'),
                isChild: $row.find('.lr-child').is(':checked'),
                quantity: parseDecimal($row.find('.lr-quantity').val()),
                price: parseDecimal($row.find('.lr-price').val()),
                amount: parseDecimal($row.find('.lr-amount').val()),
                note: $row.find('.lr-note').val() || ''
            });
        });

        return rows;
    }

    function recalcAllRows() {
        $('#linenReceivingDetailTable tbody tr.linen-receiving-detail-row').each(function () {
            recalcRow($(this));
        });
    }

    function refreshRowPriceFromLinen($row) {
        const selectedPrice = parseDecimal($row.find('.lr-linen option:selected').data('price'));
        $row.find('.lr-price').val(formatDecimal(selectedPrice));
    }

    function recalcRow($row) {
        const quantity = parseDecimal($row.find('.lr-quantity').val());
        const price = parseDecimal($row.find('.lr-price').val());
        const amount = quantity * price;
        $row.find('.lr-amount').val(formatDecimal(amount));
    }

    function ensureEmptyRow() {
        const rowCount = $('#linenReceivingDetailTable tbody tr.linen-receiving-detail-row').length;
        if (rowCount === 0) {
            $('#linenReceivingDetailTable tbody').html('<tr class="linen-receiving-empty-row"><td colspan="10" class="text-center text-muted py-3">No detail rows</td></tr>');
        }
    }

    function parseNullableInt(value) {
        if (value === null || value === undefined || value === '') {
            return null;
        }

        const parsed = parseInt(value, 10);
        return Number.isNaN(parsed) ? null : parsed;
    }

    function parseDecimal(value) {
        if (value === null || value === undefined || value === '') {
            return 0;
        }

        const parsed = parseFloat(value.toString().replace(/,/g, ''));
        return Number.isNaN(parsed) ? 0 : parsed;
    }

    function formatDecimal(value) {
        const numberValue = Number(value || 0);
        if (!Number.isFinite(numberValue)) {
            return '0.00';
        }

        return numberValue.toLocaleString('en-US', {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
    }

    function encodeHtml(value) {
        return $('<div>').text(value ?? '').html();
    }

    $(document).ready(initializePage);
})();
