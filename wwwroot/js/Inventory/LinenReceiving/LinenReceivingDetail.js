(function () {
    'use strict';

    let pageDirty = false;
    let activeReportObjectUrl = '';
    let rowDeliveryOptionsCache = null;
    let rowDeliveryOptionsRequest = null;

    function initializePage() {
        initAutoDismissAlerts();
        bindFormSubmit();
        bindGridEvents();
        bindHeaderEvents();
        bindDeliveryViewEvents();
        bindPrintEvents();
        bindDirtyTracking();
        initReportModal();
        initializeLocationSelect2(document);
        recalcAllRows();
        pageDirty = false;
        preloadRowDeliveryOptions();
    }

    function initAutoDismissAlerts() {
        $('.js-auto-dismiss-alert').each(function () {
            const $alert = $(this);
            const timeout = parseInt($alert.data('timeout'), 10) || 10000;
            setTimeout(function () {
                $alert.alert('close');
            }, timeout);
        });
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
            const $button = $(this);
            $button.prop('disabled', true);
            ensureRowDeliveryOptions(getHeaderDeliveryOption(), true)
                .done(function (deliveryOptions) {
                    addDetailRow(deliveryOptions);
                    pageDirty = true;
                })
                .fail(function (error) {
                    alert(error?.message || 'Cannot load delivery list.');
                })
                .always(function () {
                    $button.prop('disabled', false);
                });
        });

        $(document).off('click', '.js-remove-row').on('click', '.js-remove-row', function () {
            const $row = $(this).closest('tr');
            destroyLocationSelect2($row);
            $row.remove();
            ensureEmptyRow();
            pageDirty = true;
        });

        $(document).off('change', '.lr-linen').on('change', '.lr-linen', function () {
            const $row = $(this).closest('tr');
            refreshRowPriceFromLinen($row);
            recalcRow($row);
            pageDirty = true;
        });

        $(document).off('focus click', '.lr-delivery').on('focus click', '.lr-delivery', function () {
            hydrateDeliverySelect($(this));
        });

        $(document).off('change', '.lr-delivery').on('change', '.lr-delivery', function () {
            pageDirty = true;
        });

        $(document).off('input change', '.lr-quantity').on('input change', '.lr-quantity', function (e) {
            if (e.type === 'change') {
                $(this).val(formatDecimal(parseDecimal($(this).val())));
            }
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
        $('#Header_SendID').off('change').on('change', function () {
            updateDeliveryViewButton();
            syncDeliveryRent();
        });
        updateDeliveryViewButton();
    }

    function updateDeliveryViewButton() {
        const sendId = ($('#Header_SendID').val() || '').toString();
        const $button = $('#btnViewLinenReceivingDelivery');

        if (!$button.length) {
            return;
        }

        if (!sendId) {
            $button.prop('disabled', true).data('delivery-url', '').attr('data-delivery-url', '');
            return;
        }

        const template = ($button.data('delivery-url-template') || '').toString();
        const detailUrl = template.replace('__DELIVERY_ID__', encodeURIComponent(sendId));
        $button.prop('disabled', false).data('delivery-url', detailUrl).attr('data-delivery-url', detailUrl);
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

    function bindDeliveryViewEvents() {
        $(document).off('click', '.js-view-delivery-detail').on('click', '.js-view-delivery-detail', function () {
            const detailUrl = $(this).data('delivery-url') || '';
            const frame = document.getElementById('linenReceivingDeliveryDetailFrame');
            if (!detailUrl || !frame) {
                return;
            }

            frame.src = detailUrl;
            $('#linenReceivingDeliveryDetailModal').modal('show');
        });

        $('#linenReceivingDeliveryDetailModal').off('hidden.bs.modal').on('hidden.bs.modal', function () {
            const frame = document.getElementById('linenReceivingDeliveryDetailFrame');
            if (frame) {
                frame.removeAttribute('src');
            }
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

    function loadPdfPreview(frame, url) {
        return new Promise(function (resolve, reject) {
            frame.onload = function () {
                frame.onload = null;
                frame.onerror = null;
                resolve(url);
            };
            frame.onerror = function () {
                frame.onload = null;
                frame.onerror = null;
                reject(new Error('Cannot load report preview.'));
            };
            frame.src = url;
        });
    }

    function clearReportPreview(frame) {
        if (frame) {
            frame.removeAttribute('src');
        }

        if (activeReportObjectUrl && activeReportObjectUrl.indexOf('blob:') === 0) {
            URL.revokeObjectURL(activeReportObjectUrl);
        }

        activeReportObjectUrl = '';
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

    function getHeaderDeliveryOption() {
        const $header = $('#Header_SendID');
        return {
            value: ($header.val() || window.linenReceivingPage?.sendId || '').toString(),
            text: ($header.find('option:selected').text() || '').toString()
        };
    }

    function getCurrentDeliveryOption($select) {
        return {
            value: ($select.val() || '').toString(),
            text: ($select.find('option:selected').text() || '').toString()
        };
    }

    function hydrateDeliverySelect($select) {
        if (!$select.length || $select.prop('disabled') || $select.data('delivery-loaded') === true) {
            return;
        }

        const currentOption = getCurrentDeliveryOption($select);
        ensureRowDeliveryOptions(currentOption, false)
            .done(function (deliveryOptions) {
                renderDeliveryOptions($select, deliveryOptions, currentOption.value);
                $select.data('delivery-loaded', true).attr('data-delivery-loaded', 'true');
            })
            .fail(function (error) {
                alert(error?.message || 'Cannot load delivery list.');
            });
    }

    function preloadRowDeliveryOptions() {
        return ensureRowDeliveryOptions(getHeaderDeliveryOption(), true);
    }

    function ensureRowDeliveryOptions(currentOption, allowFetch) {
        const current = currentOption || { value: '', text: '' };
        const deferred = $.Deferred();

        if (rowDeliveryOptionsCache) {
            deferred.resolve(mergeDeliveryOptions(rowDeliveryOptionsCache, current));
            return deferred.promise();
        }

        if (rowDeliveryOptionsRequest) {
            rowDeliveryOptionsRequest
                .done(function () {
                    deferred.resolve(mergeDeliveryOptions(rowDeliveryOptionsCache || [], current));
                })
                .fail(function () {
                    deferred.reject(new Error('Cannot load delivery list.'));
                });
            return deferred.promise();
        }

        if (allowFetch !== true) {
            deferred.reject(new Error('Delivery list is still loading.'));
            return deferred.promise();
        }

        rowDeliveryOptionsRequest = $.ajax({
            url: `${window.location.pathname}?handler=RowDeliveryOptions`,
            type: 'GET',
            data: { currentDeliveryId: current.value || '' }
        });

        rowDeliveryOptionsRequest
            .done(function (response) {
                if (!response || response.success !== true) {
                    deferred.reject(new Error(response?.message || 'Cannot load delivery list.'));
                    return;
                }

                rowDeliveryOptionsCache = response.options || [];
                deferred.resolve(mergeDeliveryOptions(rowDeliveryOptionsCache, current));
            })
            .fail(function () {
                deferred.reject(new Error('Cannot load delivery list.'));
            })
            .always(function () {
                rowDeliveryOptionsRequest = null;
            });

        return deferred.promise();
    }

    function mergeDeliveryOptions(options, currentOption) {
        const merged = [];
        const seen = {};
        const currentValue = (currentOption?.value || '').toString();

        (options || []).forEach(function (item) {
            const value = (item.value || '').toString();
            if (seen[value]) {
                return;
            }

            seen[value] = true;
            merged.push({ value: value, text: item.text || '' });
        });

        if (currentValue && !seen[currentValue]) {
            merged.unshift({ value: currentValue, text: currentOption?.text || currentValue });
        }

        return merged;
    }

    function renderDeliveryOptions($select, options, selectedValue) {
        const selected = (selectedValue || '').toString();
        const html = (options || []).map(function (item) {
            const value = (item.value || '').toString();
            const selectedAttr = value === selected ? ' selected' : '';
            return `<option value="${encodeHtml(value)}"${selectedAttr}>${encodeHtml(item.text || '')}</option>`;
        }).join('');

        $select.html(html);
    }

    function addDetailRow(deliveryOptions) {
        $('.linen-receiving-empty-row').remove();

        const headerSendId = ($('#Header_SendID').val() || window.linenReceivingPage?.sendId || '').toString();
        const deliveries = (deliveryOptions || []).map(function (item) {
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
            <td><select class="form-control form-control-sm lr-delivery vni-font" data-delivery-loaded="true">${deliveries}</select></td>
            <td><select class="form-control form-control-sm lr-location select2 vni-font" data-placeholder="-- Select --">${locations}</select></td>
            <td><select class="form-control form-control-sm lr-linen vni-font">${linens}</select></td>
            <td class="text-center"><input type="checkbox" class="lr-express" /></td>
            <td class="text-center"><input type="checkbox" class="lr-child" /></td>
            <td><input type="text" inputmode="decimal" class="form-control form-control-sm text-right lr-quantity" value="0" /></td>
            <td><input type="text" inputmode="decimal" class="form-control form-control-sm text-right lr-price" value="0" /></td>
            <td><input type="text" inputmode="decimal" class="form-control form-control-sm text-right lr-amount" value="0" readonly /></td>
            <td class="linen-receiving-note-cell"><input type="text" maxlength="100" class="form-control form-control-sm lr-note vni-font" value="" /></td>
        </tr>`;

        const $row = $(html);
        $('#linenReceivingDetailTable tbody').append($row);
        initializeLocationSelect2($row);
    }

    function initializeLocationSelect2(scope) {
        if (!$.fn.select2) {
            return;
        }

        $(scope || document).find('select.lr-location.select2').each(function () {
            const $element = $(this);
            if ($element.hasClass('select2-hidden-accessible')) {
                $element.select2('destroy');
            }

            $element.select2({
                width: '100%',
                placeholder: $element.data('placeholder') || '-- Select --',
                allowClear: true,
                minimumResultsForSearch: 0
            });

            $element.off('select2:open.linenReceiving').on('select2:open.linenReceiving', function () {
                const searchField = document.querySelector('.select2-container--open .select2-search__field');
                if (searchField) {
                    searchField.focus();
                }
            });
        });
    }

    function destroyLocationSelect2(scope) {
        if (!$.fn.select2) {
            return;
        }

        $(scope || document).find('select.lr-location.select2.select2-hidden-accessible').each(function () {
            $(this).select2('destroy');
        });
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
            return '0';
        }

        return numberValue.toLocaleString('en-US', {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        });
    }

    function encodeHtml(value) {
        return $('<div>').text(value ?? '').html();
    }

    $(document).ready(initializePage);
})();
