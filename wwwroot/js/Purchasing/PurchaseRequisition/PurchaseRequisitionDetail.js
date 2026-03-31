(() => {
    "use strict";

    function getConfig() {
        return window.purchaseRequisitionDetailPage || { canSave: false, canMoveToMr: false };
    }

    function getAllowedExtensions() {
        return String(getConfig().allowedAttachmentExtensions || "")
            .split(",")
            .map((item) => item.trim().toLowerCase())
            .filter(Boolean);
    }

    function getMaxAttachmentSizeBytes() {
        const maxMb = Number.parseInt(getConfig().maxAttachmentSizeMb, 10);
        return Number.isFinite(maxMb) && maxMb > 0 ? maxMb * 1024 * 1024 : 0;
    }

    function toNumber(value) {
        const normalized = String(value ?? "").trim().replace(/,/g, "");
        const number = Number.parseFloat(normalized);
        return Number.isFinite(number) ? number : 0;
    }

    function formatNumber(value) {
        const number = toNumber(value);
        const negative = number < 0;
        const absolute = Math.abs(number);
        const parts = absolute.toFixed(3).split(".");
        const integerPart = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ",");
        const decimalPart = parts[1].replace(/0+$/, "");
        return `${negative ? "-" : ""}${integerPart}${decimalPart ? `.${decimalPart}` : ""}`;
    }

    function formatAmount(value) {
        return toNumber(value).toLocaleString("en-US", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
    }

    function normalizeDetailRow(row) {
        if (!row || typeof row !== "object") {
            return {
                tempKey: `tmp-${Date.now()}-${Math.random().toString(16).slice(2)}`,
                detailId: 0,
                itemId: 0,
                itemCode: "",
                itemName: "",
                unit: "",
                qtyFromM: 0,
                qtyPur: 0,
                unitPrice: 0,
                remark: "",
                supplierId: null,
                supplierText: "",
                mrRequestNo: "",
                mrDetailId: null
            };
        }

        return {
            tempKey: row.tempKey ?? row.TempKey ?? `tmp-${Date.now()}-${Math.random().toString(16).slice(2)}`,
            detailId: row.detailId ?? row.DetailId ?? 0,
            itemId: row.itemId ?? row.ItemId ?? 0,
            itemCode: row.itemCode ?? row.ItemCode ?? "",
            itemName: row.itemName ?? row.ItemName ?? "",
            unit: row.unit ?? row.Unit ?? "",
            qtyFromM: row.qtyFromM ?? row.QtyFromM ?? 0,
            qtyPur: row.qtyPur ?? row.QtyPur ?? 0,
            unitPrice: row.unitPrice ?? row.UnitPrice ?? 0,
            remark: row.remark ?? row.Remark ?? "",
            supplierId: row.supplierId ?? row.SupplierId ?? null,
            supplierText: row.supplierText ?? row.SupplierText ?? "",
            mrRequestNo: row.mrRequestNo ?? row.MrRequestNo ?? "",
            mrDetailId: row.mrDetailId ?? row.MrDetailId ?? null
        };
    }

    function initDetail() {
        const detailsJsonEl = document.getElementById("DetailsJson");
        const rowsContainer = document.getElementById("prqDetailRows");
        const emptyRow = document.getElementById("prqDetailEmptyRow");
        const selectedDetailIdEl = document.getElementById("SelectedDetailId");
        const totalAmountEl = document.getElementById("prqTotalAmount");
        const recordCountEl = document.getElementById("prqDetailRecordCount");
        const toMrButton = document.getElementById("btnToMr");
        const detailModal = document.getElementById("prqAddDetailModal");
        const addDetailBtn = document.getElementById("btnAddDetailConfirm");
        const addDetailError = document.getElementById("detailAddError");
        const detailAttachmentInput = document.getElementById("detailAttachmentUpload");
        const detailAttachmentError = document.getElementById("detailAttachmentError");
        const detailAttachmentUploadButton = document.getElementById("btnDetailAttachmentUpload");
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
        let selectedDetailId = selectedDetailIdEl ? Number.parseInt(selectedDetailIdEl.value || "0", 10) || 0 : 0;
        let selectedRowKey = "";

        try {
            details = detailsJsonEl.value ? JSON.parse(detailsJsonEl.value).map(normalizeDetailRow) : [];
        } catch {
            details = [];
        }

        const hideAddDetailError = () => {
            if (!addDetailError) return;
            addDetailError.style.display = "none";
            addDetailError.textContent = "";
        };

        const hideAttachmentError = () => {
            if (!detailAttachmentError) return;
            detailAttachmentError.textContent = "";
            detailAttachmentError.style.display = "none";
        };

        const showAttachmentError = (message) => {
            if (!detailAttachmentError) return;
            detailAttachmentError.textContent = message;
            detailAttachmentError.style.display = "block";
        };

        const validateAttachmentFile = () => {
            if (!detailAttachmentInput || !detailAttachmentInput.files || detailAttachmentInput.files.length === 0) {
                hideAttachmentError();
                return true;
            }

            const file = detailAttachmentInput.files[0];
            const extension = `.${String(file.name.split(".").pop() || "").toLowerCase()}`;
            const allowedExtensions = getAllowedExtensions();
            const maxSizeBytes = getMaxAttachmentSizeBytes();

            if (allowedExtensions.length > 0 && !allowedExtensions.includes(extension)) {
                showAttachmentError(`Attached file extension is invalid. Allowed: ${getConfig().allowedAttachmentExtensions}`);
                detailAttachmentInput.value = "";
                return false;
            }

            if (maxSizeBytes > 0 && file.size > maxSizeBytes) {
                showAttachmentError(`Attached file size must not exceed ${getConfig().maxAttachmentSizeMb} MB.`);
                detailAttachmentInput.value = "";
                return false;
            }

            hideAttachmentError();
            return true;
        };

        const showAddDetailError = (message) => {
            if (!addDetailError) {
                return;
            }
            addDetailError.textContent = message;
            addDetailError.style.display = "block";
        };

        const syncHiddenInput = () => {
            detailsJsonEl.value = JSON.stringify(details.map((detail) => ({
                detailId: detail.detailId || 0,
                itemId: detail.itemId,
                itemCode: detail.itemCode || "",
                itemName: detail.itemName || "",
                unit: detail.unit || "",
                qtyFromM: detail.qtyFromM,
                qtyPur: detail.qtyPur,
                unitPrice: detail.unitPrice,
                remark: detail.remark,
                supplierId: detail.supplierId,
                supplierText: detail.supplierText || "",
                mrRequestNo: detail.mrRequestNo || "",
                mrDetailId: detail.mrDetailId
            })));
        };

        const updateSummary = () => {
            const totalAmount = details.reduce((sum, detail) => sum + (toNumber(detail.qtyPur) * toNumber(detail.unitPrice)), 0);
            if (totalAmountEl) {
                totalAmountEl.value = formatAmount(totalAmount);
            }
            if (recordCountEl) {
                recordCountEl.textContent = String(details.length);
            }
        };

        const updateToMrButton = () => {
            if (!toMrButton) return;
            const selectedRow = details.find((detail) => Number(detail.detailId) === Number(selectedDetailId));
            const canMove = !!selectedRow && !!selectedRow.mrDetailId && getConfig().canMoveToMr;
            toMrButton.disabled = !canMove;
        };

        const getRowKey = (detail) => {
            if (Number(detail.detailId) > 0) {
                return `id:${detail.detailId}`;
            }

            return `tmp:${detail.tempKey}`;
        };

        const setSelectedDetail = (detail) => {
            selectedDetailId = detail && Number(detail.detailId) > 0 ? Number(detail.detailId) : 0;
            selectedRowKey = detail ? getRowKey(detail) : "";
            if (selectedDetailIdEl) {
                selectedDetailIdEl.value = selectedDetailId > 0 ? String(selectedDetailId) : "";
            }
            rowsContainer.querySelectorAll("tr[data-row='1']").forEach((row) => {
                const rowKey = row.getAttribute("data-row-key") || "";
                const isSelected = !!selectedRowKey && rowKey === selectedRowKey;
                row.classList.toggle("prq-detail-row-selected", isSelected);
                const selector = row.querySelector(".prq-detail-selector");
                if (selector) {
                    selector.checked = isSelected;
                }
            });
            updateToMrButton();
        };

        const renderRows = () => {
            rowsContainer.querySelectorAll("tr[data-row='1']").forEach((row) => row.remove());
            if (details.length === 0) {
                emptyRow.style.display = "";
                setSelectedDetail(null);
                syncHiddenInput();
                updateSummary();
                return;
            }

            emptyRow.style.display = "none";
            details.forEach((detail, index) => {
                const amount = toNumber(detail.qtyPur) * toNumber(detail.unitPrice);
                const removeButtonCell = getConfig().canSave
                    ? `<td class="text-center"><button type="button" class="btn btn-xs btn-outline-danger border" data-remove-index="${index}">X</button></td>`
                    : "";
                const rowKey = getRowKey(detail);
                const row = document.createElement("tr");
                row.dataset.row = "1";
                row.dataset.detailId = String(detail.detailId || 0);
                row.dataset.rowKey = rowKey;
                if (Number(detail.detailId) <= 0) {
                    row.classList.add("prq-detail-row-unsaved");
                }
                row.innerHTML = `
                    <td class="prq-center"><input type="checkbox" class="prq-detail-selector" ${selectedRowKey === rowKey ? "checked" : ""} /></td>
                    <td>${detail.itemCode || ""}</td>
                    <td>${detail.itemName || ""}</td>
                    <td class="prq-center">${detail.unit || ""}</td>
                    <td class="prq-center">${formatNumber(detail.qtyFromM)}</td>
                    <td class="prq-center">${formatNumber(detail.qtyPur)}</td>
                    <td class="prq-center">${formatNumber(detail.unitPrice)}</td>
                    <td class="prq-center">${formatAmount(amount)}</td>
                    <td class="prq-center">${detail.remark || ""}</td>
                    <td>${detail.supplierText || ""}</td>
                    ${removeButtonCell}`;
                rowsContainer.appendChild(row);
            });

            if (!details.some((detail) => getRowKey(detail) === selectedRowKey)) {
                setSelectedDetail(null);
            } else {
                const selectedDetail = details.find((detail) => getRowKey(detail) === selectedRowKey) || null;
                setSelectedDetail(selectedDetail);
            }
            syncHiddenInput();
            updateSummary();
        };

        if (!getConfig().canSave) {
            renderRows();
        }

        const updateAmount = () => {
            if (!amountInput || !qtyPur || !unitPrice) return;
            amountInput.value = formatAmount(toNumber(qtyPur.value) * toNumber(unitPrice.value));
        };

        const isDuplicateDetail = (itemId, supplierId) => {
            return details.some((detail) =>
                Number(detail.itemId) === Number(itemId) &&
                Number(detail.supplierId || 0) === Number(supplierId || 0) &&
                !detail.mrDetailId
            );
        };

        const normalizeInputValue = (input) => {
            if (!input) return;
            input.value = formatNumber(input.value);
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
            hideAddDetailError();
        };

        rowsContainer.addEventListener("click", (event) => {
            const removeButton = event.target.closest("button[data-remove-index]");
            if (removeButton) {
                const index = Number.parseInt(removeButton.getAttribute("data-remove-index") || "-1", 10);
                if (Number.isInteger(index) && index >= 0 && index < details.length) {
                    const removed = details[index];
                    details.splice(index, 1);
                    if (getRowKey(removed) === selectedRowKey) {
                        selectedDetailId = 0;
                        selectedRowKey = "";
                    }
                    renderRows();
                }
                return;
            }

            const row = event.target.closest("tr[data-row='1']");
            if (!row) return;
            const rowKey = row.getAttribute("data-row-key") || "";
            if (rowKey && rowKey === selectedRowKey) {
                setSelectedDetail(null);
                return;
            }
            const detail = details.find((item) => getRowKey(item) === rowKey) || null;
            if (detail) {
                setSelectedDetail(detail);
            }
        });

        itemSelect?.addEventListener("change", () => {
            hideAddDetailError();
            const selected = itemSelect.selectedOptions[0];
            if (unitInput) {
                unitInput.value = selected ? (selected.dataset.unit || "") : "";
            }
        });

        qtyPur?.addEventListener("input", () => {
            hideAddDetailError();
            updateAmount();
        });
        unitPrice?.addEventListener("input", updateAmount);
        qtyFromM?.addEventListener("blur", () => normalizeInputValue(qtyFromM));
        qtyPur?.addEventListener("blur", () => normalizeInputValue(qtyPur));
        unitPrice?.addEventListener("blur", () => normalizeInputValue(unitPrice));
        updateAmount();

        addDetailBtn?.addEventListener("click", () => {
            if (!itemSelect || !qtyFromM || !qtyPur || !unitPrice || !remarkInput) return;
            hideAddDetailError();

            const selectedItem = itemSelect.selectedOptions[0];
            if (!selectedItem || !selectedItem.value) {
                showAddDetailError("Cannot add detail because no item has been selected.");
                itemSelect.focus();
                return;
            }

            const qtyFromMValue = toNumber(qtyFromM.value);
            const qtyPurValue = toNumber(qtyPur.value);
            const unitPriceValue = toNumber(unitPrice.value);

            if (qtyFromMValue < 0) {
                showAddDetailError("Cannot add detail because QtyFromM must not be less than 0.");
                qtyFromM.focus();
                return;
            }

            if (qtyPurValue <= 0) {
                showAddDetailError("Cannot add detail because QtyPur must be greater than 0.");
                qtyPur.focus();
                return;
            }

            if (unitPriceValue < 0) {
                showAddDetailError("Cannot add detail because U.Price must not be less than 0.");
                unitPrice.focus();
                return;
            }

            const supplierOption = supplierSelect?.selectedOptions[0];
            const supplierId = supplierOption && supplierOption.value ? Number.parseInt(supplierOption.value, 10) : null;
            if (isDuplicateDetail(selectedItem.value, supplierId)) {
                showAddDetailError("Cannot add detail because this item already exists in the unsaved detail list.");
                itemSelect.focus();
                return;
            }

            details.push({
                tempKey: `tmp-${Date.now()}-${Math.random().toString(16).slice(2)}`,
                detailId: 0,
                itemId: Number.parseInt(selectedItem.value, 10),
                itemCode: selectedItem.dataset.code || "",
                itemName: selectedItem.dataset.name || "",
                unit: selectedItem.dataset.unit || "",
                qtyFromM: qtyFromMValue,
                qtyPur: qtyPurValue,
                unitPrice: unitPriceValue,
                remark: remarkInput.value.trim(),
                supplierId: supplierId,
                supplierText: supplierOption && supplierOption.value ? supplierOption.text : "",
                mrRequestNo: "",
                mrDetailId: null
            });
            renderRows();
            resetDetailFields();
            if (window.jQuery && detailModal) {
                window.jQuery(detailModal).modal("hide");
            }
        });

        detailAttachmentInput?.addEventListener("change", validateAttachmentFile);
        detailAttachmentUploadButton?.addEventListener("click", (event) => {
            if (!validateAttachmentFile()) {
                event.preventDefault();
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
