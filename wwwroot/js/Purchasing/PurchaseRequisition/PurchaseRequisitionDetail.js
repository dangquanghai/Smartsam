(() => {
    "use strict";

    function getConfig() {
        return window.purchaseRequisitionDetailPage || { canSave: false, canMoveToMr: false, canSelectDetail: false };
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

    function formatSummaryAmount(value) {
        const number = toNumber(value);
        return number.toLocaleString("en-US", {
            minimumFractionDigits: 0,
            maximumFractionDigits: 3
        });
    }

    function sanitizeNonNegativeDecimal(value) {
        let normalized = String(value ?? "")
            .replace(/,/g, "")
            .replace(/[^0-9.]/g, "");

        const firstDotIndex = normalized.indexOf(".");
        if (firstDotIndex >= 0) {
            normalized = normalized.slice(0, firstDotIndex + 1) + normalized.slice(firstDotIndex + 1).replace(/\./g, "");
        }

        return normalized;
    }

    function clampNonNegativeDecimalInput(input, maxValue) {
        if (!input) return 0;

        let normalizedValue = toNumber(input.value);
        const normalizedMax = toNumber(maxValue);

        if (normalizedValue < 0) {
            normalizedValue = 0;
        }

        if (normalizedMax > 0 && normalizedValue > normalizedMax) {
            normalizedValue = normalizedMax;
        }

        input.value = formatNumber(normalizedValue);
        return normalizedValue;
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function buildCollapsibleText(text, previewLength) {
        const safeText = String(text ?? "").trim();
        if (!safeText) {
            return "";
        }

        if (safeText.length <= previewLength) {
            return `<div class="prq-description-cell prq-add-at-text-cell"><span class="prq-description-full">${escapeHtml(safeText)}</span></div>`;
        }

        const preview = `${escapeHtml(safeText.slice(0, previewLength))}...`;
        const full = escapeHtml(safeText);
        return `
            <div class="prq-description-cell prq-add-at-text-cell">
                <span class="prq-description-preview">${preview}</span>
                <button type="button" class="prq-read-more">Read more</button>
                <span class="prq-description-full d-none">${full}</span>
            </div>`;
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
        const totalAmountCurrencyEl = document.getElementById("prqTotalAmountCurrency");
        const currencySelectEl = document.getElementById("prqCurrencySelect");
        const recordCountEl = document.getElementById("prqDetailRecordCount");
        const toMrButton = document.getElementById("btnToMr");
        const detailModal = document.getElementById("prqAddDetailModal");
        const addDetailBtn = document.getElementById("btnAddDetailConfirm");
        const addDetailError = document.getElementById("detailAddError");
        const detailAttachmentInput = document.getElementById("detailAttachmentUpload");
        const detailAttachmentError = document.getElementById("detailAttachmentError");
        const detailAttachmentUploadButton = document.getElementById("btnDetailAttachmentUpload");
        const addDetailRows = Array.from(document.querySelectorAll("tr[data-add-detail-row='1']"));
        if (!detailsJsonEl || !rowsContainer || !emptyRow) return;

        let details = [];
        let selectedDetailId = selectedDetailIdEl ? Number.parseInt(selectedDetailIdEl.value || "0", 10) || 0 : 0;
        let selectedRowKey = "";

        try {
            details = detailsJsonEl.value ? JSON.parse(detailsJsonEl.value).map(normalizeDetailRow) : [];
        } catch {
            details = [];
        }

        const initialAllocatedQtyByMrDetail = {};
        details.forEach((detail) => {
            const mrDetailId = Number(detail.mrDetailId || 0);
            if (mrDetailId > 0) {
                initialAllocatedQtyByMrDetail[mrDetailId] = (initialAllocatedQtyByMrDetail[mrDetailId] || 0) + toNumber(detail.qtyPur);
            }
        });

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
                if ("value" in totalAmountEl) {
                    totalAmountEl.value = formatSummaryAmount(totalAmount);
                } else {
                    totalAmountEl.textContent = formatSummaryAmount(totalAmount);
                }
            }
            if (totalAmountCurrencyEl && currencySelectEl) {
                totalAmountCurrencyEl.textContent = currencySelectEl.options[currencySelectEl.selectedIndex]?.text || "";
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
                selectedDetailIdEl.value = selectedDetailId > 0 ? String(selectedDetailId) : "0";
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
                updateAddDetailAvailability();
                return;
            }

            emptyRow.style.display = "none";
            details.forEach((detail) => {
                const amount = toNumber(detail.qtyPur) * toNumber(detail.unitPrice);
                const rowKey = getRowKey(detail);
                const row = document.createElement("tr");
                row.dataset.row = "1";
                row.dataset.detailId = String(detail.detailId || 0);
                row.dataset.rowKey = rowKey;
                if (Number(detail.detailId) <= 0) {
                    row.classList.add("prq-detail-row-unsaved");
                }
                const selectorCell = Number(detail.detailId) <= 0
                    ? `<button type="button" class="btn btn-xs btn-outline-danger border prq-unsaved-remove" data-remove-temp-key="${escapeHtml(detail.tempKey || "")}" title="Remove"><i class="fas fa-trash"></i></button>`
                    : (getConfig().canSelectDetail ? `<input type="checkbox" class="prq-detail-selector" ${selectedRowKey === rowKey ? "checked" : ""} />` : "");
                row.innerHTML = `
                    <td class="prq-center">${selectorCell}</td>
                    <td>${detail.itemCode || ""}</td>
                    <td>${detail.itemName || ""}</td>
                    <td class="prq-center">${detail.unit || ""}</td>
                    <td class="prq-center">${formatNumber(detail.qtyFromM)}</td>
                    <td class="prq-center">${formatNumber(detail.qtyPur)}</td>
                    <td class="prq-center">${formatNumber(detail.unitPrice)}</td>
                    <td class="prq-center">${formatNumber(amount)}</td>
                    <td class="prq-center">${detail.remark || ""}</td>
                    <td>${detail.supplierText || ""}</td>`;
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
            updateAddDetailAvailability();
        };

        if (!getConfig().canSave) {
            renderRows();
        }

        const isDuplicateDetail = (itemId, supplierId, mrDetailId) => {
            return details.some((detail) =>
                (
                    Number(mrDetailId || 0) > 0
                        ? Number(detail.mrDetailId || 0) === Number(mrDetailId || 0)
                        : (
                            Number(detail.itemId) === Number(itemId) &&
                            Number(detail.supplierId || 0) === Number(supplierId || 0) &&
                            !detail.mrDetailId
                        )
                )
            );
        };

        const getAllocatedQtyByMrDetail = (mrDetailId) => {
            const targetMrDetailId = Number(mrDetailId || 0);
            if (targetMrDetailId <= 0) {
                return 0;
            }

            return details
                .filter((detail) => Number(detail.mrDetailId || 0) === targetMrDetailId)
                .reduce((sum, detail) => sum + toNumber(detail.qtyPur), 0);
        };

        const getPendingUnsavedQtyByMrDetail = (mrDetailId) => {
            const targetMrDetailId = Number(mrDetailId || 0);
            if (targetMrDetailId <= 0) {
                return 0;
            }

            return details
                .filter((detail) => Number(detail.detailId || 0) <= 0 && Number(detail.mrDetailId || 0) === targetMrDetailId)
                .reduce((sum, detail) => sum + toNumber(detail.qtyPur), 0);
        };

        const findPendingUnsavedDetail = (itemId, mrDetailId) => {
            const targetMrDetailId = Number(mrDetailId || 0);
            if (targetMrDetailId > 0) {
                return details.find((detail) =>
                    Number(detail.detailId || 0) <= 0 &&
                    Number(detail.mrDetailId || 0) === targetMrDetailId
                ) || null;
            }

            return details.find((detail) =>
                Number(detail.detailId || 0) <= 0 &&
                Number(detail.itemId || 0) === Number(itemId || 0) &&
                !Number(detail.mrDetailId || 0)
            ) || null;
        };

        const updateAddDetailAvailability = () => {
            addDetailRows.forEach((row) => {
                const mrDetailId = Number.parseInt(row.getAttribute("data-mr-detail-id") || "0", 10) || 0;
                const baseBuy = toNumber(row.getAttribute("data-base-buy") || row.getAttribute("data-buy") || "0");
                const pendingQty = getPendingUnsavedQtyByMrDetail(mrDetailId);
                const remainingBuy = Math.max(0, baseBuy - pendingQty);
                const checkbox = row.querySelector(".prq-add-detail-check");
                const subQtyInput = row.querySelector(".prq-add-detail-subqty");
                const buyCell = row.querySelector(".prq-add-detail-buy");

                row.setAttribute("data-current-buy", String(remainingBuy));

                if (buyCell) {
                    buyCell.textContent = formatNumber(remainingBuy);
                }

                if (subQtyInput) {
                    const currentValue = toNumber(subQtyInput.value);
                    const nextValue = remainingBuy <= 0
                        ? 0
                        : (currentValue > 0 && currentValue <= remainingBuy ? currentValue : remainingBuy);
                    subQtyInput.value = formatNumber(nextValue);
                    subQtyInput.disabled = remainingBuy <= 0;
                }

                if (checkbox) {
                    if (remainingBuy <= 0) {
                        checkbox.checked = false;
                    }
                    checkbox.disabled = remainingBuy <= 0;
                }

                row.classList.toggle("d-none", remainingBuy <= 0);
                if (remainingBuy <= 0) {
                    row.classList.remove("prq-detail-row-selected");
                }
            });
        };

        const normalizeInputValue = (input) => {
            if (!input) return;
            input.value = formatNumber(input.value);
        };

        const resetDetailFields = () => {
            addDetailRows.forEach((row) => {
                const checkbox = row.querySelector(".prq-add-detail-check");
                const subQtyInput = row.querySelector(".prq-add-detail-subqty");
                row.classList.remove("prq-detail-row-selected");
                if (checkbox) {
                    checkbox.checked = false;
                }
                if (subQtyInput) {
                    const currentBuy = row.getAttribute("data-current-buy") || row.getAttribute("data-base-buy") || row.getAttribute("data-buy") || "0";
                    subQtyInput.value = formatNumber(currentBuy);
                }
            });
            hideAddDetailError();
        };

        rowsContainer.addEventListener("click", (event) => {
            const removeUnsavedButton = event.target.closest("button[data-remove-temp-key]");
            if (removeUnsavedButton) {
                const tempKey = removeUnsavedButton.getAttribute("data-remove-temp-key") || "";
                const removeIndex = details.findIndex((detail) => String(detail.tempKey || "") === tempKey && Number(detail.detailId || 0) <= 0);
                if (removeIndex >= 0) {
                    details.splice(removeIndex, 1);
                    if (!details.some((detail) => getRowKey(detail) === selectedRowKey)) {
                        setSelectedDetail(null);
                    }
                    renderRows();
                }
                return;
            }

            if (!getConfig().canSelectDetail) {
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

        addDetailRows.forEach((row) => {
            const checkbox = row.querySelector(".prq-add-detail-check");
            const subQtyInput = row.querySelector(".prq-add-detail-subqty");
            const specificationCell = row.querySelector(".prq-add-detail-specification");
            const noteCell = row.querySelector(".prq-add-detail-note");
            if (specificationCell) {
                specificationCell.innerHTML = buildCollapsibleText(row.getAttribute("data-specification") || "", 36);
            }
            if (noteCell) {
                noteCell.innerHTML = buildCollapsibleText(row.getAttribute("data-note") || "", 34);
            }
            checkbox?.addEventListener("change", () => {
                row.classList.toggle("prq-detail-row-selected", checkbox.checked);
                hideAddDetailError();
            });
            subQtyInput?.addEventListener("input", () => {
                const sanitizedValue = sanitizeNonNegativeDecimal(subQtyInput.value);
                if (subQtyInput.value !== sanitizedValue) {
                    subQtyInput.value = sanitizedValue;
                }

                clampNonNegativeDecimalInput(
                    subQtyInput,
                    row.getAttribute("data-current-buy") || row.getAttribute("data-base-buy") || row.getAttribute("data-buy") || "0"
                );
                hideAddDetailError();
            });
            subQtyInput?.addEventListener("blur", () => {
                clampNonNegativeDecimalInput(
                    subQtyInput,
                    row.getAttribute("data-current-buy") || row.getAttribute("data-base-buy") || row.getAttribute("data-buy") || "0"
                );
                normalizeInputValue(subQtyInput);
            });
        });

        addDetailBtn?.addEventListener("click", () => {
            hideAddDetailError();

            const selectedRows = addDetailRows.filter((row) => {
                const checkbox = row.querySelector(".prq-add-detail-check");
                return !!checkbox && checkbox.checked;
            });

            if (selectedRows.length === 0) {
                showAddDetailError("Cannot add detail because no item has been selected.");
                return;
            }

            const newDetails = [];
            for (const row of selectedRows) {
                const itemId = Number.parseInt(row.getAttribute("data-item-id") || "0", 10);
                const itemCode = row.getAttribute("data-item-code") || "";
                const itemName = row.getAttribute("data-item-name") || "";
                const requestNo = row.getAttribute("data-request-no") || "";
                const buy = toNumber(row.getAttribute("data-current-buy") || row.getAttribute("data-base-buy") || row.getAttribute("data-buy") || "0");
                const baseBuy = toNumber(row.getAttribute("data-base-buy") || row.getAttribute("data-buy") || "0");
                const unit = row.getAttribute("data-unit") || "";
                const unitPrice = toNumber(row.getAttribute("data-unit-price") || "0");
                const specification = row.getAttribute("data-specification") || "";
                const mrDetailId = Number.parseInt(row.getAttribute("data-mr-detail-id") || "0", 10) || 0;
                const subQtyInput = row.querySelector(".prq-add-detail-subqty");
                const qtyPurValue = toNumber(subQtyInput ? subQtyInput.value : "0");

                if (qtyPurValue <= 0) {
                    showAddDetailError(`Cannot add detail because SugBuy for item ${itemCode} must be greater than 0.`);
                    subQtyInput?.focus();
                    return;
                }

                if (qtyPurValue > buy) {
                    showAddDetailError(`Cannot add detail because SugBuy for item ${itemCode} cannot be greater than BUY.`);
                    subQtyInput?.focus();
                    return;
                }

                const initialAllocatedQty = Number(initialAllocatedQtyByMrDetail[mrDetailId] || 0);
                const currentAllocatedQty = getAllocatedQtyByMrDetail(mrDetailId);
                const proposedAllocatedQty = currentAllocatedQty + qtyPurValue;
                const maxAllocatedQty = initialAllocatedQty + baseBuy;

                if (mrDetailId > 0 && proposedAllocatedQty > maxAllocatedQty) {
                    showAddDetailError(`Cannot add detail because total SugBuy for item ${itemCode} exceeds the remaining BUY of this MR line.`);
                    subQtyInput?.focus();
                    return;
                }

                const pendingUnsavedDetail = findPendingUnsavedDetail(itemId, mrDetailId);
                if (pendingUnsavedDetail) {
                    pendingUnsavedDetail.qtyPur = toNumber(pendingUnsavedDetail.qtyPur) + qtyPurValue;
                    pendingUnsavedDetail.qtyFromM = mrDetailId > 0 ? initialAllocatedQty + baseBuy : Math.max(toNumber(pendingUnsavedDetail.qtyFromM), baseBuy);
                    pendingUnsavedDetail.unitPrice = unitPrice;
                    pendingUnsavedDetail.remark = specification;
                    pendingUnsavedDetail.mrRequestNo = requestNo;
                    if (mrDetailId > 0) {
                        pendingUnsavedDetail.mrDetailId = mrDetailId;
                    }
                    continue;
                }

                newDetails.push({
                    tempKey: `tmp-${Date.now()}-${Math.random().toString(16).slice(2)}`,
                    detailId: 0,
                    itemId: itemId,
                    itemCode: itemCode,
                    itemName: itemName,
                    unit: unit,
                    qtyFromM: mrDetailId > 0 ? initialAllocatedQty + baseBuy : baseBuy,
                    qtyPur: qtyPurValue,
                    unitPrice: unitPrice,
                    remark: specification,
                    supplierId: null,
                    supplierText: "",
                    mrRequestNo: requestNo,
                    mrDetailId: mrDetailId > 0 ? mrDetailId : null
                });
            }

            details.push(...newDetails);
            setSelectedDetail(null);
            renderRows();
            if (toMrButton) {
                toMrButton.disabled = true;
            }
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

        currencySelectEl?.addEventListener("change", updateSummary);

        document.addEventListener("click", (event) => {
            const readMoreButton = event.target.closest(".prq-read-more");
            if (!readMoreButton) return;

            const container = readMoreButton.closest(".prq-description-cell");
            if (!container) return;

            const previewEl = container.querySelector(".prq-description-preview");
            const fullEl = container.querySelector(".prq-description-full");
            previewEl?.classList.add("d-none");
            fullEl?.classList.remove("d-none");
            readMoreButton.remove();
        });

        renderRows();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initDetail);
    } else {
        initDetail();
    }
})();
