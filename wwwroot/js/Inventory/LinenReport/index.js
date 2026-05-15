(function () {
    'use strict';

    function initializePage() {
        bindEvents();
        if (window.linenReportPage?.autoPreview) {
            loadPreview();
        }
    }

    function bindEvents() {
        $('input[name="linenReportType"]').off('change').on('change', function () {
            loadModeOptions();
        });

        $('#btnPreviewLinenReportPage').off('click').on('click', function () {
            loadPreview();
        });
    }

    function loadModeOptions() {
        $.ajax({
            url: window.linenReportPage?.modeOptionsUrl || '',
            type: 'GET',
            data: {
                reportType: getSelectedReportType(),
                descriptionId: $('#linenReportDescription').val() || ''
            },
            success: function (response) {
                if (!response || response.success !== true) {
                    alert(response && response.message ? response.message : 'Cannot load report options.');
                    return;
                }

                applyModeState(response);
                loadPreview();
            },
            error: function (xhr) {
                alert(readAjaxError(xhr, 'Cannot load report options.'));
            }
        });
    }

    function applyModeState(response) {
        syncRadioState(response.reportType || '');
        $('#linenReportDescriptionLabel').text(response.labelText || 'Des');
        rebuildSelect($('#linenReportDescription'), response.descriptions || [], response.selectedDescriptionId);
        $('#linenReportDescription').prop('disabled', response.descriptionEnabled !== true);
        $('#linenReportLinenCode').prop('disabled', response.linenEnabled !== true);
        $('#linenReportFromDate').prop('disabled', response.fromEnabled !== true);
        $('#linenReportToDate').prop('disabled', response.toEnabled !== true);
        $('#linenReportChart').prop('disabled', response.chartEnabled !== true);
    }

    function syncRadioState(reportType) {
        if (!reportType) {
            return;
        }

        const $target = $(`input[name="linenReportType"][value="${reportType}"]`);
        if ($target.length > 0) {
            $target.prop('checked', true);
        }
    }

    function rebuildSelect($select, items, selectedValue) {
        const html = (items || []).map(function (item) {
            const isSelected = String(item.value || '') === String(selectedValue || '');
            return `<option value="${encodeHtml(item.value || '')}"${isSelected ? ' selected' : ''}>${encodeHtml(item.text || '')}</option>`;
        }).join('');
        $select.html(html);
    }

    function loadPreview() {
        const validationMessage = validatePreviewFilter();
        if (validationMessage) {
            showPreviewMessage(validationMessage, validationMessage);
            return;
        }

        $('#linenReportPreviewMeta').text('Loading report data...');
        renderPaper('<div class="linen-report-empty">Loading report data...</div>');

        $.ajax({
            url: window.linenReportPage?.previewUrl || '',
            type: 'GET',
            data: {
                reportType: getSelectedReportType(),
                descriptionId: $('#linenReportDescription').prop('disabled') ? '' : ($('#linenReportDescription').val() || ''),
                linenCode: $('#linenReportLinenCode').prop('disabled') ? '' : ($('#linenReportLinenCode').val() || ''),
                fromDate: $('#linenReportFromDate').prop('disabled') ? '' : ($('#linenReportFromDate').val() || ''),
                toDate: $('#linenReportToDate').prop('disabled') ? '' : ($('#linenReportToDate').val() || '')
            },
            success: function (response) {
                if (!response || response.success !== true) {
                    const message = response && response.message ? response.message : 'Cannot load report preview.';
                    showPreviewMessage(message, message);
                    return;
                }

                renderPreview(response);
            },
            error: function (xhr) {
                const message = readAjaxError(xhr, 'Cannot load report preview.');
                showPreviewMessage(message, message);
            }
        });
    }

    function showPreviewMessage(statusText, bodyText) {
        $('#linenReportPreviewMeta').text(statusText);
        renderPaper(`<div class="linen-report-empty">${encodeHtml(bodyText)}</div>`);
    }

    function validatePreviewFilter() {
        if (!$('#linenReportDescription').prop('disabled') && !$('#linenReportDescription').val()) {
            return 'Please select description/apartment.';
        }

        if (!$('#linenReportFromDate').prop('disabled') && !$('#linenReportFromDate').val()) {
            return 'Please select From Date.';
        }

        if (!$('#linenReportToDate').prop('disabled') && !$('#linenReportToDate').val()) {
            return 'Please select To Date.';
        }

        const fromValue = $('#linenReportFromDate').val() || '';
        const toValue = $('#linenReportToDate').val() || '';
        if (fromValue && toValue && fromValue > toValue) {
            return 'From Date must be less than or equal to To Date.';
        }

        return '';
    }

    function renderPreview(response) {
        switch (response.reportType) {
            case 'pantry':
                renderPantryPreview(response);
                break;
            case 'delivery':
                renderDeliveryPreview(response);
                break;
            case 'receive':
                renderReceivePreview(response);
                break;
            case 'laundry-record':
                renderLaundryRecordPreview(response);
                break;
            case 'not-receive':
                renderNotReceivePreview(response);
                break;
            case 'laundry-balance':
                renderLaundryBalancePreview(response);
                break;
            case 'apmt-balance':
                renderApartmentBalancePreview(response);
                break;
            default:
                showPreviewMessage('Unknown report type.', 'Unknown report type.');
                break;
        }
    }

    function renderPantryPreview(response) {
        const columns = Array.isArray(response.columns) ? response.columns : [];
        const rows = Array.isArray(response.rows) ? response.rows : [];
        $('#linenReportPreviewMeta').text(`Pantry-Linen | ${response.description || ''}`);

        if (columns.length === 0 || rows.length === 0) {
            renderPaper('<div class="linen-report-empty">No preview data.</div>');
            return;
        }

        let html = buildHeader('DAILY NOTE LINEN CONTROL', formatSlashDate(response.dateText), '');
        html += '<table class="linen-report-info-table">';
        html += `<tr><td style="width:140px;"><strong>Pickup ID:</strong></td><td style="width:180px;">${encodeHtml(response.descriptionId || '')}</td><td style="width:100px;"><strong>Date:</strong></td><td>${encodeHtml(formatSlashDate(response.dateText))}</td></tr>`;
        html += `<tr><td><strong>Des</strong></td><td colspan="3" class="vni-font">${encodeHtml(response.description || '')}</td></tr>`;
        html += '</table>';

        html += '<table class="linen-report-table linen-report-pantry-table">';
        html += '<thead><tr><th rowspan="2" style="min-width:88px;">NAME</th><th rowspan="2" style="width:28px;"></th>';
        columns.forEach(function (column) {
            html += `<th colspan="3">${encodeHtml(column.title || '')}</th>`;
        });
        html += '</tr><tr>';
        columns.forEach(function () {
            html += '<th style="width:26px;">B</th><th style="width:26px;">R</th><th style="width:26px;">D</th>';
        });
        html += '</tr></thead><tbody>';

        rows.forEach(function (row) {
            html += '<tr>';
            html += `<td class="linen-report-pantry-name vni-font">${encodeHtml(row.pentryName || '')}</td>`;
            html += `<td class="linen-report-pantry-time">${row.timeSection === 1 ? 'A' : 'P'}</td>`;
            columns.forEach(function (column) {
                html += `<td class="text-right">${formatIntegerOrBlank(row[column.beField])}</td>`;
                html += `<td class="text-right">${formatIntegerOrBlank(row[column.reField])}</td>`;
                html += `<td class="text-right">${formatIntegerOrBlank(row[column.deField])}</td>`;
            });
            html += '</tr>';
        });

        html += '</tbody></table>';
        renderPaper(html);
    }

    function renderDeliveryPreview(response) {
        const rows = Array.isArray(response.rows) ? response.rows : [];
        $('#linenReportPreviewMeta').text(`Delivery | ${response.description || ''}`);

        if (rows.length === 0) {
            renderPaper('<div class="linen-report-empty">No preview data.</div>');
            return;
        }

        let html = buildHeader('', formatShortDate(response.dateText), '');
        html += '<table class="linen-report-info-table">';
        html += `<tr><td style="width:120px;"><strong>DeliveryID</strong></td><td style="width:210px;">${encodeHtml(response.deliveryId || '')}</td><td style="width:120px;"></td><td></td></tr>`;
        html += `<tr><td><strong>DeliveryDate</strong></td><td>${encodeHtml(formatShortDate(response.dateText))}</td><td></td><td></td></tr>`;
        html += `<tr><td><strong>SupplierName</strong></td><td colspan="3" class="vni-font">${encodeHtml(response.supplierName || '')}</td></tr>`;
        html += `<tr><td><strong>LaundryTypeName</strong></td><td colspan="3" class="vni-font">${encodeHtml(response.deliveryTypeName || '')}</td></tr>`;
        html += `<tr><td><strong>Des</strong></td><td colspan="3" class="vni-font">${encodeHtml(response.description || '')}</td></tr>`;
        html += '</table>';

        html += '<table class="linen-report-table">';
        html += '<thead><tr><th style="width:36px;">No.</th><th>Location</th><th>LinenCode</th><th style="width:78px;">Quantity</th><th style="width:88px;">Price</th><th style="width:92px;">Amount</th><th>Note</th></tr></thead><tbody>';
        let totalAmount = 0;
        rows.forEach(function (row, index) {
            const amount = parseNumber(row.amount);
            totalAmount += amount;
            html += '<tr>';
            html += `<td class="text-center">${index + 1}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.location || '')}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.quantity, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.price, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.amount, false)}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.note || '')}</td>`;
            html += '</tr>';
        });
        html += `<tr><td colspan="5"></td><td class="text-right"><strong>${formatDecimalOrZero(totalAmount, false)}</strong></td><td></td></tr>`;
        html += '</tbody></table>';
        html += buildSignatureBlock('Deliverer', 'Receiver');
        renderPaper(html);
    }

    function renderReceivePreview(response) {
        const rows = Array.isArray(response.rows) ? response.rows : [];
        $('#linenReportPreviewMeta').text(`Receive | ${response.description || ''}`);

        if (rows.length === 0) {
            renderPaper('<div class="linen-report-empty">No preview data.</div>');
            return;
        }

        let html = buildHeader('', formatShortDate(response.dateText), '');
        html += '<table class="linen-report-info-table">';
        html += `<tr><td style="width:120px;"><strong>ReceiveID</strong></td><td style="width:210px;">${encodeHtml(response.receiveId || '')}</td><td style="width:120px;"></td><td></td></tr>`;
        html += `<tr><td><strong>Receive Date</strong></td><td>${encodeHtml(formatShortDate(response.dateText))}</td><td></td><td></td></tr>`;
        html += `<tr><td><strong>SupplierName</strong></td><td colspan="3" class="vni-font">${encodeHtml(response.supplierName || '')}</td></tr>`;
        html += `<tr><td><strong>Des</strong></td><td colspan="3" class="vni-font">${encodeHtml(response.description || '')}</td></tr>`;
        html += `<tr><td><strong>Ref Delivery</strong></td><td colspan="3" class="vni-font">${encodeHtml(response.refDeliveryDescription || '')}</td></tr>`;
        html += '</table>';

        html += '<table class="linen-report-table">';
        html += '<thead><tr><th style="width:36px;">No.</th><th>Location</th><th>LinenCode</th><th style="width:78px;">Quantity</th><th style="width:88px;">Price</th><th style="width:92px;">Amount</th><th>Note</th></tr></thead><tbody>';
        let totalAmount = 0;
        rows.forEach(function (row, index) {
            const amount = parseNumber(row.amount);
            totalAmount += amount;
            html += '<tr>';
            html += `<td class="text-center">${index + 1}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.location || '')}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.quantity, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.price, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.amount, false)}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.note || '')}</td>`;
            html += '</tr>';
        });
        html += `<tr><td colspan="5"></td><td class="text-right"><strong>${formatDecimalOrZero(totalAmount, false)}</strong></td><td></td></tr>`;
        html += '</tbody></table>';
        html += buildSignatureBlock('Deliverer', 'Receiver');
        renderPaper(html);
    }

    function renderLaundryRecordPreview(response) {
        const groups = Array.isArray(response.groups) ? response.groups : [];
        $('#linenReportPreviewMeta').text(`Laundry Record | ${response.fromDate || ''} - ${response.toDate || ''}`);

        if (groups.length === 0) {
            renderPaper('<div class="linen-report-empty">No preview data.</div>');
            return;
        }

        let html = buildHeader('LINEN-LAUDRY RECORD', '', '');
        html += '<table class="linen-report-info-table">';
        html += `<tr><td style="width:120px;"><strong>From Dat</strong></td><td style="width:180px;">${encodeHtml(formatShortDate(response.fromDate))}</td><td style="width:100px;"><strong>To Date:</strong></td><td>${encodeHtml(formatShortDate(response.toDate))}</td></tr>`;
        html += '</table>';

        html += '<table class="linen-report-table">';
        html += '<thead><tr><th style="min-width:100px;">LinenCode</th><th style="width:62px;">Price</th>';
        for (let day = 1; day <= 31; day++) {
            html += `<th style="width:30px;">${String(day).padStart(2, '0')}</th>`;
        }
        html += '<th style="width:58px;">Total QT</th></tr></thead><tbody>';

        groups.forEach(function (group) {
            html += `<tr class="linen-report-group-row"><td colspan="34" class="vni-font">${encodeHtml(group.supplierName || '')}</td></tr>`;
            html += `<tr class="linen-report-subgroup-row"><td colspan="34">${encodeHtml(group.groupName || '')}</td></tr>`;

            const totals = new Array(31).fill(0);
            let groupTotal = 0;
            (group.rows || []).forEach(function (row) {
                html += '<tr>';
                html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
                html += `<td class="text-right">${formatDecimalOrZero(row.price, false)}</td>`;
                (row.days || []).forEach(function (value, index) {
                    const numeric = parseNumber(value);
                    totals[index] += numeric;
                    groupTotal += numeric;
                    html += `<td class="text-right">${formatDecimalOrZero(value, true)}</td>`;
                });
                html += `<td class="text-right">${formatDecimalOrZero(row.total, false)}</td>`;
                html += '</tr>';
            });

            html += '<tr class="linen-report-subgroup-row">';
            html += '<td>Total</td><td></td>';
            totals.forEach(function (value) {
                html += `<td class="text-right">${formatDecimalOrZero(value, true)}</td>`;
            });
            html += `<td class="text-right">${formatDecimalOrZero(groupTotal, false)}</td>`;
            html += '</tr>';
        });

        html += '</tbody></table>';
        renderPaper(html);
    }

    function renderNotReceivePreview(response) {
        const rows = Array.isArray(response.rows) ? response.rows : [];
        $('#linenReportPreviewMeta').text('Not Receive');

        if (rows.length === 0) {
            renderPaper('<div class="linen-report-empty">No preview data.</div>');
            return;
        }

        let html = buildHeader('LINEN NOT RECEIVE', '', '');
        html += '<table class="linen-report-table">';
        html += '<thead><tr><th style="width:72px;">DeliveryID</th><th style="width:90px;">Date</th><th>Description</th><th>Supplier</th><th>Linnen</th><th style="width:76px;">De</th><th style="width:76px;">Re</th><th style="width:76px;">Remain</th></tr></thead><tbody>';
        rows.forEach(function (row) {
            html += '<tr>';
            html += `<td class="text-right">${encodeHtml(row.deliveryId || '')}</td>`;
            html += `<td class="text-center">${encodeHtml(formatShortDate(row.deliveryDate || ''))}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.description || '')}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.supplierName || '')}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.quantityDe, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.quantityRe, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.remain, false)}</td>`;
            html += '</tr>';
        });
        html += '</tbody></table>';
        renderPaper(html);
    }

    function renderLaundryBalancePreview(response) {
        const rows = Array.isArray(response.rows) ? response.rows : [];
        $('#linenReportPreviewMeta').text(`Laundry Room Balance | ${response.fromDate || ''} - ${response.toDate || ''}`);

        if (rows.length === 0) {
            renderPaper('<div class="linen-report-empty">No preview data.</div>');
            return;
        }

        let html = buildHeader('LAUNDRY ROOM BALANCE', '', '');
        html += '<table class="linen-report-info-table">';
        html += `<tr><td style="width:120px;"><strong>From Dat</strong></td><td style="width:180px;">${encodeHtml(formatShortDate(response.fromDate))}</td><td style="width:100px;"><strong>To Date</strong></td><td>${encodeHtml(formatShortDate(response.toDate))}</td></tr>`;
        html += '</table>';
        html += '<table class="linen-report-table" style="max-width:720px;">';
        html += '<thead><tr><th>LinenCode</th><th>Begin</th><th>R Apmt</th><th>R Supplier</th><th>D Apmt</th><th>D Supplier</th><th>End</th></tr></thead><tbody>';
        rows.forEach(function (row) {
            html += '<tr>';
            html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.begin, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.receiveApartment, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.receiveSupplier, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.deliveryApartment, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.deliverySupplier, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.end, false)}</td>`;
            html += '</tr>';
        });
        html += '</tbody></table>';
        renderPaper(html);
    }

    function renderApartmentBalancePreview(response) {
        const rows = Array.isArray(response.rows) ? response.rows : [];
        $('#linenReportPreviewMeta').text(`Apartment Balance | ${response.apartmentNo || ''}`);

        if (rows.length === 0) {
            renderPaper('<div class="linen-report-empty">No preview data.</div>');
            return;
        }

        let html = buildHeader('LAUNDRY ROOM BALANCE', formatSlashDate(new Date().toISOString().slice(0, 10)), '');
        html += '<table class="linen-report-info-table">';
        html += `<tr><td style="width:120px;"><strong>Apartment No:</strong></td><td style="width:180px;">${encodeHtml(response.apartmentNo || '')}</td><td></td><td></td></tr>`;
        html += `<tr><td><strong>From Dat</strong></td><td>${encodeHtml(formatShortDate(response.fromDate))}</td><td style="width:100px;"><strong>To Dat</strong></td><td>${encodeHtml(formatShortDate(response.toDate))}</td></tr>`;
        html += '</table>';
        html += '<table class="linen-report-table" style="max-width:620px;">';
        html += '<thead><tr><th>Linen</th><th>TonDau</th><th>NhapVaoCanH</th><th>XuatRaTuCanH</th><th>TonCuoi</th></tr></thead><tbody>';
        rows.forEach(function (row) {
            html += '<tr>';
            html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.begin, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.receiveApartment, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.deliveryApartment, false)}</td>`;
            html += `<td class="text-right">${formatDecimalOrZero(row.end, false)}</td>`;
            html += '</tr>';
        });
        html += '</tbody></table>';
        renderPaper(html);
    }

    function renderPaper(contentHtml) {
        $('#linenReportPreviewContainer').html(`<div class="linen-report-paper">${contentHtml}</div>`);
    }

    function buildHeader(title, rightDateText, extraRightHtml) {
        const titleHtml = title ? `<div class="linen-report-paper-title">${encodeHtml(title)}</div>` : '<div class="linen-report-paper-title">&nbsp;</div>';
        const rightHtml = extraRightHtml || (rightDateText ? encodeHtml(rightDateText) : '&nbsp;');
        return `
            <div class="linen-report-paper-header">
                <div class="linen-report-brand">
                    <div class="linen-report-brand-mark"></div>
                    <div>SAI GON</div>
                    <div>SKYGARDEN</div>
                </div>
                ${titleHtml}
                <div class="linen-report-paper-date">${rightHtml}</div>
            </div>`;
    }

    function buildSignatureBlock(leftLabel, rightLabel) {
        return `
            <div class="linen-report-signatures">
                <div class="linen-report-signature">
                    <div><strong>${encodeHtml(leftLabel)}</strong></div>
                    <div class="linen-report-signature-line"></div>
                </div>
                <div class="linen-report-signature">
                    <div><strong>${encodeHtml(rightLabel)}</strong></div>
                    <div class="linen-report-signature-line"></div>
                </div>
            </div>`;
    }

    function getSelectedReportType() {
        return $('input[name="linenReportType"]:checked').val() || window.linenReportPage?.initialType || 'laundry-record';
    }

    function formatIntegerOrBlank(value) {
        const numeric = parseNumber(value);
        if (!Number.isFinite(numeric) || numeric === 0) {
            return '';
        }

        return Math.trunc(numeric).toString();
    }

    function formatDecimalOrZero(value, blankZero) {
        const numeric = parseNumber(value);
        if (!Number.isFinite(numeric)) {
            return blankZero ? '' : '0';
        }

        if (numeric === 0 && blankZero) {
            return '';
        }

        if (Number.isInteger(numeric)) {
            return numeric.toString();
        }

        return numeric.toFixed(2).replace(/\.?0+$/, '');
    }

    function parseNumber(value) {
        const numeric = Number.parseFloat(value);
        return Number.isFinite(numeric) ? numeric : 0;
    }

    function formatShortDate(value) {
        if (!value) {
            return '';
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return value;
        }

        const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
        const day = String(date.getDate()).padStart(2, '0');
        const month = monthNames[date.getMonth()];
        const year = String(date.getFullYear()).slice(-2);
        return `${day}-${month}-${year}`;
    }

    function formatSlashDate(value) {
        if (!value) {
            return '';
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return value;
        }

        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        const year = date.getFullYear();
        return `${month}/${day}/${year}`;
    }

    function encodeHtml(value) {
        return $('<div>').text(value || '').html();
    }

    function readAjaxError(xhr, fallbackMessage) {
        if (xhr && xhr.responseJSON && xhr.responseJSON.message) {
            return xhr.responseJSON.message;
        }

        if (xhr && xhr.responseText) {
            return xhr.responseText;
        }

        return fallbackMessage;
    }

    $(document).ready(initializePage);
})();
