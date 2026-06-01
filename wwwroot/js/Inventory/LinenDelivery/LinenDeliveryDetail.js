(function () {
    'use strict';

    let pageDirty = false;
    let allowUnload = false;
    let pendingNavigationUrl = '';
    let activeLinenDeliveryReportObjectUrl = '';

    function validateMainForm() {
        const typeValue = $('#Header_DeliveryType').val();
        const supplierValue = $('#Header_SupplierID').val();
        const pantryValue = $('#Header_NoteID').val();

        if (!typeValue) {
            alert('Type is required.');
            focusErrorField('#Header_DeliveryType');
            return false;
        }

        if (typeValue === '1') {
            if (!pantryValue) {
                alert('Pantry Linen is required.');
                focusErrorField('#Header_NoteID');
                return false;
            }

            if (!supplierValue) {
                alert('Supplier is required.');
                focusErrorField('#Header_SupplierID');
                return false;
            }
        }

        if ((typeValue === '2' || typeValue === '3') && !supplierValue) {
            alert('Supplier is required.');
            focusErrorField('#Header_SupplierID');
            return false;
        }

        const rows = collectDetails();
        for (let i = 0; i < rows.length; i++) {
            const row = rows[i];
            if (!row.locationID) {
                alert('Location is required for every detail row.');
                focusErrorField($('#linenDeliveryDetailTable tbody tr').eq(i).find('.ld-location'));
                return false;
            }

            if (!row.linnenID) {
                alert('Linen is required for every detail row.');
                focusErrorField($('#linenDeliveryDetailTable tbody tr').eq(i).find('.ld-linen'));
                return false;
            }
        }

        $('#DetailsJson').val(JSON.stringify(rows));
        return true;
    }

    function focusErrorField(target) {
        $(target).trigger('focus');
    }

    function initializePage() {
        bindFormSubmit();
        bindGridEvents();
        bindHeaderState();
        bindPrintEvents();
        bindBillEvents();
        bindPantryNoteViewEvents();
        bindDirtyTracking();
        initCloseButton();
        initUnsavedChangeGuard();
        initReportModal();
        initPantryNoteModal();
        refreshHeaderSections();
        updatePantryNoteViewButton();
        recalcAllRows();
        pageDirty = false;
    }

    function bindFormSubmit() {
        $('#linenDeliveryDetailForm').off('submit').on('submit', function (e) {
            e.preventDefault();
            if (!validateMainForm()) {
                resetRedirectFlags();
                return;
            }

            if (shouldConfirmPantrySelection()) {
                if (!window.confirm('Are you sure to select this Pantry Linen !')) {
                    resetRedirectFlags();
                    return;
                }
            }

            allowUnload = true;
            pageDirty = false;
            $(this).off('submit').submit();
        });
    }

    function shouldConfirmPantrySelection() {
        if (($('#Header_DeliveryType').val() || '') !== '1') {
            return false;
        }

        const currentNoteId = $('#Header_NoteID').val() || '';
        const initialNoteId = (window.linenDeliveryPage?.noteId ?? '').toString();
        return currentNoteId !== '' && currentNoteId !== initialNoteId;
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

        $(document).off('change', '.ld-linen').on('change', '.ld-linen', function () {
            const $row = $(this).closest('tr');
            const selectedPrice = parseDecimal($(this).find('option:selected').data('price'));
            $row.find('.ld-price').val(formatDecimal(selectedPrice));
            recalcRow($row);
            pageDirty = true;
        });

        $(document).off('input change', '.ld-quantity, .ld-price, .ld-express').on('input change', '.ld-quantity, .ld-price, .ld-express', function (e) {
            if (e.type === 'change' && ($(this).hasClass('ld-quantity') || $(this).hasClass('ld-price'))) {
                $(this).val(formatDecimal(parseDecimal($(this).val())));
            }
            recalcRow($(this).closest('tr'));
            pageDirty = true;
        });

        $(document).off('dblclick', '#linenDeliveryDetailTable tbody tr.linen-delivery-detail-row').on('dblclick', '#linenDeliveryDetailTable tbody tr.linen-delivery-detail-row', function (e) {
            if ($(e.target).closest('input, select, button, a, label').length > 0) {
                return;
            }

            markNoNeedBill($(this));
        });
    }

    function bindHeaderState() {
        $('#Header_DeliveryType').off('change').on('change', function () {
            refreshHeaderSections();
            refreshLookupOptions();
        });

        $('#Header_SupplierID').off('change').on('change', refreshLookupOptions);
        $('#Header_IsSpecialLaundry').off('change').on('change', function () {
            syncToBillButtonState($('#Header_DeliveryType').val() || '');
            refreshLookupOptions();
        });
        $('#Header_NoteID').off('change').on('change', syncPantryNoteState);
    }

    function bindDirtyTracking() {
        $('#linenDeliveryDetailForm')
            .off('change.linenDeliveryDirty input.linenDeliveryDirty')
            .on('change.linenDeliveryDirty input.linenDeliveryDirty', ':input', function () {
                const type = ($(this).attr('type') || '').toLowerCase();
                if (type === 'hidden') {
                    return;
                }

                pageDirty = true;
            });
    }

    function hasUnsavedChanges() {
        if (window.linenDeliveryPage?.isView) {
            return false;
        }

        return pageDirty;
    }

    function resetRedirectFlags() {
        $('#RedirectUrl').val('');
    }

    function initCloseButton() {
        $('#btnCloseLinenDeliveryDetail').off('click').on('click', function () {
            const indexUrl = $(this).data('index-url') || '/Inventory/LinenDelivery';

            if (!hasUnsavedChanges()) {
                allowUnload = true;
                window.location.href = indexUrl;
                return;
            }

            openUnsavedDialog(indexUrl);
        });
    }

    function isGuardedLink($link) {
        const href = $link.attr('href') || '';
        if (!href || href === '#' || href.startsWith('javascript:')) {
            return false;
        }

        if (href.startsWith('mailto:') || href.startsWith('tel:')) {
            return false;
        }

        if ($link.attr('target') === '_blank' || $link.attr('download')) {
            return false;
        }

        if ($link.data('skip-unsaved-check') === true) {
            return false;
        }

        return true;
    }

    function openUnsavedDialog(targetUrl) {
        pendingNavigationUrl = targetUrl || '';
        $('#linenDeliveryUnsavedModal').modal('show');
    }

    function navigateWithoutSaving(targetUrl) {
        allowUnload = true;
        $('#linenDeliveryUnsavedModal').modal('hide');
        if (targetUrl) {
            window.location.href = targetUrl;
        }
    }

    function saveAndNavigate(targetUrl) {
        resetRedirectFlags();
        $('#RedirectUrl').val(targetUrl || '');
        $('#linenDeliveryUnsavedModal').modal('hide');
        $('#linenDeliveryDetailForm').trigger('submit');
    }

    function initUnsavedChangeGuard() {
        if (window.linenDeliveryPage?.isView) {
            return;
        }

        resetRedirectFlags();

        $(document).off('click.linenDeliveryUnsaved', 'a[href]').on('click.linenDeliveryUnsaved', 'a[href]', function (ev) {
            if (!hasUnsavedChanges()) {
                return;
            }

            const $link = $(this);
            if (!isGuardedLink($link)) {
                return;
            }

            const href = $link.attr('href') || '';
            if (href === window.location.pathname + window.location.search) {
                return;
            }

            ev.preventDefault();
            openUnsavedDialog(href);
        });

        window.addEventListener('beforeunload', function (ev) {
            if (allowUnload || !hasUnsavedChanges()) {
                return;
            }

            ev.preventDefault();
            ev.returnValue = '';
        });

        $('#btnLinenDeliveryUnsavedSave').off('click').on('click', function () {
            saveAndNavigate(pendingNavigationUrl);
        });

        $('#btnLinenDeliveryUnsavedDiscard').off('click').on('click', function () {
            navigateWithoutSaving(pendingNavigationUrl);
        });

        $('#linenDeliveryUnsavedModal').off('shown.bs.modal').on('shown.bs.modal', function () {
            $('#btnLinenDeliveryUnsavedCancel').trigger('focus');
        });

        $('#linenDeliveryUnsavedModal').off('hidden.bs.modal').on('hidden.bs.modal', function () {
            pendingNavigationUrl = '';
        });
    }

    function bindBillEvents() {
        $('#btnToBill').off('click').on('click', function () {
            if ($(this).is(':disabled')) {
                return;
            }

            openBillModal();
        });

        $('#btnCreateLinenDeliveryBill').off('click').on('click', function () {
            createBill();
        });

        $(document).off('dblclick', '#linenDeliveryBillTableBody tr[data-bill-id]').on('dblclick', '#linenDeliveryBillTableBody tr[data-bill-id]', function () {
            openBillDetail($(this));
        });
    }

    function bindPrintEvents() {
        $('#btnPrintLinenDeliveryDetail').off('click').on('click', function () {
            if (pageDirty) {
                alert('Please save delivery before previewing report.');
                return;
            }

            const reportPdfUrl = window.linenDeliveryPage?.reportPdfUrl || '';
            const deliveryId = window.linenDeliveryPage?.deliveryId || 0;
            if (!reportPdfUrl || !deliveryId) {
                alert('Report preview is not available.');
                return;
            }

            $('#linenDeliveryReportModal').modal('show');
            previewLinenDeliveryReportPdf();
        });

        $('#btnPreviewLinenDeliveryReport').off('click').on('click', function () {
            previewLinenDeliveryReportPdf();
        });
    }

    function bindPantryNoteViewEvents() {
        $('.js-view-pantry-note').off('click').on('click', function () {
            openPantryNoteModal();
        });
    }

    function initReportModal() {
        $('#linenDeliveryReportModal').off('hidden.bs.modal').on('hidden.bs.modal', function () {
            clearLinenDeliveryReportPreview(document.getElementById('linenDeliveryReportFrame'));
        });
    }

    function initPantryNoteModal() {
        $('#linenDeliveryPantryNoteModal').off('hidden.bs.modal').on('hidden.bs.modal', function () {
            clearPantryNoteViewer();
        });
    }

    function openPantryNoteModal() {
        const noteId = ($('#Header_NoteID').val() || $('.js-view-pantry-note').data('note-id') || '').toString();
        if (!noteId) {
            alert('Pantry Linen is required.');
            focusErrorField('#Header_NoteID');
            return;
        }

        $('#linenDeliveryPantryNoteModalLabel').text(`Pantry Linen Detail - ${noteId}`);
        $('#linenDeliveryPantryNoteModal').modal('show');
        showPantryNoteLoading();

        const baseUrl = window.linenDeliveryPage?.pantryNotePrintPreviewUrl || '/Inventory/LinnenNoteDaily/LinnenNoteDailyDetail?handler=PrintPreview';
        const url = new URL(baseUrl, window.location.origin);
        url.searchParams.set('id', noteId);

        $.ajax({
            url: `${url.pathname}${url.search}`,
            type: 'GET',
            success: function (response) {
                if (!response || response.success !== true) {
                    showPantryNoteError(response && response.message ? response.message : 'Cannot load pantry linen.');
                    return;
                }

                renderPantryNoteViewer(response);
            },
            error: function (xhr) {
                showPantryNoteError(readAjaxError(xhr, 'Cannot load pantry linen.'));
            }
        });
    }

    function clearPantryNoteViewer() {
        $('#linenDeliveryPantryNoteNo').val('');
        $('#linenDeliveryPantryNoteDate').val('');
        $('#linenDeliveryPantryNoteDescription').val('');
        $('#linenDeliveryPantryNoteRent').prop('checked', false);
        $('#linenDeliveryPantryNoteClose').prop('checked', false);
        $('#linenDeliveryPantryNoteContent').html('<div class="linen-delivery-pantry-note-loading">Loading pantry linen...</div>');
    }

    function showPantryNoteLoading() {
        $('#linenDeliveryPantryNoteNo').val('');
        $('#linenDeliveryPantryNoteDate').val('');
        $('#linenDeliveryPantryNoteDescription').val('');
        $('#linenDeliveryPantryNoteRent').prop('checked', false);
        $('#linenDeliveryPantryNoteClose').prop('checked', false);
        $('#linenDeliveryPantryNoteContent').html('<div class="linen-delivery-pantry-note-loading">Loading pantry linen...</div>');
    }

    function showPantryNoteError(message) {
        $('#linenDeliveryPantryNoteContent').html(`<div class="linen-delivery-pantry-note-error">${encodeHtml(message || 'Cannot load pantry linen.')}</div>`);
    }

    function renderPantryNoteViewer(response) {
        const noteId = getJsonValue(response, 'noteId') || '';
        const description = getJsonValue(response, 'description') || '';
        const dateCreate = getJsonValue(response, 'dateCreate') || '';
        const isRent = getJsonValue(response, 'isRent') === true;
        const isClose = getJsonValue(response, 'isClose') === true;
        const columns = getJsonValue(response, 'columns') || [];
        const rows = getJsonValue(response, 'rows') || [];

        $('#linenDeliveryPantryNoteNo').val(noteId);
        $('#linenDeliveryPantryNoteDate').val(dateCreate);
        $('#linenDeliveryPantryNoteDescription').val(description);
        $('#linenDeliveryPantryNoteRent').prop('checked', isRent);
        $('#linenDeliveryPantryNoteClose').prop('checked', isClose);
        $('#linenDeliveryPantryNoteContent').html(buildPantryNoteTable(columns, rows));
    }

    function buildPantryNoteTable(columns, rows) {
        if (!columns.length) {
            return '<div class="linen-delivery-pantry-note-error">No linen columns.</div>';
        }

        let html = '<table class="table table-bordered table-sm mb-0 linen-delivery-pantry-note-table"><thead><tr>';
        html += '<th colspan="2" rowspan="2" class="pl-pantry-title">Pantry<br>No</th>';
        columns.forEach(function (column) {
            html += `<th colspan="3" class="pl-linen-title">${encodeHtml(getJsonValue(column, 'title') || '')}</th>`;
        });
        html += '</tr><tr>';
        columns.forEach(function () {
            html += '<th class="pl-sub-title">Be</th><th class="pl-sub-title">De</th><th class="pl-sub-title">Re</th>';
        });
        html += '</tr></thead><tbody>';

        const groups = groupPantryRows(rows);
        groups.forEach(function (group) {
            const amRow = group.rows.find(function (row) { return parseInt(getJsonValue(row, 'timeSection') || '0', 10) === 1; }) || {};
            const pmRow = group.rows.find(function (row) { return parseInt(getJsonValue(row, 'timeSection') || '0', 10) === 2; }) || {};
            html += buildPantryNoteDataRow(group.name, 'A', amRow, columns, true);
            html += buildPantryNoteDataRow(group.name, 'P', pmRow, columns, false);
        });

        html += '</tbody></table>';
        return html;
    }

    function buildPantryNoteDataRow(pantryName, shiftName, row, columns, includePantryName) {
        let html = '<tr>';
        if (includePantryName) {
            html += `<td rowspan="2" class="pl-pantry-cell">${encodeHtml(pantryName)}</td>`;
        }

        html += `<td class="pl-time-cell ${shiftName === 'A' ? 'pl-time-am' : 'pl-time-pm'}">${shiftName}</td>`;
        columns.forEach(function (column) {
            html += `<td class="pl-qty-cell pl-qty-be">${formatPantryQty(getJsonValue(row, getJsonValue(column, 'beField')))}</td>`;
            html += `<td class="pl-qty-cell">${formatPantryQty(getJsonValue(row, getJsonValue(column, 'deField')))}</td>`;
            html += `<td class="pl-qty-cell">${formatPantryQty(getJsonValue(row, getJsonValue(column, 'reField')))}</td>`;
        });
        html += '</tr>';
        return html;
    }

    function groupPantryRows(rows) {
        const groups = [];
        rows.forEach(function (row) {
            const pentry = getJsonValue(row, 'pentry') || '';
            let group = groups.find(function (item) { return item.pentry === pentry; });
            if (!group) {
                group = {
                    pentry: pentry,
                    name: getJsonValue(row, 'pentryName') || pentry,
                    rows: []
                };
                groups.push(group);
            }

            group.rows.push(row);
        });
        return groups;
    }

    function getJsonValue(source, name) {
        if (!source || !name) {
            return undefined;
        }

        if (Object.prototype.hasOwnProperty.call(source, name)) {
            return source[name];
        }

        const camelName = name.charAt(0).toLowerCase() + name.slice(1);
        if (Object.prototype.hasOwnProperty.call(source, camelName)) {
            return source[camelName];
        }

        const pascalName = name.charAt(0).toUpperCase() + name.slice(1);
        if (Object.prototype.hasOwnProperty.call(source, pascalName)) {
            return source[pascalName];
        }

        return undefined;
    }

    function formatPantryQty(value) {
        const numberValue = Number(value || 0);
        if (!Number.isFinite(numberValue) || numberValue === 0) {
            return '';
        }

        return numberValue.toLocaleString('en-US', {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        });
    }

    function previewLinenDeliveryReportPdf() {
        const reportPdfUrl = buildLinenDeliveryReportPdfUrl();
        const frame = document.getElementById('linenDeliveryReportFrame');
        const $loading = $('#linenDeliveryReportLoading');
        const linenCode = ($('#linenDeliveryReportLinenCode').val() || '').toString();
        const description = ($('#linenDeliveryReportDescription option:selected').text() || '').trim();
        const linenLabel = ($('#linenDeliveryReportLinenCode option:selected').text() || 'All').trim();

        if (!reportPdfUrl || !frame) {
            alert('Report preview is not available.');
            return;
        }

        $('#linenDeliveryReportMeta').text(`Delivery | ${description}${linenCode ? ` | ${linenLabel}` : ''}`);
        $loading.show();
        clearLinenDeliveryReportPreview(frame);

        loadLinenDeliveryReportPdfPreview(frame, reportPdfUrl)
            .then(function (objectUrl) {
                activeLinenDeliveryReportObjectUrl = objectUrl;
                $loading.hide();
            })
            .catch(function (error) {
                $loading.hide();
                alert(error?.message || 'Cannot load report preview.');
            });
    }

    function buildLinenDeliveryReportPdfUrl() {
        const baseUrl = window.linenDeliveryPage?.reportPdfUrl || '';
        if (!baseUrl) {
            return '';
        }

        const url = new URL(baseUrl, window.location.origin);
        const linenCode = ($('#linenDeliveryReportLinenCode').val() || '').toString().trim();
        if (linenCode) {
            url.searchParams.set('linenCode', linenCode);
        } else {
            url.searchParams.delete('linenCode');
        }

        return `${url.pathname}${url.search}`;
    }

    async function loadLinenDeliveryReportPdfPreview(frame, url) {
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

    function clearLinenDeliveryReportPreview(frame) {
        if (frame) {
            frame.removeAttribute('src');
        }

        if (activeLinenDeliveryReportObjectUrl) {
            URL.revokeObjectURL(activeLinenDeliveryReportObjectUrl);
            activeLinenDeliveryReportObjectUrl = '';
        }
    }

    function refreshHeaderSections() {
        const typeValue = $('#Header_DeliveryType').val();
        const showPantry = typeValue === '1';
        const showSupplier = typeValue === '1' || typeValue === '2' || typeValue === '3';
        const showSpecial = typeValue === '3';

        const layoutClass = showPantry ? 'ld-layout-pantry' : (showSpecial ? 'ld-layout-special' : (showSupplier ? 'ld-layout-supplier' : 'ld-layout-simple'));
        $('.linen-delivery-header-vb')
            .removeClass('ld-layout-pantry ld-layout-supplier ld-layout-special ld-layout-simple')
            .addClass(layoutClass);

        $('.linen-delivery-note-wrap').toggle(showPantry);
        $('.linen-delivery-supplier-wrap').toggle(showSupplier);
        $('.linen-delivery-special-wrap').toggle(showSpecial);

        if (!showSpecial) {
            $('#Header_IsSpecialLaundry').prop('checked', false);
        }

        syncToBillButtonState(typeValue);
        updatePantryNoteViewButton();
    }

    function syncToBillButtonState(typeValue) {
        const canToBill = typeValue === '5' || typeValue === '3';
        $('#btnToBill').prop('disabled', !canToBill);
    }

    function addDetailRow() {
        $('.linen-delivery-empty-row').remove();

        const locations = (window.linenDeliveryPage?.rowTemplateOptions?.locations || []).map(function (item) {
            return `<option value="${encodeHtml(item.value)}">${encodeHtml(item.text)}</option>`;
        }).join('');

        const linens = (window.linenDeliveryPage?.rowTemplateOptions?.linens || []).map(function (item) {
            return `<option value="${encodeHtml(item.value)}" data-price="${encodeHtml(item.price)}">${encodeHtml(item.text)}</option>`;
        }).join('');

        const html = `
        <tr class="linen-delivery-detail-row" data-id="0" data-delivery-id="${encodeHtml($('#Header_DeliveryID').val() || $('#Header_DeliveryID').attr('value') || '')}" data-no-need-bill="false">
            <td class="linen-delivery-action-cell">
                <div class="linen-delivery-action-wrap">
                    <button type="button" class="btn btn-xs btn-outline-danger border js-remove-row" title="Remove">
                        <i class="fas fa-trash"></i>
                    </button>
                </div>
            </td>
            <td><select class="form-control form-control-sm ld-location vni-font">${locations}</select></td>
            <td><select class="form-control form-control-sm ld-linen vni-font">${linens}</select></td>
            <td class="text-center"><input type="checkbox" class="ld-express" /></td>
            <td class="text-center"><input type="checkbox" class="ld-child" /></td>
            <td><input type="text" inputmode="decimal" class="form-control form-control-sm text-right ld-quantity" value="0" /></td>
            <td><input type="text" inputmode="decimal" class="form-control form-control-sm text-right ld-price" value="0" /></td>
            <td><input type="text" inputmode="decimal" class="form-control form-control-sm text-right ld-amount" value="0" readonly /></td>
            <td class="linen-delivery-note-cell"><input type="text" maxlength="100" class="form-control form-control-sm ld-note vni-font" value="" /></td>
        </tr>`;

        $('#linenDeliveryDetailTable tbody').append(html);
    }

    function openBillModal() {
        clearBillError();
        $('#linenDeliveryBillModal').modal('show');
        loadBills();
    }

    function loadBills() {
        clearBillError();

        $.ajax({
            url: `${window.location.pathname}?handler=Bills`,
            type: 'GET',
            data: {
                id: window.linenDeliveryPage?.deliveryId || 0
            },
            success: function (response) {
                if (!response || response.success !== true) {
                    showBillError(response && response.message ? response.message : 'Cannot load bills.');
                    return;
                }

                renderBillState(response.toBill === true, response.bills || []);
            },
            error: function (xhr) {
                showBillError(readAjaxError(xhr, 'Cannot load bills.'));
            }
        });
    }

    function createBill() {
        clearBillError();

        if (!validateMainForm()) {
            return;
        }

        const billDate = $('#linenDeliveryBillDate').val();
        if (!billDate) {
            showBillError('Bill date is required.');
            $('#linenDeliveryBillDate').trigger('focus');
            return;
        }

        $.ajax({
            url: `${window.location.pathname}?handler=CreateBill`,
            type: 'POST',
            data: {
                deliveryId: window.linenDeliveryPage?.deliveryId || 0,
                billDate: billDate,
                detailsJson: $('#DetailsJson').val() || JSON.stringify(collectDetails())
            },
            headers: {
                RequestVerificationToken: $('input[name="__RequestVerificationToken"]').first().val() || ''
            },
            success: function (response) {
                if (!response || response.success !== true) {
                    showBillError(response && response.message ? response.message : 'Cannot create bill.');
                    return;
                }

                pageDirty = false;
                renderBillState(true, response.bills || []);
            },
            error: function (xhr) {
                showBillError(readAjaxError(xhr, 'Cannot create bill.'));
            }
        });
    }

    function renderBillState(hasCreatedBill, bills) {
        const hasBills = !!(bills && bills.length > 0);
        $('#linenDeliveryBillCreatePanel').toggleClass('d-none', hasCreatedBill || hasBills);
        $('#linenDeliveryBillListWrap').toggleClass('d-none', !hasCreatedBill && !hasBills);

        if (hasCreatedBill || hasBills) {
            renderBillRows(bills || []);
        } else {
            $('#linenDeliveryBillTableBody').html('<tr><td colspan="9" class="text-center text-muted py-3">No bills</td></tr>');
        }
    }

    function renderBillRows(bills) {
        if (!bills || bills.length === 0) {
            $('#linenDeliveryBillTableBody').html('<tr><td colspan="9" class="text-center text-muted py-3">No bills</td></tr>');
            $('#linenDeliveryBillListWrap').removeClass('d-none');
            return;
        }

        const html = bills.map(function (item) {
            const mode = Number(item.billStatus) === 1 ? 'edit' : 'view';
            return `<tr class="js-bill-row" data-bill-id="${encodeHtml(item.billId)}" data-mode="${mode}">
                <td>${encodeHtml(item.billId)}</td>
                <td>${encodeHtml(item.billDate || '')}</td>
                <td>${encodeHtml(item.apartmentNo || '')}</td>
                <td>${encodeHtml(item.customer || '')}</td>
                <td class="text-right">${encodeHtml(formatDisplayNumber(item.vndAmountBefVat))}</td>
                <td class="text-right">${encodeHtml(formatDisplayNumber(item.pctTax))}</td>
                <td class="text-right">${encodeHtml(formatDisplayNumber(item.vndAmountVat))}</td>
                <td class="text-right">${encodeHtml(formatDisplayNumber(item.vndAmount))}</td>
                <td class="bill-status-cell">${encodeHtml(item.billStatusText || '')}</td>
            </tr>`;
        }).join('');

        $('#linenDeliveryBillTableBody').html(html);
        $('#linenDeliveryBillListWrap').removeClass('d-none');
    }

    function openBillDetail($row) {
        const billId = $row.data('bill-id');
        const mode = $row.data('mode') || 'view';
        const detailUrl = window.linenDeliveryPage?.billDetailUrl || '';
        if (!billId || !detailUrl) {
            return;
        }

        const returnUrl = `${window.location.pathname}${window.location.search || ''}`;
        const separator = detailUrl.indexOf('?') >= 0 ? '&' : '?';
        window.location.href = `${detailUrl}${separator}id=${encodeURIComponent(billId)}&mode=${encodeURIComponent(mode)}&returnUrl=${encodeURIComponent(returnUrl)}`;
    }

    function formatDisplayNumber(value) {
        const text = (value || '0').toString().replace(/,/g, '').trim();
        const number = Number(text);
        if (!Number.isFinite(number)) {
            return value || '0';
        }

        return number.toLocaleString('en-US', {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        });
    }

    function loadPrintPreview() {
        const previewUrl = window.linenDeliveryPage?.printPreviewUrl || '';
        if (!previewUrl) {
            alert('Report preview is not available.');
            return;
        }

        const linenCode = ($('#linenDeliveryReportLinenCode').val() || '').toString().trim();
        const requestUrl = new URL(previewUrl, window.location.origin);
        if (linenCode) {
            requestUrl.searchParams.set('linenCode', linenCode);
        }

        $('#linenDeliveryReportPreviewContainer').html('<div class="text-center text-muted py-5">Loading report data...</div>');

        $.ajax({
            url: requestUrl.toString(),
            type: 'GET',
            success: function (response) {
                if (!response || response.success !== true) {
                    const message = response && response.message ? response.message : 'Cannot load report preview.';
                    $('#linenDeliveryReportPreviewContainer').html(`<div class="alert alert-danger mb-0">${encodeHtml(message)}</div>`);
                    return;
                }

                renderPrintPreview(response);
            },
            error: function (xhr) {
                $('#linenDeliveryReportPreviewContainer').html(`<div class="alert alert-danger mb-0">${encodeHtml(readAjaxError(xhr, 'Cannot load report preview.'))}</div>`);
            }
        });
    }

    function renderPrintPreview(response) {
        const rows = Array.isArray(response.rows) ? response.rows : [];
        const metaParts = [];
        const description = response.description || '';
        const deliveryDate = response.deliveryDate || '';
        const supplierName = response.supplierName || '';
        const deliveryTypeName = response.deliveryTypeName || '';

        if (description) {
            metaParts.push(`Delivery: ${description}`);
        }

        if (deliveryDate) {
            metaParts.push(`Date: ${deliveryDate}`);
        }

        if (supplierName) {
            metaParts.push(`Supplier: ${supplierName}`);
        }

        if (deliveryTypeName) {
            metaParts.push(`Type: ${deliveryTypeName}`);
        }

        $('#linenDeliveryReportPreviewMeta').text(metaParts.join(' | '));

        if (rows.length === 0) {
            $('#linenDeliveryReportPreviewContainer').html('<div class="text-center text-muted py-5">No preview data.</div>');
            return;
        }

        let html = '<table class="table table-bordered table-sm mb-0 linen-delivery-report-preview-table"><thead><tr>';
        html += '<th>Location</th><th>Linnen</th><th>Child</th><th>Express</th><th>Qty</th><th>Price</th><th>Amount</th><th>Note</th>';
        html += '</tr></thead><tbody>';

        rows.forEach(function (row) {
            html += '<tr>';
            html += `<td class="vni-font">${encodeHtml(row.location || '')}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.linenCode || '')}</td>`;
            html += `<td class="text-center">${row.isChild === true ? 'Y' : ''}</td>`;
            html += `<td class="text-center">${row.express === true ? 'Y' : ''}</td>`;
            html += `<td class="text-right">${encodeHtml(row.quantity || '')}</td>`;
            html += `<td class="text-right">${encodeHtml(row.price || '')}</td>`;
            html += `<td class="text-right">${encodeHtml(row.amount || '')}</td>`;
            html += `<td class="vni-font">${encodeHtml(row.note || '')}</td>`;
            html += '</tr>';
        });

        html += '</tbody></table>';
        $('#linenDeliveryReportPreviewContainer').html(html);
    }

    function refreshLookupOptions() {
        const deliveryType = $('#Header_DeliveryType').val() || '';
        const supplierId = $('#Header_SupplierID').val() || '';
        const isSpecialLaundry = $('#Header_IsSpecialLaundry').is(':checked');

        $.ajax({
            url: `${window.location.pathname}?handler=LookupOptions`,
            type: 'GET',
            data: {
                deliveryType: deliveryType,
                supplierId: supplierId,
                isSpecialLaundry: isSpecialLaundry
            },
            success: function (response) {
                if (!response || response.success !== true) {
                    return;
                }

                window.linenDeliveryPage = window.linenDeliveryPage || {};
                window.linenDeliveryPage.isSpecialLaundry = isSpecialLaundry;
                window.linenDeliveryPage.rowTemplateOptions = {
                    locations: response.locations || [],
                    linens: response.linens || []
                };

                syncToBillButtonState(deliveryType);
                refreshGridSelectOptions();
            }
        });
    }

    function refreshGridSelectOptions() {
        const locationOptions = window.linenDeliveryPage?.rowTemplateOptions?.locations || [];
        const linenOptions = window.linenDeliveryPage?.rowTemplateOptions?.linens || [];

        $('#linenDeliveryDetailTable tbody tr.linen-delivery-detail-row').each(function () {
            const $row = $(this);
            const selectedLocation = $row.find('.ld-location').val() || '';
            const selectedLinen = $row.find('.ld-linen').val() || '';

            $row.find('.ld-location').html(buildOptions(locationOptions, selectedLocation, false));
            $row.find('.ld-linen').html(buildOptions(linenOptions, selectedLinen, true));

            if ($row.find('.ld-linen').val()) {
                $row.find('.ld-price').val(formatDecimal(parseDecimal($row.find('.ld-linen option:selected').data('price'))));
            }

            recalcRow($row);
        });
    }

    function buildOptions(items, selectedValue, includePrice) {
        return (items || []).map(function (item) {
            const isSelected = String(item.value || '') === String(selectedValue || '');
            const pricePart = includePrice ? ` data-price="${encodeHtml(item.price || '')}"` : '';
            const selectedPart = isSelected ? ' selected' : '';
            return `<option value="${encodeHtml(item.value || '')}"${pricePart}${selectedPart}>${encodeHtml(item.text || '')}</option>`;
        }).join('');
    }

    function collectDetails() {
        const rows = [];

        $('#linenDeliveryDetailTable tbody tr.linen-delivery-detail-row').each(function () {
            const $row = $(this);
            rows.push({
                id: parseInt($row.data('id') || 0, 10),
                deliveryID: parseInt($('#Header_DeliveryID').val() || $('#Header_DeliveryID').attr('value') || 0, 10),
                locationID: parseNullableInt($row.find('.ld-location').val()),
                linnenID: parseNullableInt($row.find('.ld-linen').val()),
                express: $row.find('.ld-express').is(':checked'),
                isChild: $row.find('.ld-child').is(':checked'),
                quantity: parseDecimal($row.find('.ld-quantity').val()),
                price: parseDecimal($row.find('.ld-price').val()),
                amount: parseDecimal($row.find('.ld-amount').val()),
                note: $row.find('.ld-note').val() || '',
                noNeedBill: $row.attr('data-no-need-bill') === 'true'
            });
        });

        return rows;
    }

    function syncPantryNoteState() {
        const noteId = $('#Header_NoteID').val() || '';
        updatePantryNoteViewButton();
        if (!noteId) {
            applyPantryRent(false);
            return;
        }

        $.ajax({
            url: `${window.location.pathname}?handler=PantryNoteInfo`,
            type: 'GET',
            data: {
                id: noteId
            },
            success: function (response) {
                if (!response || response.success !== true) {
                    return;
                }

                applyPantryRent(response.isRent === true);
            }
        });
    }

    function updatePantryNoteViewButton() {
        const typeValue = $('#Header_DeliveryType').val() || '';
        const noteId = ($('#Header_NoteID').val() || '').toString();
        const canView = typeValue === '1' && noteId !== '';
        $('.js-view-pantry-note')
            .prop('disabled', !canView)
            .attr('data-note-id', noteId);
    }

    function applyPantryRent(isRent) {
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

    function markNoNeedBill($row) {
        if (!$row || $row.length === 0) {
            return;
        }

        const detailId = parseInt($row.attr('data-id') || '0', 10);
        const deliveryId = parseInt(window.linenDeliveryPage?.deliveryId || '0', 10);
        const typeValue = $('#Header_DeliveryType').val() || '';
        const isSpecialLaundry = $('#Header_IsSpecialLaundry').is(':checked');

        if (detailId <= 0 || deliveryId <= 0) {
            return;
        }

        if ($row.attr('data-no-need-bill') === 'true') {
            return;
        }

        if (!(typeValue === '5' || (typeValue === '3' && !isSpecialLaundry))) {
            return;
        }

        if (!window.confirm("You don't want to issue bill for this linnen!")) {
            return;
        }

        $.ajax({
            url: `${window.location.pathname}?handler=MarkNoNeedBill`,
            type: 'POST',
            data: {
                deliveryId: deliveryId,
                detailId: detailId
            },
            headers: {
                RequestVerificationToken: $('input[name="__RequestVerificationToken"]').first().val() || ''
            },
            success: function (response) {
                if (!response || response.success !== true) {
                    alert(response && response.message ? response.message : 'Cannot update No Need Bill.');
                    return;
                }

                $row.attr('data-no-need-bill', 'true');
            },
            error: function (xhr) {
                alert(readAjaxError(xhr, 'Cannot update No Need Bill.'));
            }
        });
    }

    function recalcAllRows() {
        $('#linenDeliveryDetailTable tbody tr.linen-delivery-detail-row').each(function () {
            recalcRow($(this));
        });
    }

    function recalcRow($row) {
        const quantity = parseDecimal($row.find('.ld-quantity').val());
        const price = parseDecimal($row.find('.ld-price').val());
        const express = $row.find('.ld-express').is(':checked');
        const amount = quantity * price * (express ? 2 : 1);
        $row.find('.ld-amount').val(formatDecimal(amount));
    }

    function ensureEmptyRow() {
        const rowCount = $('#linenDeliveryDetailTable tbody tr.linen-delivery-detail-row').length;
        if (rowCount === 0) {
            $('#linenDeliveryDetailTable tbody').html('<tr class="linen-delivery-empty-row"><td colspan="9" class="text-center text-muted py-3">No detail rows</td></tr>');
        }
    }

    function showBillError(message) {
        $('#linenDeliveryBillError').removeClass('d-none').text(message || 'Cannot process bill.');
    }

    function clearBillError() {
        $('#linenDeliveryBillError').addClass('d-none').text('');
    }

    function readAjaxError(xhr, fallbackMessage) {
        if (xhr && xhr.responseJSON && xhr.responseJSON.message) {
            return xhr.responseJSON.message;
        }

        return fallbackMessage;
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
            return '0';
        }

        return numberValue.toLocaleString('en-US', {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        });
    }

    function encodeHtml(value) {
        return $('<div>').text(value).html();
    }

    $(document).ready(initializePage);
})();
