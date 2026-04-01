$(document).ready(function () {
    // Read mode and user rights
    const urlParams = new URLSearchParams(window.location.search);
    const modeParam = urlParams.get('mode');
    const mode = modeParam ? modeParam.toLowerCase() : 'add';

    const $form = $('#materialRequestDetailForm');
    const currentStatusId = toIntData($form.data('current-status'));
    const actionPerm = {
        canSave: toBoolData($form.data('can-save')),
        canSubmit: toBoolData($form.data('can-submit')),
        canCalculate: toBoolData($form.data('can-calculate')),
        canApprove: toBoolData($form.data('can-approve')),
        canReject: toBoolData($form.data('can-reject'))
    };

    // Start page
    initializePage(mode, currentStatusId, actionPerm);

    // Handle main form submit
    $('#materialRequestDetailForm').on('submit', function (e) {
        if (mode === 'view') return true;

        const submitter = e.originalEvent && e.originalEvent.submitter ? e.originalEvent.submitter : null;
        const form = this;
        let actionMode = ($('#workflowActionModeInput').val() || '').toString().trim();

        e.preventDefault(); // Stop browser submit

        const $tableBody = $('#mrLineTableBody');
        const $linesJsonInput = $('#linesJsonInput');
        syncPostedLines($tableBody, $linesJsonInput);

        $('#rejectItemLineIdsJsonInput').val('');

        const isRejectButton = submitter && submitter.id === 'mrRejectBtn';
        const isIssueButton = submitter && submitter.id === 'mrIssueBtn';
        const $selectedRows = $tableBody.find('.mr-line-row.is-selected');
        const lineCount = getMrLineCount($tableBody);

        if (isRejectButton && $selectedRows.length > 0 && lineCount > 1) {
            const rejectPayload = promptRejectItemPayload($selectedRows);
            if (!rejectPayload) {
                return;
            }

            actionMode = 'reject-item';
            $('#rejectItemLineIdsJsonInput').val(JSON.stringify(rejectPayload.lineIds));
        }
        else if (submitter && submitter.id === 'mrRejectBtn') {
            if (!window.confirm('Are you sure to reject this Material Request?')) {
                return;
            }
        }
        else if (isIssueButton) {
            if (!window.confirm('Are you sure to mark this Material Request as ISSUED?')) {
                return;
            }
        }

        $('#workflowActionModeInput').val(actionMode);

        if (validateMainForm(actionMode)) {
            if (submitter && submitter.formAction) {
                form.action = submitter.formAction;
            }

            form.submit(); // Use the clicked button submit
        }
    });

    $('#mrRejectBtn').off('click.mrReject').on('click.mrReject', function (event) {
        event.preventDefault();

        const $tableBody = $('#mrLineTableBody');
        const $linesJsonInput = $('#linesJsonInput');
        const $selectedRows = $tableBody.find('.mr-line-row.is-selected');
        const lineCount = getMrLineCount($tableBody);

        syncPostedLines($tableBody, $linesJsonInput);
        $('#workflowActionModeInput').val('');
        $('#rejectItemLineIdsJsonInput').val('');

        let actionMode = '';
        if ($selectedRows.length > 0 && lineCount > 1) {
            const rejectPayload = promptRejectItemPayload($selectedRows);
            if (!rejectPayload) {
                return;
            }

            actionMode = 'reject-item';
            $('#rejectItemLineIdsJsonInput').val(JSON.stringify(rejectPayload.lineIds));
        }
        else if (!window.confirm('Are you sure to reject this Material Request?')) {
            return;
        }

        $('#workflowActionModeInput').val(actionMode);

        if (!validateMainForm(actionMode)) {
            return;
        }

        const form = this.form;
        if (form && this.formAction) {
            form.action = this.formAction;
        }

        if (form) {
            form.submit();
        }
    });
});

