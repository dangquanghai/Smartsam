(function () {
    'use strict';

    function initializePage() {
        bindEvents();
        loadPreview();
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
        $('#linenReportDescriptionLabel').text(response.labelText || 'Des');
        rebuildSelect($('#linenReportDescription'), response.descriptions || [], response.selectedDescriptionId);
        $('#linenReportDescription').prop('disabled', response.descriptionEnabled !== true);
        $('#linenReportLinenCode').prop('disabled', response.linenEnabled !== true);
        $('#linenReportFromDate').prop('disabled', response.fromEnabled !== true);
        $('#linenReportToDate').prop('disabled', response.toEnabled !== true);
        $('#linenReportChart').prop('disabled', response.chartEnabled !== true);
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
            $('#linenReportPreviewMeta').text(validationMessage);
            $('#linenReportPreviewContainer').html('<div class="text-center text-muted py-5">No preview data.</div>');
            return;
        }

        $('#linenReportPreviewMeta').text('Loading report data...');
        $('#linenReportPreviewContainer').html('<div class="text-center text-muted py-5">Loading report data...</div>');

        $.ajax({
            url: window.linenReportPage?.previewUrl || '',
            type: 'GET',
            data: {
                reportType: getSelectedReportType(),
                descriptionId: $('#linenReportDescription').val() || '',
                linenCode: $('#linenReportLinenCode').prop('disabled') ? '' : ($('#linenReportLinenCode').val() || ''),
                fromDate: $('#linenReportFromDate').prop('disabled') ? '' : ($('#linenReportFromDate').val() || ''),
                toDate: $('#linenReportToDate').prop('disabled') ? '' : ($('#linenReportToDate').val() || '')
            },
            success: function (response) {
                if (!response || response.success !== true) {
                    const message = response && response.message ? response.message : 'Cannot load report preview.';
                    $('#linenReportPreviewMeta').text(message);
                    $('#linenReportPreviewContainer').html(`<div class="alert alert-danger mb-0">${encodeHtml(message)}</div>`);
                    return;
                }

                renderPreview(response);
            },
            error: function (xhr) {
                const message = readAjaxError(xhr, 'Cannot load report preview.');
                $('#linenReportPreviewMeta').text(message);
                $('#linenReportPreviewContainer').html(`<div class="alert alert-danger mb-0">${encodeHtml(message)}</div>`);
            }
        });
    }

    function validatePreviewFilter() {
        const descriptionDisabled = $('#linenReportDescription').prop('disabled');
        const fromDisabled = $('#linenReportFromDate').prop('disabled');
        const toDisabled = $('#linenReportToDate').prop('disabled');

        if (!descriptionDisabled && !$('#linenReportDescription').val()) {
            return 'Please select description/apartment.';
        }

        if (!fromDisabled && !$('#linenReportFromDate').val()) {
            return 'Please select From Date.';
        }

        if (!toDisabled && !$('#linenReportToDate').val()) {
            return 'Please select To Date.';
        }

        if (!fromDisabled && !toDisabled) {
            const fromValue = $('#linenReportFromDate').val() || '';
            const toValue = $('#linenReportToDate').val() || '';
            if (fromValue && toValue && fromValue > toValue) {
                return 'From Date must be less than or equal to To Date.';
            }
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
                $('#linenReportPreviewMeta').text('Unknown report type.');
                $('#linenReportPreviewContainer').html('<div class="text-center text-muted py-5">No preview data.</div>');
                break;
        }
    }

    function renderPantryPreview(response) {
        const columns = Array.isArray(response.columns) ? response.columns : [];
        const rows = Array.isArray(response.rows) ? response.rows : [];
        $('#linenReportPreviewMeta').text(`Note: ${response.description || ''} | Date: ${response.dateText || ''}`);

        if (columns.length === 0 || rows.length === 0) {
            $('#linenReportPreviewContainer').html('<div class="text-center text-muted py-5">No preview data.</div>');
            return;
        }

        let html = '<table class="table table-bordered table-sm mb-0 linen-report-page-preview-table linen-report-page-pantry-table"><thead><tr>';
        html += '<th rowspan="2">Pantry</th><th rowspan="2">A/P</th>';
        columns.forEach(function (column) {
            html += `<th colspan="3">${encodeHtml(column.title || '')}</th>`;
        });
        html += '</tr><tr>';
        columns.forEach(function () {
            html += '<th>Be</th><th>De</th><th>Re</th>';
        });
        html += '</tr></thead><tbody>';

        rows.forEach(function (row) {
            html += '<tr>';
            html += `<td class="linen-report-page-pantry">${encodeHtml(row.pentryName || '')}</td>`;
            html += `<td class="linen-report-page-time">${row.timeSection === 1 ? 'A' : 'P'}</td>`;
            columns.forEach(function (column) {
                html += `<td class="text-right">${formatWholeNumber(row[column.beField])}</td>`;
                html += `<td class="text-right">${formatWholeNumber(row[column.deField])}</td>`;
                html += `<td class="text-right">${formatWholeNumber(row[column.reField])}</td>`;
            });
            html += '</tr>';
        });

        html += '</tbody></table>';
        $('#linenReportPreviewContainer').html(html);
    }

    function renderDeliveryPreview(response) {
        const rows = Array.isArray(response.rows) ? response.rows : [];
        const parts = [];
        if (response.description) {
            parts.push(`Delivery: ${response.description}`);
        }
        if (response.dateText) {
            parts.push(`Date: ${response.dateText}`);
        }
        if (response.supplierName) {
            parts.push(`Supplier: ${response.supplierName}`);
        }
        if (response.deliveryTypeName) {
            parts.push(`Type: ${response.deliveryTypeName}`);
        }
        $('#linenReportPreviewMeta').text(parts.join(' | '));

        if (rows.length === 0) {
            $('#linenReportPreviewContainer').html('<div class="text-center text-muted py-5">No preview data.</div>');
            return;
        }

        let html = '<table class="table table-bordered table-sm mb-0 linen-report-page-preview-table linen-report-page-doc-table"><thead><tr>';
        html += '<th>Location</th><th>Linnen</th><th>Child</th><th>Express</th><th>Qty</th><th>Price</th><th>Amount</th><th>Note</th>';
        html += '</tr></thead><tbody>';

        rows.forEach(function (row) {
            html += '<tr>';
            html += `<td class="vni-font">${encodeHtml(row.location || '')}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
            html += `<td class="text-center">${row.isChild === true ? 'Y' : ''}</td>`;
            html += `<td class="text-center">${row.express === true ? 'Y' : ''}</td>`;
            html += `<td class="text-right">${formatDecimal(row.quantity)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.price)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.amount)}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.note || '')}</td>`;
            html += '</tr>';
        });

        html += '</tbody></table>';
        $('#linenReportPreviewContainer').html(html);
    }

    function renderReceivePreview(response) {
        const rows = Array.isArray(response.rows) ? response.rows : [];
        const parts = [];
        if (response.description) {
            parts.push(`Receive: ${response.description}`);
        }
        if (response.dateText) {
            parts.push(`Date: ${response.dateText}`);
        }
        if (response.supplierName) {
            parts.push(`Supplier: ${response.supplierName}`);
        }
        if (response.refDeliveryDescription) {
            parts.push(`Ref Delivery: ${response.refDeliveryDescription}`);
        }
        $('#linenReportPreviewMeta').text(parts.join(' | '));

        if (rows.length === 0) {
            $('#linenReportPreviewContainer').html('<div class="text-center text-muted py-5">No preview data.</div>');
            return;
        }

        let html = '<table class="table table-bordered table-sm mb-0 linen-report-page-preview-table linen-report-page-doc-table"><thead><tr>';
        html += '<th>Location</th><th>Linnen</th><th>Child</th><th>Express</th><th>Qty</th><th>Price</th><th>Amount</th><th>Note</th>';
        html += '</tr></thead><tbody>';

        rows.forEach(function (row) {
            html += '<tr>';
            html += `<td class="vni-font">${encodeHtml(row.location || '')}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
            html += `<td class="text-center">${row.isChild === true ? 'Y' : ''}</td>`;
            html += `<td class="text-center">${row.express === true ? 'Y' : ''}</td>`;
            html += `<td class="text-right">${formatDecimal(row.quantity)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.price)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.amount)}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.note || '')}</td>`;
            html += '</tr>';
        });

        html += '</tbody></table>';
        $('#linenReportPreviewContainer').html(html);
    }

    function renderLaundryRecordPreview(response) {
        const groups = Array.isArray(response.groups) ? response.groups : [];
        $('#linenReportPreviewMeta').text(`From Date: ${response.fromDate || ''} | To Date: ${response.toDate || ''}`);

        if (groups.length === 0) {
            $('#linenReportPreviewContainer').html('<div class="text-center text-muted py-5">No preview data.</div>');
            return;
        }

        let html = '<table class="table table-bordered table-sm mb-0 linen-report-page-preview-table linen-report-page-record-table"><thead><tr>';
        html += '<th>LinenCode</th><th>Price</th>';
        for (let day = 1; day <= 31; day++) {
            html += `<th>${String(day).padStart(2, '0')}</th>`;
        }
        html += '<th>Total</th></tr></thead><tbody>';

        groups.forEach(function (group) {
            const rows = Array.isArray(group.rows) ? group.rows : [];
            html += `<tr><td colspan="34" class="linen-report-page-record-supplier vni-font">${encodeHtml(group.supplierName || '')}</td></tr>`;
            html += `<tr><td colspan="34" class="linen-report-page-record-group">${encodeHtml(group.groupName || '')}</td></tr>`;

            const totals = new Array(31).fill(0);
            let groupTotal = 0;

            rows.forEach(function (row) {
                const dayValues = Array.isArray(row.days) ? row.days : [];
                html += '<tr>';
                html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
                html += `<td class="text-right">${formatDecimal(row.price)}</td>`;
                dayValues.forEach(function (value, index) {
                    const numeric = parseNumber(value);
                    totals[index] += numeric;
                    groupTotal += numeric;
                    html += `<td class="text-right">${formatDecimal(value)}</td>`;
                });
                html += `<td class="text-right">${formatDecimal(row.total)}</td>`;
                html += '</tr>';
            });

            html += '<tr class="font-weight-bold">';
            html += '<td>Total</td><td></td>';
            totals.forEach(function (value) {
                html += `<td class="text-right">${formatDecimal(value)}</td>`;
            });
            html += `<td class="text-right">${formatDecimal(groupTotal)}</td>`;
            html += '</tr>';
        });

        html += '</tbody></table>';
        $('#linenReportPreviewContainer').html(html);
    }

    function renderNotReceivePreview(response) {
        const rows = Array.isArray(response.rows) ? response.rows : [];
        $('#linenReportPreviewMeta').text('Not Receive report');

        if (rows.length === 0) {
            $('#linenReportPreviewContainer').html('<div class="text-center text-muted py-5">No preview data.</div>');
            return;
        }

        let html = '<table class="table table-bordered table-sm mb-0 linen-report-page-preview-table linen-report-page-doc-table"><thead><tr>';
        html += '<th>Delivery ID</th><th>Date</th><th>Description</th><th>Supplier</th><th>Linnen</th><th>Quantity De</th><th>Quantity Re</th><th>Remain</th>';
        html += '</tr></thead><tbody>';

        rows.forEach(function (row) {
            html += '<tr>';
            html += `<td class="text-right">${encodeHtml(row.deliveryId || '')}</td>`;
            html += `<td class="text-center">${encodeHtml(row.deliveryDate || '')}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.description || '')}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.supplierName || '')}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
            html += `<td class="text-right">${formatDecimal(row.quantityDe)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.quantityRe)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.remain)}</td>`;
            html += '</tr>';
        });

        html += '</tbody></table>';
        $('#linenReportPreviewContainer').html(html);
    }

    function renderLaundryBalancePreview(response) {
        const rows = Array.isArray(response.rows) ? response.rows : [];
        $('#linenReportPreviewMeta').text(`From Date: ${response.fromDate || ''} | To Date: ${response.toDate || ''}`);

        if (rows.length === 0) {
            $('#linenReportPreviewContainer').html('<div class="text-center text-muted py-5">No preview data.</div>');
            return;
        }

        let html = '<table class="table table-bordered table-sm mb-0 linen-report-page-preview-table linen-report-page-balance-table"><thead><tr>';
        html += '<th>LinenCode</th><th>Begin</th><th>RApmt</th><th>R Supplier</th><th>D Apmt</th><th>D Supplier</th><th>End</th>';
        html += '</tr></thead><tbody>';

        rows.forEach(function (row) {
            html += '<tr>';
            html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
            html += `<td class="text-right">${formatDecimal(row.begin)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.receiveApartment)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.receiveSupplier)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.deliveryApartment)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.deliverySupplier)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.end)}</td>`;
            html += '</tr>';
        });

        html += '</tbody></table>';
        $('#linenReportPreviewContainer').html(html);
    }

    function renderApartmentBalancePreview(response) {
        const rows = Array.isArray(response.rows) ? response.rows : [];
        $('#linenReportPreviewMeta').text(`Apartment: ${response.apartmentNo || ''} | From Date: ${response.fromDate || ''} | To Date: ${response.toDate || ''}`);

        if (rows.length === 0) {
            $('#linenReportPreviewContainer').html('<div class="text-center text-muted py-5">No preview data.</div>');
            return;
        }

        let html = '<table class="table table-bordered table-sm mb-0 linen-report-page-preview-table linen-report-page-balance-table"><thead><tr>';
        html += '<th>Linen</th><th>TonDau</th><th>NhapVaoCanH</th><th>XuatRaTuCanH</th><th>TonCuoi</th>';
        html += '</tr></thead><tbody>';

        rows.forEach(function (row) {
            html += '<tr>';
            html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
            html += `<td class="text-right">${formatDecimal(row.begin)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.receiveApartment)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.deliveryApartment)}</td>`;
            html += `<td class="text-right">${formatDecimal(row.end)}</td>`;
            html += '</tr>';
        });

        html += '</tbody></table>';
        $('#linenReportPreviewContainer').html(html);
    }

    function getSelectedReportType() {
        return $('input[name="linenReportType"]:checked').val() || window.linenReportPage?.initialType || 'laundry-record';
    }

    function formatWholeNumber(value) {
        const numeric = parseNumber(value);
        if (!Number.isFinite(numeric) || numeric === 0) {
            return '';
        }

        return numeric.toString();
    }

    function formatDecimal(value) {
        const numeric = parseNumber(value);
        if (!Number.isFinite(numeric) || numeric === 0) {
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
