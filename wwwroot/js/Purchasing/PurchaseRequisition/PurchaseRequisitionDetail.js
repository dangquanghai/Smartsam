(() => {
    "use strict";

    function getConfig() {
        return window.purchaseRequisitionDetailPage || { canSave: false };
    }

    function toNumber(value) {
        const n = Number.parseFloat(value);
        return Number.isFinite(n) ? n : 0;
    }

    function formatNumber(value) {
        return toNumber(value).toLocaleString("en-US", { minimumFractionDigits: 0, maximumFractionDigits: 2 });
    }

    function initDetail() {
        const detailsJsonEl = document.getElementById("DetailsJson");
        const rowsContainer = document.getElementById("prqDetailRows");
        const emptyRow = document.getElementById("prqDetailEmptyRow");
        const detailModal = document.getElementById("prqAddDetailModal");
        const addDetailBtn = document.getElementById("btnAddDetailConfirm");
        const itemSelect = document.getElementById("detailItemId");
        const unitInput = document.getElementById("detailUnit");
        const supplierSelect = document.getElementById("detailSupplierId");
        const qtyFromM = document.getElementById("detailQtyFromM");
        const qtyPur = document.getElementById("detailQtyPur");
        const unitPrice = document.getElementById("detailUnitPrice");
        const amountInput = document.getElementById("detailAmount");
        const remarkInput = document.getElementById("detailRemark");
        if (!detailsJsonEl || !rowsContainer || !emptyRow) return;

        let details = [];
        try {
            details = detailsJsonEl.value ? JSON.parse(detailsJsonEl.value) : [];
        } catch {
            details = [];
        }

        const syncHiddenInput = () => {
            detailsJsonEl.value = JSON.stringify(details.map((x) => ({
                detailId: x.detailId || 0,
                itemId: x.itemId,
                itemCode: x.itemCode || "",
                itemName: x.itemName || "",
                unit: x.unit || "",
                qtyFromM: x.qtyFromM,
                qtyPur: x.qtyPur,
                unitPrice: x.unitPrice,
                remark: x.remark,
                supplierId: x.supplierId,
                supplierText: x.supplierText || ""
            })));
        };

        const renderRows = () => {
            rowsContainer.querySelectorAll("tr[data-row='1']").forEach((x) => x.remove());
            if (details.length === 0) {
                emptyRow.style.display = "";
                syncHiddenInput();
                return;
            }

            emptyRow.style.display = "none";
            details.forEach((d, index) => {
                const removeButton = getConfig().canSave
                    ? `<button type="button" class="btn btn-xs btn-outline-danger border" data-remove-index="${index}">X</button>`
                    : "";

                const tr = document.createElement("tr");
                tr.dataset.row = "1";
                tr.innerHTML = `
                    <td>${d.itemCode || ""}</td>
                    <td>${d.itemName || ""}</td>
                    <td class="prq-center">${d.unit || ""}</td>
                    <td class="prq-center">${formatNumber(d.qtyFromM)}</td>
                    <td class="prq-center">${formatNumber(d.qtyPur)}</td>
                    <td class="prq-center">${formatNumber(d.unitPrice)}</td>
                    <td class="prq-center">${formatNumber(d.qtyPur * d.unitPrice)}</td>
                    <td class="prq-center">${d.remark || ""}</td>
                    <td>${d.supplierText || ""}</td>
                    <td class="text-center">${removeButton}</td>`;
                rowsContainer.appendChild(tr);
            });
            syncHiddenInput();
        };

        if (!getConfig().canSave) {
            renderRows();
            return;
        }

        const updateAmount = () => {
            if (!amountInput || !qtyPur || !unitPrice) return;
            amountInput.value = formatNumber(toNumber(qtyPur.value) * toNumber(unitPrice.value));
        };

        const resetDetailFields = () => {
            if (itemSelect) itemSelect.value = "";
            if (unitInput) unitInput.value = "";
            if (supplierSelect) supplierSelect.value = "";
            if (qtyFromM) qtyFromM.value = "0";
            if (qtyPur) qtyPur.value = "0";
            if (unitPrice) unitPrice.value = "0";
            if (remarkInput) remarkInput.value = "";
            updateAmount();
        };

        rowsContainer.addEventListener("click", (ev) => {
            const btn = ev.target.closest("button[data-remove-index]");
            if (!btn) return;
            const index = Number.parseInt(btn.getAttribute("data-remove-index"), 10);
            if (!Number.isInteger(index) || index < 0 || index >= details.length) return;
            details.splice(index, 1);
            renderRows();
        });

        itemSelect?.addEventListener("change", () => {
            const selected = itemSelect.selectedOptions[0];
            if (unitInput) unitInput.value = selected ? (selected.dataset.unit || "") : "";
        });

        qtyPur?.addEventListener("input", updateAmount);
        unitPrice?.addEventListener("input", updateAmount);
        updateAmount();

        addDetailBtn?.addEventListener("click", () => {
            if (!itemSelect || !supplierSelect || !qtyFromM || !qtyPur || !unitPrice || !remarkInput) return;
            const selectedItem = itemSelect.selectedOptions[0];
            if (!selectedItem || !selectedItem.value) {
                alert("Please select an item.");
                itemSelect.focus();
                return;
            }
            const qtyPurValue = toNumber(qtyPur.value);
            if (qtyPurValue <= 0) {
                alert("QtyPur must be greater than 0.");
                qtyPur.focus();
                return;
            }
            const supplierOption = supplierSelect.selectedOptions[0];
            details.push({
                detailId: 0,
                itemId: Number.parseInt(selectedItem.value, 10),
                itemCode: selectedItem.dataset.code || "",
                itemName: selectedItem.dataset.name || "",
                unit: selectedItem.dataset.unit || "",
                qtyFromM: toNumber(qtyFromM.value),
                qtyPur: qtyPurValue,
                unitPrice: toNumber(unitPrice.value),
                remark: remarkInput.value.trim(),
                supplierId: supplierOption && supplierOption.value ? Number.parseInt(supplierOption.value, 10) : null,
                supplierText: supplierOption && supplierOption.value ? supplierOption.text : ""
            });
            renderRows();
            resetDetailFields();
            if (window.jQuery) {
                window.jQuery(detailModal).modal("hide");
            }
        });

        renderRows();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initDetail);
    } else {
        initDetail();
    }
})();