// Validation helpers
function initializePage(mode, currentStatusId, actionPerm) {
    const $form = $('#materialRequestDetailForm');
    const $tableBody = $('#mrLineTableBody');

    const canSave = !!actionPerm.canSave;
    const canSubmit = !!actionPerm.canSubmit;
    const canCalculate = !!actionPerm.canCalculate;
    const canApprove = !!actionPerm.canApprove;
    const canReject = !!actionPerm.canReject;

    const isViewMode = mode === 'view';
    const disableEditFields = isViewMode || !canSave;

    // Stop browser submit
    $form.find('input, textarea, select')
        .not('[type="hidden"], .mr-line-select, #Input_IsAuto, #mrSubmitBtn, #mrApproveBtn, #mrRejectBtn, #addMrLineBtn, #removeMrLineBtn, #createNewItemBtn, #calculateBtn')
        .prop('disabled', disableEditFields);

    $('#Input_IsAuto').prop('disabled', true);

    if (toBoolData($form.data('store-group-locked'))) {
        $('#Input_StoreGroup').prop('disabled', true);
    }

    const isDraft = currentStatusId === -1;
    const isSubmittedToHead = currentStatusId === 0;
    const isHeadApproved = currentStatusId === 1;
    const isPurchaserChecked = currentStatusId === 2;
    const isCfoApproved = currentStatusId === 3;
    const isCollectedToPr = currentStatusId === 4;

    const showEditActions = isDraft && !isViewMode && canSave;
    const showSubmitAction = isDraft && !isViewMode && canSubmit;
    const showCalculateAction = isHeadApproved && canCalculate;
    const showWorkflowActions = !isDraft && (canApprove || canReject);

    // Use the clicked button submit
    $('#addMrLineBtn, #removeMrLineBtn, #createNewItemBtn')
        .toggle(showEditActions)
        .prop('disabled', !showEditActions);
    $('#calculateBtn')
        .toggle(showCalculateAction)
        .prop('disabled', !showCalculateAction);
    $('#mrSubmitBtn')
        .toggle(showSubmitAction)
        .prop('disabled', !showSubmitAction);
    $('#mrApproveBtn')
        .toggle(showWorkflowActions)
        .prop('disabled', !showWorkflowActions || (!canApprove && !canReject));
    $('#mrRejectBtn')
        .toggle(showWorkflowActions)
        .prop('disabled', !showWorkflowActions || (!canApprove && !canReject));
    $('#mrIssueBtn')
        .toggle(isCollectedToPr)
        .prop('disabled', !isCollectedToPr);

    // Display only.
    $('#NoIssueCheck').prop('disabled', true);

    // Lock inputs by mode and rights
    $tableBody.off('click.mrLine').on('click.mrLine', '.mr-line-row', function (event) {
        if ($(event.target).is('input, label, button, textarea, select, a')) return;
        setSelectedMrLineRow($tableBody, $(this));
    });

    $tableBody.off('change.mrLineSelect').on('change.mrLineSelect', '.mr-line-select', function () {
        setSelectedMrLineRow($tableBody, $(this).closest('.mr-line-row'));
    });

    $tableBody.off('click.mrLineSelect').on('click.mrLineSelect', '.mr-line-select', function (event) {
        const $row = $(this).closest('.mr-line-row');
        if ($row.length === 0) return;

        if ($row.hasClass('is-selected')) {
            event.preventDefault();
            event.stopPropagation();
            clearSelectedMrLineRow($tableBody);
        }
    });

    // Lock action buttons by workflow status
    $('#addMrLineBtn').off('click').on('click', function () {
        $('#mrItemLookupModal').modal('show');
        renderLookupResults($('#lookupResultBody'), []);
        $('#lookupKeyword').trigger('focus');
    });

    // Sync the Not issue checkbox with the hidden field
    $('#removeMrLineBtn').off('click').on('click', function () {
        const $selectedRows = $tableBody.find('.mr-line-row.is-selected');
        if ($selectedRows.length === 0) {
            alert('Please select item row(s) to remove.');
            return;
        }

        if (!window.confirm('Are you sure to remove selected item row(s)?')) {
            return;
        }

        $selectedRows.remove();
        syncEmptyRow($tableBody);
        syncLineInputNames($tableBody);
        refreshLineIndexes($tableBody);
        autoSaveDraftAfterGridChange('remove-item');
    });

    // Search trong popup lookup
    $('#lookupSearchBtn').off('click').on('click', function () {
        runItemLookupSearch();
    });

    // Select a row in the grid
    $('#lookupResultBody').off('click.mrAddItem').on('click.mrAddItem', '.lookup-add-item-btn', function () {
        const $tr = $(this).closest('tr');
        if ($tr.length === 0) return;

        const added = addItemToGrid($tableBody, {
            itemCode: $tr.data('item-code') || '',
            itemName: $tr.data('item-name') || '',
            unit: $tr.data('unit') || '',
            orderQty: 1,
            notReceipt: 0,
            inStock: 0,
            accIn: 0,
            buy: 0,
            price: 0,
            note: '',
            normMain: 0,
            newItem: false
        });

        if (added) {
            autoSaveDraftAfterGridChange('add-detail');
        }
    });

    // Add Detail opens item lookup
    $('#createNewItemBtn').off('click').on('click', function () {
        $('#newItemName').val('');
        $('#newItemUnit').val('');
        $('#newItemError').text('').addClass('d-none');
        $('#mrNewItemModal').modal('show');
    });

    // Remove Item deletes the selected rows
    $('#createNewItemConfirmBtn').off('click').on('click', async function () {
        const itemName = ($('#newItemName').val() || '').toString().trim();
        const unit = ($('#newItemUnit').val() || '').toString().trim();

        if (!itemName) {
            $('#newItemError').text('Item Name is required.').removeClass('d-none');
            focusErrorField($('#newItemName'));
            return;
        }

        try {
            const createdItem = await createQuickItem(itemName, unit);
            const added = addItemToGrid($tableBody, {
                itemCode: createdItem.itemCode || '',
                itemName: createdItem.itemName || '',
                unit: createdItem.unit || '',
                orderQty: 1,
                notReceipt: 0,
                inStock: 0,
                accIn: 0,
                buy: 0,
                price: 0,
                note: '',
                normMain: 0,
                newItem: true
        });
            if (added) {
                autoSaveDraftAfterGridChange('create-new-item');
            }
        } catch (error) {
            $('#newItemError').text(error.message || 'Cannot create new item.').removeClass('d-none');
        }
    });

    // Search in item lookup
    $('#lookupKeyword').off('keydown').on('keydown', function (event) {
        if (event.key !== 'Enter') return;
        event.preventDefault();
        runItemLookupSearch();
    });

    $('#lookupCheckStore').off('change').on('change', function () {
        if (($('#lookupKeyword').val() || '').toString().trim().length > 0) {
            runItemLookupSearch();
        }
    });

    // Add item from lookup to the grid
    syncEmptyRow($tableBody);
    syncLineInputNames($tableBody);
    refreshLineIndexes($tableBody);
    syncPostedLines($tableBody, $('#linesJsonInput'));
}

