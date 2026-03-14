$(document).ready(function () {
    const $form = $("#materialRequestDetailForm");

    // 1) Lấy mode từ query string để xác định trạng thái xử lý trang.
    const urlParams = new URLSearchParams(window.location.search);
    const queryMode = (urlParams.get("mode") || "").toLowerCase();
    const actionPerm = {
        canSave: toBoolData($form.data("can-save")),
        canSubmit: toBoolData($form.data("can-submit")),
        canApprove: toBoolData($form.data("can-approve")),
        canReject: toBoolData($form.data("can-reject"))
    };
    const mode = queryMode || (actionPerm.canSave ? "edit" : "view");

    // 2) Khởi tạo toàn bộ hành vi trang.
    initializePage(mode, actionPerm);

    // 3) Theo dõi submitter đang bấm để giữ đúng handler Save/Submit/Approve/Reject.
    $("#mrSaveBtn, #mrSubmitBtn, #mrApproveBtn, #mrRejectBtn").off("click").on("click", function () {
        window.__mrSubmitter = this;
        const $tableBody = $("#mrLineTableBody");
        const $linesJsonInput = $("#linesJsonInput");
        syncPostedLines($tableBody, $linesJsonInput);
    });

    // 4) Chặn submit tạm thời để validate trước khi submit thật.
    $("#materialRequestDetailForm").off("submit").on("submit", function (e) {
        const $form = $(this);
        const $tableBody = $("#mrLineTableBody");
        const $linesJsonInput = $("#linesJsonInput");
        syncPostedLines($tableBody, $linesJsonInput);

        if (mode === "view") return true;

        e.preventDefault();
        if (validateMainForm($form, $tableBody)) {
            submitRealForm($form, e.originalEvent ? e.originalEvent.submitter : null);
        }
    });
});

/* ==========================================================================
   CÁC HÀM KHỞI TẠO & VALIDATION (nội bộ)
   ========================================================================== */

function initializePage(mode, actionPerm) {
    const $tableBody = $("#mrLineTableBody");
    const $linesJsonInput = $("#linesJsonInput");

    // Đồng nhất trạng thái input/select/textarea theo mode + quyền save.
    applyFormEditableState(mode, actionPerm || {});

    // Đồng nhất trạng thái nút action theo quyền (xử lý ở JS).
    applyActionButtonStates(mode, actionPerm || {});

    // Đồng bộ checkbox Not issue với hidden input.
    $("#NoIssueCheck").off("change").on("change", function () {
        $("#Input_NoIssue").val(this.checked ? "1" : "0");
    });

    // Chọn dòng trong grid line item.
    $tableBody.off("click.mrLine").on("click.mrLine", ".mr-line-row", function (event) {
        const $row = $(this);

        if (event.ctrlKey || event.metaKey) {
            $row.toggleClass("is-selected");
            return;
        }

        $tableBody.find(".mr-line-row.is-selected").not($row).removeClass("is-selected");
        $row.addClass("is-selected");
    });

    // Nút Add Detail mở popup lookup item.
    $("#addMrLineBtn").off("click").on("click", function () {
        $("#mrItemLookupModal").modal("show");
        runItemLookupSearch();
    });

    // Nút Remove Item xóa các dòng đang chọn.
    $("#removeMrLineBtn").off("click").on("click", function () {
        const $selectedRows = $tableBody.find(".mr-line-row.is-selected");
        if ($selectedRows.length === 0) {
            alert("Please select item row(s) to remove.");
            return;
        }

        $selectedRows.remove();
        syncEmptyRow($tableBody);
        syncLineInputNames($tableBody);
        refreshLineIndexes($tableBody);
    });

    // Search trong popup lookup.
    $("#lookupSearchBtn").off("click").on("click", function () {
        runItemLookupSearch();
    });

    // Add item từ lookup vào grid.
    $("#lookupResultBody").off("click.mrAddItem").on("click.mrAddItem", ".lookup-add-item-btn", function () {
        const $tr = $(this).closest("tr");
        if ($tr.length === 0) return;

        addItemToGrid($tableBody, {
            itemCode: $tr.data("item-code") || "",
            itemName: $tr.data("item-name") || "",
            unit: $tr.data("unit") || "",
            orderQty: 0,
            notReceipt: 0,
            inStock: 0,
            accIn: 0,
            buy: 0,
            price: 0,
            note: "",
            newItem: false
        });
    });

    // Nút Create new item mở popup tạo nhanh.
    $("#createNewItemBtn").off("click").on("click", function () {
        $("#newItemName").val("");
        $("#newItemUnit").val("");
        $("#newItemError").text("").addClass("d-none");
        $("#mrNewItemModal").modal("show");
    });

    // Xác nhận tạo item mới.
    $("#createNewItemConfirmBtn").off("click").on("click", async function () {
        const itemName = ($("#newItemName").val() || "").toString().trim();
        const unit = ($("#newItemUnit").val() || "").toString().trim();

        if (!itemName) {
            $("#newItemError").text("Item Name is required.").removeClass("d-none");
            focusErrorField($("#newItemName"));
            return;
        }

        try {
            const createdItem = await createQuickItem(itemName, unit);
            addItemToGrid($tableBody, {
                itemCode: createdItem.itemCode || "",
                itemName: createdItem.itemName || "",
                unit: createdItem.unit || "",
                orderQty: 0,
                notReceipt: 0,
                inStock: 0,
                accIn: 0,
                buy: 0,
                price: 0,
                note: "",
                newItem: true
            });
            $("#mrNewItemModal").modal("hide");
        } catch (error) {
            $("#newItemError").text(error.message || "Cannot create new item.").removeClass("d-none");
        }
    });

    // Enter trong ô search lookup thì chạy search luôn.
    $("#lookupKeyword").off("keydown").on("keydown", function (event) {
        if (event.key !== "Enter") return;
        event.preventDefault();
        runItemLookupSearch();
    });

    // Nút Print xử lý bằng JS để thống nhất pattern event-binding.
    $("#mrPrintBtn").off("click").on("click", function () {
        window.print();
    });


    // Đồng bộ dữ liệu line ban đầu.
    syncEmptyRow($tableBody);
    syncLineInputNames($tableBody);
    refreshLineIndexes($tableBody);
    syncPostedLines($tableBody, $linesJsonInput);
}


