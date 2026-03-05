(() => {
    "use strict";

    /**
     * Parse số từ input, mặc định trả về 0 nếu rỗng/sai.
     */
    function toNumber(value) {
        const parsed = Number.parseFloat((value || "").toString().trim());
        return Number.isFinite(parsed) ? parsed : 0;
    }

    /**
     * Đọc token chống giả mạo từ form hiện tại.
     */
    function getAntiForgeryToken(form) {
        return form?.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
    }

    /**
     * Cập nhật lại số thứ tự từng dòng.
     */
    function refreshLineIndexes(tableBody) {
        const totalItemText = document.getElementById("mrTotalItemText");
        if (!totalItemText) return;
        const rows = tableBody.querySelectorAll(".mr-line-row");
        totalItemText.textContent = `Total Item ${rows.length}`;
    }

    /**
     * Ẩn/hiện dòng trống khi chưa có item.
     */
    function syncEmptyRow(tableBody) {
        const lineRows = tableBody.querySelectorAll(".mr-line-row");
        const emptyRow = tableBody.querySelector(".mr-line-empty");

        if (lineRows.length === 0) {
            if (!emptyRow) {
                const tr = document.createElement("tr");
                tr.className = "mr-line-empty";
                tr.innerHTML = '<td colspan="9" class="text-center text-muted">No line items.</td>';
                tableBody.appendChild(tr);
            }
            return;
        }

        if (emptyRow) {
            emptyRow.remove();
        }
    }

    /**
     * Tạo dòng mới trong bảng chi tiết MR.
     */
    function createLineRow(data, canEdit) {
        const rowData = data || {};
        const tr = document.createElement("tr");
        tr.className = "mr-line-row";
        tr.innerHTML = `
            <td><input type="text" class="form-control form-control-sm mr-line-itemcode" value="${rowData.itemCode || ""}" /></td>
            <td><input type="text" class="form-control form-control-sm mr-line-itemname" value="${rowData.itemName || ""}" /></td>
            <td><input type="text" class="form-control form-control-sm mr-line-unit" value="${rowData.unit || ""}" /></td>
            <td><input type="number" step="0.01" class="form-control form-control-sm mr-line-order" value="${rowData.orderQty ?? 0}" /></td>
            <td><input type="number" step="0.01" class="form-control form-control-sm mr-line-notrec" value="${rowData.notReceipt ?? 0}" /></td>
            <td><input type="number" step="0.01" class="form-control form-control-sm mr-line-in" value="${rowData.inStock ?? 0}" /></td>
            <td><input type="number" step="0.01" class="form-control form-control-sm mr-line-accin" value="${rowData.accIn ?? 0}" /></td>
            <td><input type="number" step="0.01" class="form-control form-control-sm mr-line-buy" value="${rowData.buy ?? 0}" /></td>
            <td>
                <input type="text" class="form-control form-control-sm mr-line-note" value="${rowData.note || ""}" />
                <input type="hidden" class="mr-line-price" value="${rowData.price ?? 0}" />
                <input type="hidden" class="mr-line-new-item" value="${rowData.newItem ? "1" : "0"}" />
            </td>
        `;
        return tr;
    }

    /**
     * Gom dữ liệu line-item thành JSON trước khi submit.
     */
    function serializeLines(tableBody) {
        const rows = tableBody.querySelectorAll(".mr-line-row");
        const payload = [];

        rows.forEach((row) => {
            payload.push({
                itemCode: row.querySelector(".mr-line-itemcode")?.value?.trim() || "",
                itemName: row.querySelector(".mr-line-itemname")?.value?.trim() || "",
                unit: row.querySelector(".mr-line-unit")?.value?.trim() || "",
                orderQty: toNumber(row.querySelector(".mr-line-order")?.value),
                notReceipt: toNumber(row.querySelector(".mr-line-notrec")?.value),
                inStock: toNumber(row.querySelector(".mr-line-in")?.value),
                accIn: toNumber(row.querySelector(".mr-line-accin")?.value),
                buy: toNumber(row.querySelector(".mr-line-buy")?.value),
                price: toNumber(row.querySelector(".mr-line-price")?.value),
                note: row.querySelector(".mr-line-note")?.value?.trim() || "",
                newItem: (row.querySelector(".mr-line-new-item")?.value || "0") === "1",
                selected: true
            });
        });

        return payload;
    }

    /**
     * Gán name theo index cho từng input line để server bind được trực tiếp vào Lines[i].*
     * (fallback khi LinesJson không được set đúng thời điểm submit).
     */
    function syncLineInputNames(tableBody) {
        const rows = Array.from(tableBody.querySelectorAll(".mr-line-row"));
        rows.forEach((row, index) => {
            const setName = (selector, name) => {
                const input = row.querySelector(selector);
                if (input) {
                    input.setAttribute("name", `Lines[${index}].${name}`);
                }
            };

            setName(".mr-line-itemcode", "ItemCode");
            setName(".mr-line-itemname", "ItemName");
            setName(".mr-line-unit", "Unit");
            setName(".mr-line-order", "OrderQty");
            setName(".mr-line-notrec", "NotReceipt");
            setName(".mr-line-in", "InStock");
            setName(".mr-line-accin", "AccIn");
            setName(".mr-line-buy", "Buy");
            setName(".mr-line-note", "Note");
            setName(".mr-line-price", "Price");
            setName(".mr-line-new-item", "NewItem");
        });
    }

    /**
     * Đồng bộ toàn bộ dữ liệu line trước khi submit về server.
     */
    function syncPostedLines(tableBody, linesJsonInput) {
        syncLineInputNames(tableBody);
        linesJsonInput.value = JSON.stringify(serializeLines(tableBody));
    }

    /**
     * Add item vào lưới, nếu item đã có thì chỉ focus dòng đó.
     */
    function addItemToGrid(tableBody, canEdit, item) {
        const itemCode = (item.itemCode || "").trim().toLowerCase();
        if (itemCode) {
            const existed = Array.from(tableBody.querySelectorAll(".mr-line-row"))
                .find((row) => (row.querySelector(".mr-line-itemcode")?.value || "").trim().toLowerCase() === itemCode);
            if (existed) {
                existed.querySelector(".mr-line-buy")?.focus();
                return;
            }
        }

        tableBody.appendChild(createLineRow(item, canEdit));
        syncEmptyRow(tableBody);
        syncLineInputNames(tableBody);
        refreshLineIndexes(tableBody);
    }

    /**
     * Render kết quả lookup item trong modal Create MR.
     */
    function renderLookupResults(resultBody, items) {
        resultBody.innerHTML = "";
        if (!items || items.length === 0) {
            resultBody.innerHTML = '<tr><td colspan="4" class="text-center text-muted">No data</td></tr>';
            return;
        }

        items.forEach((item) => {
            const tr = document.createElement("tr");
            tr.innerHTML = `
                <td>${item.itemCode || ""}</td>
                <td>${item.itemName || ""}</td>
                <td>${item.unit || ""}</td>
                <td class="text-center">
                    <button type="button" class="btn btn-sm btn-outline-primary lookup-add-item-btn">Add</button>
                </td>
            `;
            tr.dataset.itemCode = item.itemCode || "";
            tr.dataset.itemName = item.itemName || "";
            tr.dataset.unit = item.unit || "";
            resultBody.appendChild(tr);
        });
    }

    /**
     * Gọi handler SearchItems để lấy danh sách item.
     */
    async function searchItems(keyword, checkBalanceInStore) {
        const url = new URL(window.location.href);
        url.searchParams.set("handler", "SearchItems");
        if (keyword) {
            url.searchParams.set("keyword", keyword);
        }
        if (checkBalanceInStore) {
            url.searchParams.set("checkBalanceInStore", "true");
        }

        const response = await fetch(url.toString(), {
            method: "GET",
            headers: { "X-Requested-With": "XMLHttpRequest" }
        });

        if (!response.ok) {
            throw new Error("Cannot load item list.");
        }

        const json = await response.json();
        if (!json.success) {
            throw new Error(json.message || "Cannot load item list.");
        }

        return json.data || [];
    }

    /**
     * Gọi handler CreateItem để tạo nhanh item mới.
     */
    async function createQuickItem(form, itemName, unit) {
        const token = getAntiForgeryToken(form);
        const body = new URLSearchParams();
        body.set("itemName", itemName || "");
        body.set("unit", unit || "");
        if (token) {
            body.set("__RequestVerificationToken", token);
        }

        const response = await fetch("?handler=CreateItem", {
            method: "POST",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                "X-Requested-With": "XMLHttpRequest"
            },
            body: body.toString()
        });

        if (!response.ok) {
            throw new Error("Cannot create new item.");
        }

        const json = await response.json();
        if (!json.success) {
            throw new Error(json.message || "Cannot create new item.");
        }

        return json.data;
    }

    /**
     * Khởi tạo hành vi cho trang Detail Material Request.
     */
    function initMaterialRequestDetailPage() {
        const form = document.getElementById("materialRequestDetailForm");
        const tableBody = document.getElementById("mrLineTableBody");
        const addLineBtn = document.getElementById("addMrLineBtn");
        const removeLineBtn = document.getElementById("removeMrLineBtn");
        const createNewItemBtn = document.getElementById("createNewItemBtn");
        const saveBtn = document.getElementById("mrSaveBtn");
        const submitBtn = document.getElementById("mrSubmitBtn");
        const approveBtn = document.getElementById("mrApproveBtn");
        const rejectBtn = document.getElementById("mrRejectBtn");
        const linesJsonInput = document.getElementById("linesJsonInput");
        const noIssueCheck = document.getElementById("NoIssueCheck");
        const noIssueInput = document.getElementById("Input_NoIssue");
        const itemLookupModal = document.getElementById("mrItemLookupModal");
        const itemLookupBody = document.getElementById("lookupResultBody");
        const lookupKeyword = document.getElementById("lookupKeyword");
        const lookupCheckStore = document.getElementById("lookupCheckStore");
        const lookupSearchBtn = document.getElementById("lookupSearchBtn");
        const newItemModal = document.getElementById("mrNewItemModal");
        const newItemName = document.getElementById("newItemName");
        const newItemUnit = document.getElementById("newItemUnit");
        const newItemError = document.getElementById("newItemError");
        const createNewItemConfirmBtn = document.getElementById("createNewItemConfirmBtn");

        if (!form || !tableBody || !linesJsonInput) return;

        const canEdit = !!addLineBtn;

        const getSelectedLineRows = () =>
            Array.from(tableBody.querySelectorAll(".mr-line-row.is-selected"));

        tableBody.addEventListener("click", (event) => {
            const row = event.target.closest(".mr-line-row");
            if (!row) return;

            if (event.ctrlKey || event.metaKey) {
                row.classList.toggle("is-selected");
                return;
            }

            tableBody.querySelectorAll(".mr-line-row.is-selected").forEach((x) => {
                if (x !== row) {
                    x.classList.remove("is-selected");
                }
            });
            row.classList.add("is-selected");
        });

        noIssueCheck?.addEventListener("change", () => {
            if (noIssueInput) {
                noIssueInput.value = noIssueCheck.checked ? "1" : "0";
            }
        });

        addLineBtn?.addEventListener("click", async () => {
            if (itemLookupModal && typeof window.$ === "function") {
                window.$(itemLookupModal).modal("show");
                try {
                    const items = await searchItems((lookupKeyword?.value || "").trim(), !!lookupCheckStore?.checked);
                    renderLookupResults(itemLookupBody, items);
                } catch (err) {
                    renderLookupResults(itemLookupBody, []);
                    console.error(err);
                }
            } else {
                addItemToGrid(tableBody, canEdit, {});
            }
        });

        removeLineBtn?.addEventListener("click", () => {
            const selectedRows = getSelectedLineRows();
            if (selectedRows.length === 0) {
                return;
            }

            selectedRows.forEach((row) => row.remove());
            syncEmptyRow(tableBody);
            syncLineInputNames(tableBody);
            refreshLineIndexes(tableBody);
        });

        lookupSearchBtn?.addEventListener("click", async () => {
            try {
                const items = await searchItems((lookupKeyword?.value || "").trim(), !!lookupCheckStore?.checked);
                renderLookupResults(itemLookupBody, items);
            } catch (err) {
                renderLookupResults(itemLookupBody, []);
                console.error(err);
            }
        });

        itemLookupBody?.addEventListener("click", (event) => {
            const btn = event.target.closest(".lookup-add-item-btn");
            if (!btn) return;

            const tr = btn.closest("tr");
            if (!tr) return;

            addItemToGrid(tableBody, canEdit, {
                itemCode: tr.dataset.itemCode || "",
                itemName: tr.dataset.itemName || "",
                unit: tr.dataset.unit || "",
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

        createNewItemBtn?.addEventListener("click", () => {
            if (!newItemModal || typeof window.$ !== "function") return;
            if (newItemName) newItemName.value = "";
            if (newItemUnit) newItemUnit.value = "";
            if (newItemError) {
                newItemError.textContent = "";
                newItemError.classList.add("d-none");
            }
            window.$(newItemModal).modal("show");
        });

        createNewItemConfirmBtn?.addEventListener("click", async () => {
            const itemNameValue = (newItemName?.value || "").trim();
            const unitValue = (newItemUnit?.value || "").trim();

            if (!itemNameValue) {
                if (newItemError) {
                    newItemError.textContent = "Item Name is required.";
                    newItemError.classList.remove("d-none");
                }
                newItemName?.focus();
                return;
            }

            try {
                const created = await createQuickItem(form, itemNameValue, unitValue);
                addItemToGrid(tableBody, canEdit, {
                    itemCode: created.itemCode || "",
                    itemName: created.itemName || "",
                    unit: created.unit || "",
                    newItem: true
                });

                if (newItemModal && typeof window.$ === "function") {
                    window.$(newItemModal).modal("hide");
                }
            } catch (err) {
                if (newItemError) {
                    newItemError.textContent = err instanceof Error ? err.message : "Cannot create new item.";
                    newItemError.classList.remove("d-none");
                }
            }
        });

        [saveBtn, submitBtn, approveBtn, rejectBtn]
            .filter((x) => !!x)
            .forEach((btn) => {
                btn.addEventListener("click", () => {
                    syncPostedLines(tableBody, linesJsonInput);
                }, true);
            });

        form.addEventListener("submit", () => {
            syncPostedLines(tableBody, linesJsonInput);
        }, true);

        syncEmptyRow(tableBody);
        syncLineInputNames(tableBody);
        refreshLineIndexes(tableBody);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initMaterialRequestDetailPage);
    } else {
        initMaterialRequestDetailPage();
    }
})();