function promptRejectItemPayload($selectedRows) {
    const selectedItems = $selectedRows.map(function () {
        const $row = $(this);
        return {
            id: toNullableInt($row.find('.mr-line-id').val()),
            code: ($row.find('.mr-line-itemcode').val() || '').toString().trim()
        };
    }).get().filter(row => row.id !== null && row.id > 0);

    if (selectedItems.length === 0) {
        alert('Please select item row(s) to reject.');
        return null;
    }

    const confirmMessage = selectedItems.length === 1
        ? `Are you sure to reject item ${selectedItems[0].code || ('#' + selectedItems[0].id)}?`
        : `Are you sure to reject ${selectedItems.length} selected item(s)?`;
    if (!window.confirm(confirmMessage)) {
        return null;
    }

    return {
        lineIds: selectedItems.map(row => row.id)
    };
}

function getMrLineCount($tableBody) {
    if ($tableBody.length === 0) return 0;

    return $tableBody.find('.mr-line-row').filter(function () {
        const lineId = toNullableInt($(this).find('.mr-line-id').val());
        return lineId !== null && lineId > 0;
    }).length;
}

function setSelectedMrLineRow($tableBody, $row) {
    if ($tableBody.length === 0 || $row.length === 0) return;

    const alreadySelected = $row.hasClass('is-selected');
    if (alreadySelected) {
        clearSelectedMrLineRow($tableBody);
        return;
    }

    $tableBody.find('.mr-line-row')
        .removeClass('is-selected')
        .find('.mr-line-select')
        .prop('checked', false);

    $row.addClass('is-selected');
    $row.find('.mr-line-select').prop('checked', true);
}