function validateMainForm($form, $tableBody) {
    const fields = [
        { id: "Input_DateCreate", name: "Date Create" },
        { id: "Input_StoreGroup", name: "Store Group" },
        { id: "Input_AccordingTo", name: "Description" }
    ];

    for (let i = 0; i < fields.length; i++) {
        const field = fields[i];
        const $el = $("#" + field.id);
        if ($el.length === 0 || $el.is(":disabled")) continue;

        const value = ($el.val() || "").toString().trim();
        if (value === "" || value === "0") {
            alert("Please enter/select: " + field.name);
            focusErrorField($el);
            return false;
        }
    }

    const fromDate = ($("#Input_FromDate").val() || "").toString().trim();
    const toDate = ($("#Input_ToDate").val() || "").toString().trim();
    if (fromDate && toDate && new Date(fromDate) > new Date(toDate)) {
        alert("Error: 'From Date' must be less than or equal to 'To Date'.");
        focusErrorField($("#Input_ToDate"));
        return false;
    }

    const $rows = $tableBody.find(".mr-line-row");
    if ($rows.length === 0) {
        alert("Please add at least one item.");
        return false;
    }

    let firstInvalidInput = null;
    let firstInvalidMessage = "";
    $rows.each(function (index) {
        if (firstInvalidInput) return false;

        const lineNo = index + 1;
        const $row = $(this);
        const itemCode = ($row.find(".mr-line-itemcode").val() || "").toString().trim();
        const $orderInput = $row.find(".mr-line-order");
        const $noteInput = $row.find(".mr-line-note");

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
            { selector: ".mr-line-notrec", label: "NotRec" },
            { selector: ".mr-line-in", label: "In" },
            { selector: ".mr-line-accin", label: "Acc.In" },
            { selector: ".mr-line-buy", label: "Buy" },
            { selector: ".mr-line-price", label: "Price" }
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
        alert(firstInvalidMessage || "Invalid line data.");
        focusErrorField(firstInvalidInput);
        return false;
    }

    return true;
}

function focusErrorField($el) {
    const $tabPane = $el.closest(".tab-pane");
    if ($tabPane.length > 0 && !$tabPane.hasClass("active")) {
        $('.nav-tabs a[href="#' + $tabPane.attr("id") + '"]').tab("show");
    }
    setTimeout(function () { $el.trigger("focus"); }, 300);
}

function submitRealForm($form, nativeSubmitter) {
    const form = $form.get(0);
    const submitter = nativeSubmitter
        || window.__mrSubmitter
        || $("#mrSaveBtn").get(0)
        || $("#mrSubmitBtn").get(0)
        || $("#mrApproveBtn").get(0)
        || $("#mrRejectBtn").get(0)
        || null;

    $form.off("submit");
    if (submitter && typeof form.requestSubmit === "function") {
        form.requestSubmit(submitter);
        return;
    }

    form.submit();
}

function applyActionButtonStates(mode, actionPerm) {
    const isViewMode = (mode || "").toLowerCase() === "view";
    const canSave = !!actionPerm.canSave;
    const canSubmit = !!actionPerm.canSubmit;
    const canApprove = !!actionPerm.canApprove;
    const canReject = !!actionPerm.canReject;

    $("#addMrLineBtn, #removeMrLineBtn, #createNewItemBtn, #calculateBtn, #mrSaveBtn")
        .prop("disabled", isViewMode || !canSave);
    $("#mrSubmitBtn").prop("disabled", !canSubmit);
    $("#mrApproveBtn").prop("disabled", !canApprove);
    $("#mrRejectBtn").prop("disabled", !canReject);
}

function applyFormEditableState(mode, actionPerm) {
    const isViewMode = (mode || "").toLowerCase() === "view";
    const canSave = !!actionPerm.canSave;
    const disableEditFields = isViewMode || !canSave;
    const $form = $("#materialRequestDetailForm");

    $form.find("input, textarea, select")
        .not("[type='hidden'], #mrSaveBtn, #mrSubmitBtn, #mrApproveBtn, #mrRejectBtn, #addMrLineBtn, #removeMrLineBtn, #createNewItemBtn, #calculateBtn, #mrPrintBtn")
        .prop("disabled", disableEditFields);

    if (toBoolData($form.data("store-group-locked"))) {
        $("#Input_StoreGroup").prop("disabled", true);
    }

    $("#mrLineTableBody .mr-line-order, #mrLineTableBody .mr-line-note").prop("disabled", disableEditFields);
}

function toBoolData(value) {
    return (value || "").toString().trim().toLowerCase() === "true";
}

/* ==========================================================================
   CÁC HÀM XỬ LÝ GRID LINE ITEM
   ========================================================================== */

function toNumber(value) {
    const parsed = Number.parseFloat((value || "").toString().trim());
    return Number.isFinite(parsed) ? parsed : 0;
}

function refreshLineIndexes($tableBody) {
    $("#mrTotalItemText").text("Total Item " + $tableBody.find(".mr-line-row").length);
}

function syncEmptyRow($tableBody) {
    const hasRows = $tableBody.find(".mr-line-row").length > 0;
    const $emptyRow = $tableBody.find(".mr-line-empty");

    if (!hasRows && $emptyRow.length === 0) {
        $tableBody.append('<tr class="mr-line-empty"><td colspan="9" class="text-center text-muted">No line items.</td></tr>');
        return;
    }

    if (hasRows && $emptyRow.length > 0) {
        $emptyRow.remove();
    }
}

function createLineRowHtml(row) {
    return `
        <tr class="mr-line-row">
            <td>
                <span class="mr-readonly-cell-text">${escapeHtml(row.itemCode || "")}</span>
                <input type="hidden" class="mr-line-itemcode" value="${escapeHtml(row.itemCode || "")}" />
            </td>
            <td>
                <span class="mr-readonly-cell-text">${escapeHtml(row.itemName || "")}</span>
                <input type="hidden" class="mr-line-itemname" value="${escapeHtml(row.itemName || "")}" />
            </td>
            <td>
                <span class="mr-readonly-cell-text">${escapeHtml(row.unit || "")}</span>
                <input type="hidden" class="mr-line-unit" value="${escapeHtml(row.unit || "")}" />
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
                <span class="mr-readonly-cell-text">${row.buy ?? 0}</span>
                <input type="hidden" class="mr-line-buy" value="${row.buy ?? 0}" />
            </td>
            <td>
                <input type="text" class="form-control form-control-sm mr-line-note" value="${escapeHtml(row.note || "")}" />
                <input type="hidden" class="mr-line-price" value="${row.price ?? 0}" />
                <input type="hidden" class="mr-line-new-item" value="${row.newItem ? "true" : "false"}" />
            </td>
        </tr>`;
}

function addItemToGrid($tableBody, item) {
    const newItemCode = (item.itemCode || "").toString().trim().toLowerCase();
    if (newItemCode) {
        const $existed = $tableBody.find(".mr-line-row").filter(function () {
            const rowCode = ($(this).find(".mr-line-itemcode").val() || "").toString().trim().toLowerCase();
            return rowCode === newItemCode;
        });

        if ($existed.length > 0) {
            focusErrorField($existed.first().find(".mr-line-order"));
            return;
        }
    }

    $tableBody.append(createLineRowHtml(item));
    syncEmptyRow($tableBody);
    syncLineInputNames($tableBody);
    refreshLineIndexes($tableBody);
}

function serializeLines($tableBody) {
    const payload = [];

    $tableBody.find(".mr-line-row").each(function () {
        const $row = $(this);
        payload.push({
            itemCode: ($row.find(".mr-line-itemcode").val() || "").toString().trim(),
            itemName: ($row.find(".mr-line-itemname").val() || "").toString().trim(),
            unit: ($row.find(".mr-line-unit").val() || "").toString().trim(),
            orderQty: toNumber($row.find(".mr-line-order").val()),
            notReceipt: toNumber($row.find(".mr-line-notrec").val()),
            inStock: toNumber($row.find(".mr-line-in").val()),
            accIn: toNumber($row.find(".mr-line-accin").val()),
            buy: toNumber($row.find(".mr-line-buy").val()),
            price: toNumber($row.find(".mr-line-price").val()),
            note: ($row.find(".mr-line-note").val() || "").toString().trim(),
            newItem: ["1", "true"].includes((($row.find(".mr-line-new-item").val() || "").toString().trim().toLowerCase())),
            selected: true
        });
    });

    return payload;
}

function syncLineInputNames($tableBody) {
    $tableBody.find(".mr-line-row").each(function (index) {
        const $row = $(this);
        setLineInputName($row, ".mr-line-itemcode", index, "ItemCode");
        setLineInputName($row, ".mr-line-itemname", index, "ItemName");
        setLineInputName($row, ".mr-line-unit", index, "Unit");
        setLineInputName($row, ".mr-line-order", index, "OrderQty");
        setLineInputName($row, ".mr-line-notrec", index, "NotReceipt");
        setLineInputName($row, ".mr-line-in", index, "InStock");
        setLineInputName($row, ".mr-line-accin", index, "AccIn");
        setLineInputName($row, ".mr-line-buy", index, "Buy");
        setLineInputName($row, ".mr-line-note", index, "Note");
        setLineInputName($row, ".mr-line-price", index, "Price");
        setLineInputName($row, ".mr-line-new-item", index, "NewItem");
    });
}

function setLineInputName($row, selector, index, propertyName) {
    const $input = $row.find(selector);
    if ($input.length === 0) return;
    $input.attr("name", `Lines[${index}].${propertyName}`);
}

function syncPostedLines($tableBody, $linesJsonInput) {
    if ($tableBody.length === 0 || $linesJsonInput.length === 0) return;
    syncLineInputNames($tableBody);
    $linesJsonInput.val(JSON.stringify(serializeLines($tableBody)));
}

/* ==========================================================================
   CÁC HÀM AJAX CHO LOOKUP / CREATE QUICK ITEM
   ========================================================================== */

async function runItemLookupSearch() {
    try {
        const keyword = ($("#lookupKeyword").val() || "").toString().trim();
        const checkBalance = $("#lookupCheckStore").is(":checked");
        const rows = await searchItems(keyword, checkBalance);
        renderLookupResults($("#lookupResultBody"), rows);
    } catch (error) {
        renderLookupResults($("#lookupResultBody"), []);
        alert(error.message || "Cannot load item list.");
    }
}

function renderLookupResults($resultBody, items) {
    $resultBody.empty();
    if (!items || items.length === 0) {
        $resultBody.append('<tr><td colspan="4" class="text-center text-muted">No data</td></tr>');
        return;
    }

    items.forEach(function (item) {
        const $tr = $(`
            <tr>
                <td>${escapeHtml(item.itemCode || "")}</td>
                <td>${escapeHtml(item.itemName || "")}</td>
                <td>${escapeHtml(item.unit || "")}</td>
                <td class="text-center">
                    <button type="button" class="btn btn-sm btn-outline-primary lookup-add-item-btn">Add</button>
                </td>
            </tr>
        `);

        $tr.attr("data-item-code", item.itemCode || "");
        $tr.attr("data-item-name", item.itemName || "");
        $tr.attr("data-unit", item.unit || "");
        $resultBody.append($tr);
    });
}

function searchItems(keyword, checkBalanceInStore) {
    return new Promise(function (resolve, reject) {
        const url = new URL(window.location.href);
        url.searchParams.set("handler", "SearchItems");
        if (keyword) {
            url.searchParams.set("keyword", keyword);
        }
        if (checkBalanceInStore) {
            url.searchParams.set("checkBalanceInStore", "true");
        }

        $.ajax({
            url: url.toString(),
            type: "GET",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            success: function (res) {
                if (res && res.success) {
                    resolve(res.data || []);
                } else {
                    reject(new Error((res && res.message) ? res.message : "Cannot load item list."));
                }
            },
            error: function () {
                reject(new Error("Cannot load item list."));
            }
        });
    });
}

function createQuickItem(itemName, unit) {
    return new Promise(function (resolve, reject) {
        const token = $('input[name="__RequestVerificationToken"]').first().val() || "";

        $.ajax({
            url: "?handler=CreateItem",
            type: "POST",
            headers: { "RequestVerificationToken": token },
            data: { itemName: itemName || "", unit: unit || "" },
            success: function (res) {
                if (res && res.success) {
                    resolve(res.data || {});
                } else {
                    reject(new Error((res && res.message) ? res.message : "Cannot create new item."));
                }
            },
            error: function () {
                reject(new Error("Cannot create new item."));
            }
        });
    });
}

/* ==========================================================================
   HÀM DÙNG CHUNG
   ========================================================================== */

function escapeHtml(value) {
    return (value || "").toString()
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#39;");
}