function clearSelectedMrLineRow($tableBody) {
    if ($tableBody.length === 0) return;

    $tableBody.find('.mr-line-row')
        .removeClass('is-selected')
        .find('.mr-line-select')
        .prop('checked', false);
}

function validateMainForm(actionMode) {
    const fields = [
        { id: 'Input_DateCreate', name: 'Date Create' },
        { id: 'Input_StoreGroup', name: 'Store Group' },
        { id: 'Input_AccordingTo', name: 'Description' }
    ];

    for (let field of fields) {
        let $el = $('#' + field.id);
        if ($el.length === 0 || $el.is(':disabled')) continue;

        if (!$el.val() || $el.val().toString().trim() === '' || $el.val() === '0') {
            alert('Please enter/select: ' + field.name);
            focusErrorField($el);
            return false;
        }
    }

    // Create new item opens the popup
    if (new Date($('#Input_FromDate').val()) > new Date($('#Input_ToDate').val())) {
        alert("Error: 'From Date' must be less than or equal to 'To Date'.");
        return false;
    }

    const normalizedActionMode = (actionMode || '').toString().toLowerCase();
    if (normalizedActionMode === 'draft-save') {
        return true;
    }

    if (normalizedActionMode === 'reject-item') {
        return true;
    }

    const $rows = $('#mrLineTableBody').find('.mr-line-row');
    if ($rows.length === 0) {
        alert('Please add at least one item.');
        return false;
    }

    let firstInvalidInput = null;
    let firstInvalidMessage = '';
    $rows.each(function (index) {
        if (firstInvalidInput) return false;

        const lineNo = index + 1;
        const $row = $(this);
        const itemCode = ($row.find('.mr-line-itemcode').val() || '').toString().trim();
        const $orderInput = $row.find('.mr-line-order');
        const $noteInput = $row.find('.mr-line-note');

        if (!itemCode) {
            firstInvalidInput = $orderInput.length ? $orderInput : $noteInput;
            firstInvalidMessage = `Line ${lineNo}: Item Code is required.`;
            return false;
        }

        const orderQty = toNumber($orderInput.val());
        if (orderQty <= 0) {
            firstInvalidInput = $orderInput;
            firstInvalidMessage = `Line ${lineNo}: Order quantity must be greater than 0.`;
            return false;
        }

        const numberChecks = [
            { selector: '.mr-line-notrec', label: 'NotRec' },
            { selector: '.mr-line-accin', label: 'Acc.In' },
            { selector: '.mr-line-buy', label: 'Buy' },
            { selector: '.mr-line-price', label: 'Price' }
        ];

        for (let i = 0; i < numberChecks.length; i++) {
            const check = numberChecks[i];
            const $input = $row.find(check.selector);
            const value = toNumber($input.val());
            if (value < 0) {
                firstInvalidInput = $orderInput;
                firstInvalidMessage = `Line ${lineNo}: ${check.label} must be greater than or equal to 0.`;
                return false;
            }
        }
    });

    if (firstInvalidInput) {
        alert(firstInvalidMessage || 'Invalid line data.');
        focusErrorField(firstInvalidInput);
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

function toBoolData(value) {
    return (value || '').toString().trim().toLowerCase() === 'true';
}

function toIntData(value) {
    const parsed = Number.parseInt((value || '').toString().trim(), 10);
    return Number.isFinite(parsed) ? parsed : 0;
}

function toNullableInt(value) {
    const raw = (value || '').toString().trim();
    if (!raw) {
        return null;
    }

    const parsed = Number.parseInt(raw, 10);
    return Number.isFinite(parsed) ? parsed : null;
}

// Grid line items

function toNumber(value) {
    const parsed = Number.parseFloat((value || '').toString().trim());
    return Number.isFinite(parsed) ? parsed : 0;
}

function refreshLineIndexes($tableBody) {
    $('#mrTotalItemText').text('Total Item ' + $tableBody.find('.mr-line-row').length);
}

function syncEmptyRow($tableBody) {
    const hasRows = $tableBody.find('.mr-line-row').length > 0;
    const $emptyRow = $tableBody.find('.mr-line-empty');

    if (!hasRows && $emptyRow.length === 0) {
        $tableBody.append('<tr class="mr-line-empty"><td colspan="10" class="text-center text-muted">No line items.</td></tr>');
        return;
    }

    if (hasRows && $emptyRow.length > 0) {
        $emptyRow.remove();
    }
}

function createLineRowHtml(row) {
    return `
        <tr class="mr-line-row">
            <td class="mr-line-select-cell text-center align-middle">
                <input type="radio" class="mr-line-select" name="mrLineSelect" aria-label="Select row" />
            </td>
            <td>
                <input type="hidden" class="mr-line-id" value="${row.id ?? ''}" />
                <span class="mr-readonly-cell-text">${escapeHtml(row.itemCode || '')}</span>
                <input type="hidden" class="mr-line-itemcode" value="${escapeHtml(row.itemCode || '')}" />
            </td>
            <td>
                <span class="mr-readonly-cell-text">${escapeHtml(row.itemName || '')}</span>
                <input type="hidden" class="mr-line-itemname" value="${escapeHtml(row.itemName || '')}" />
            </td>
            <td>
                <span class="mr-readonly-cell-text">${escapeHtml(row.unit || '')}</span>
                <input type="hidden" class="mr-line-unit" value="${escapeHtml(row.unit || '')}" />
            </td>
            <td><input type="number" min="0.01" step="0.01" class="form-control form-control-sm mr-line-order" value="${row.orderQty ?? 0}" /></td>
            <td>
                <span class="mr-readonly-cell-text">${row.notReceipt ?? 0}</span>
                <input type="hidden" class="mr-line-notrec" value="${row.notReceipt ?? 0}" />
            </td>
            <td>
                <span class="mr-readonly-cell-text">${row.inStock ?? 0}</span>
                <input type="hidden" class="mr-line-in" value="${row.inStock ?? 0}" />
            </td>
            <td>
                <span class="mr-readonly-cell-text">${row.accIn ?? 0}</span>
                <input type="hidden" class="mr-line-accin" value="${row.accIn ?? 0}" />
            </td>
            <td>
                <span class="mr-readonly-cell-text mr-line-buy-text">${row.buy ?? 0}</span>
                <input type="hidden" class="mr-line-buy" value="${row.buy ?? 0}" />
            </td>
            <td>
                <input type="text" class="form-control form-control-sm mr-line-note" value="${escapeHtml(row.note || '')}" />
                <input type="hidden" class="mr-line-normmain" value="${row.normMain ?? 0}" />
                <input type="hidden" class="mr-line-price" value="${row.price ?? 0}" />
                <input type="hidden" class="mr-line-new-item" value="${row.newItem ? 'true' : 'false'}" />
            </td>
        </tr>`;
}

function addItemToGrid($tableBody, item) {
    const newItemCode = (item.itemCode || '').toString().trim().toLowerCase();
    if (newItemCode) {
        const $existed = $tableBody.find('.mr-line-row').filter(function () {
            const rowCode = ($(this).find('.mr-line-itemcode').val() || '').toString().trim().toLowerCase();
            return rowCode === newItemCode;
        });

        if ($existed.length > 0) {
            focusErrorField($existed.first().find('.mr-line-order'));
            return false;
        }
    }

    $tableBody.append(createLineRowHtml(item));
    syncEmptyRow($tableBody);
    syncLineInputNames($tableBody);
    refreshLineIndexes($tableBody);
    return true;
}

function serializeLines($tableBody) {
    const payload = [];

    $tableBody.find('.mr-line-row').each(function () {
        const $row = $(this);
        payload.push({
            id: toNullableInt($row.find('.mr-line-id').val()),
            itemCode: ($row.find('.mr-line-itemcode').val() || '').toString().trim(),
            itemName: ($row.find('.mr-line-itemname').val() || '').toString().trim(),
            unit: ($row.find('.mr-line-unit').val() || '').toString().trim(),
            orderQty: toNumber($row.find('.mr-line-order').val()),
            notReceipt: toNumber($row.find('.mr-line-notrec').val()),
            inStock: toNumber($row.find('.mr-line-in').val()),
            accIn: toNumber($row.find('.mr-line-accin').val()),
            buy: toNumber($row.find('.mr-line-buy').val()),
            price: toNumber($row.find('.mr-line-price').val()),
            note: ($row.find('.mr-line-note').val() || '').toString().trim(),
            normMain: toNumber($row.find('.mr-line-normmain').val()),
            newItem: ['1', 'true'].includes((($row.find('.mr-line-new-item').val() || '').toString().trim().toLowerCase())),
            selected: true
        });
    });

    return payload;
}

function syncLineInputNames($tableBody) {
    $tableBody.find('.mr-line-row').each(function (index) {
        const $row = $(this);
        setLineInputName($row, '.mr-line-id', index, 'Id');
        setLineInputName($row, '.mr-line-itemcode', index, 'ItemCode');
        setLineInputName($row, '.mr-line-itemname', index, 'ItemName');
        setLineInputName($row, '.mr-line-unit', index, 'Unit');
        setLineInputName($row, '.mr-line-order', index, 'OrderQty');
        setLineInputName($row, '.mr-line-notrec', index, 'NotReceipt');
        setLineInputName($row, '.mr-line-in', index, 'InStock');
        setLineInputName($row, '.mr-line-accin', index, 'AccIn');
        setLineInputName($row, '.mr-line-buy', index, 'Buy');
        setLineInputName($row, '.mr-line-note', index, 'Note');
        setLineInputName($row, '.mr-line-normmain', index, 'NormMain');
        setLineInputName($row, '.mr-line-price', index, 'Price');
        setLineInputName($row, '.mr-line-new-item', index, 'NewItem');
    });
}

function setLineInputName($row, selector, index, propertyName) {
    const $input = $row.find(selector);
    if ($input.length === 0) return;
    $input.attr('name', `Lines[${index}].${propertyName}`);
}

function syncPostedLines($tableBody, $linesJsonInput) {
    if ($tableBody.length === 0 || $linesJsonInput.length === 0) return;
    syncLineInputNames($tableBody);
    $linesJsonInput.val(JSON.stringify(serializeLines($tableBody)));
}

function autoSaveDraftAfterGridChange(draftSaveAction) {
    const $form = $('#materialRequestDetailForm');
    if ($form.length === 0) return;

    const mode = (new URLSearchParams(window.location.search).get('mode') || '').toString().toLowerCase();
    if (mode === 'view') return;

    const currentStatusId = toIntData($form.data('current-status'));
    const canSave = toBoolData($form.data('can-save'));
    if (currentStatusId !== -1 || !canSave) return;

    syncPostedLines($('#mrLineTableBody'), $('#linesJsonInput'));
    $('#workflowActionModeInput').val('draft-save');
    $('#draftSaveActionInput').val((draftSaveAction || '').toString());
    $('#rejectItemLineIdsJsonInput').val('');

    submitDraftSaveAjax($form);
}

function submitDraftSaveAjax($form) {
    if ($form.length === 0) return;

    const form = $form[0];
    const formData = new FormData(form);
    const targetUrl = new URL(window.location.href);
    targetUrl.searchParams.delete('handler');

    $.ajax({
        url: targetUrl.toString(),
        type: 'POST',
        data: formData,
        processData: false,
        contentType: false,
        headers: { 'X-Requested-With': 'XMLHttpRequest' },
        success: function (res) {
            if (res && res.success) {
                if (res.requestNo !== undefined && res.requestNo !== null && res.requestNo !== '') {
                    updateDraftSavedRequestNo(res.requestNo);
                }
                showDetailSuccessMessage(res.message || 'Line changes saved.');
                return;
            }

            showDetailErrorMessage((res && res.message) ? res.message : 'Cannot save Material Request.');
        },
        error: function (xhr) {
            const responseMessage = xhr && xhr.responseJSON && xhr.responseJSON.message
                ? xhr.responseJSON.message
                : (xhr && xhr.responseText ? xhr.responseText : '');
            showDetailErrorMessage(responseMessage || 'Cannot save Material Request.');
        }
    });
}

function updateDraftSavedRequestNo(requestNo) {
    const normalized = (requestNo || '').toString().trim();
    if (!normalized) return;

    $('#Id').val(normalized);

    const url = new URL(window.location.href);
    url.searchParams.set('id', normalized);
    url.searchParams.set('mode', 'edit');
    window.history.replaceState({}, '', url.toString());
}

function showDetailSuccessMessage(message) {
    const $target = $('#materialRequestDetailForm .card-body').first();
    const safeMessage = escapeHtml(message || 'Saved successfully.');
    const $alert = $(`
        <div class="alert alert-success alert-dismissible fade show mr-auto-message" role="alert">
            <strong><i class="fas fa-check-circle"></i></strong> ${safeMessage}
            <button type="button" class="close" data-dismiss="alert" aria-label="Close">
                <span aria-hidden="true">&times;</span>
            </button>
        </div>`);

    $target.find('.mr-auto-message').remove();
    if ($target.length > 0) {
        $target.prepend($alert);
    } else {
        alert(message || 'Saved successfully.');
    }
}

function showDetailErrorMessage(message) {
    alert(message || 'Cannot save Material Request.');
}

// Lookup and quick item

async function runItemLookupSearch() {
    try {
        const keyword = ($('#lookupKeyword').val() || '').toString().trim();
        const checkBalance = $('#lookupCheckStore').is(':checked');
        const rows = await searchItems(keyword, checkBalance);
        renderLookupResults($('#lookupResultBody'), rows);
    } catch (error) {
        renderLookupResults($('#lookupResultBody'), []);
        alert(error.message || 'Cannot load item list.');
    }
}

function renderLookupResults($resultBody, items) {
    $resultBody.empty();
    if (!items || items.length === 0) {
        $resultBody.append('<tr><td colspan="4" class="text-center text-muted">No data</td></tr>');
        return;
    }

    items.forEach(function (item) {
        const $tr = $(
            `<tr>
                <td>${escapeHtml(item.itemCode || '')}</td>
                <td>${escapeHtml(item.itemName || '')}</td>
                <td>${escapeHtml(item.unit || '')}</td>
                <td class="text-center">
                    <button type="button" class="btn btn-sm btn-outline-primary lookup-add-item-btn">Add</button>
                </td>
            </tr>`
        );

        $tr.attr('data-item-code', item.itemCode || '');
        $tr.attr('data-item-name', item.itemName || '');
        $tr.attr('data-unit', item.unit || '');
        $resultBody.append($tr);
    });
}

function searchItems(keyword, checkBalanceInStore) {
    return new Promise(function (resolve, reject) {
        const url = new URL(window.location.href);
        url.searchParams.set('handler', 'SearchItems');
        if (keyword) {
            url.searchParams.set('keyword', keyword);
        }
        if (checkBalanceInStore) {
            url.searchParams.set('checkBalanceInStore', 'true');
        }

        $.ajax({
            url: url.toString(),
            type: 'GET',
            headers: { 'X-Requested-With': 'XMLHttpRequest' },
            success: function (res) {
                if (res && res.success) {
                    resolve(res.data || []);
                } else {
                    reject(new Error((res && res.message) ? res.message : 'Cannot load item list.'));
                }
            },
            error: function () {
                reject(new Error('Cannot load item list.'));
            }
        });
    });
}

function createQuickItem(itemName, unit) {
    return new Promise(function (resolve, reject) {
        const token = $('input[name="__RequestVerificationToken"]').first().val() || '';

        $.ajax({
            url: '?handler=CreateItem',
            type: 'POST',
            headers: { 'RequestVerificationToken': token },
            data: { itemName: itemName || '', unit: unit || '' },
            success: function (res) {
                if (res && res.success) {
                    resolve(res.data || {});
                } else {
                    reject(new Error((res && res.message) ? res.message : 'Cannot create new item.'));
                }
            },
            error: function () {
                reject(new Error('Cannot create new item.'));
            }
        });
    });
}

// Shared helpers

function escapeHtml(value) {
    return (value || '').toString()
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}



