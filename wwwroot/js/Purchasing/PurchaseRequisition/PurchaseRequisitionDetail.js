(() => {
    "use strict";

    function getConfig() {
        return window.purchaseRequisitionDetailPage || { canSave: false, canEditExistingDetailFields: false, canMoveToMr: false, canSelectDetail: false, isViewMode: false };
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

    function clampNonNegativeDecimalInput(input) {
        if (!input) return 0;

        let normalizedValue = toNumber(input.value);

        if (normalizedValue < 0) {
            normalizedValue = 0;
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
            return `<div class="prq-description-cell prq-add-at-text-cell"><span class="prq-description-full vni-font">${escapeHtml(safeText)}</span></div>`;
        }

        const preview = `${escapeHtml(safeText.slice(0, previewLength))}...`;
        const full = escapeHtml(safeText);
        return `
            <div class="prq-description-cell prq-add-at-text-cell">
                <span class="prq-description-preview vni-font">${preview}</span>
                <button type="button" class="prq-read-more">Read more</button>
                <span class="prq-description-full d-none vni-font">${full}</span>
            </div>`;
    }

    function buildSingleLineEllipsisText(text, extraClass = "") {
        const safeText = String(text ?? "").trim();
        if (!safeText) {
            return "";
        }

        const className = extraClass ? ` ${extraClass}` : "";
        return `<span class="prq-add-detail-ellipsis${className}" title="${escapeHtml(safeText)}">${escapeHtml(safeText)}</span>`;
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
                amount: 0,
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
            amount: row.amount ?? row.Amount ?? ((row.qtyPur ?? row.QtyPur ?? 0) * (row.unitPrice ?? row.UnitPrice ?? 0)),
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
        const detailAttachmentDeleteButton = document.getElementById("btnDetailAttachmentDelete");
        const attachmentSelectors = Array.from(document.querySelectorAll(".prq-attachment-selector"));
        const confirmActionModal = document.getElementById("prqConfirmActionModal");
        const confirmActionMessage = document.getElementById("prqConfirmActionMessage");
        const confirmActionYesButton = document.getElementById("btnPrqConfirmActionYes");
        const confirmActionButtons = Array.from(document.querySelectorAll(".prq-confirm-action-btn"));
        const addDetailRows = Array.from(document.querySelectorAll("tr[data-add-detail-row='1']"));
        const viewDetailModal = document.getElementById("prqViewDetailModal");
        const openViewDetailButton = document.getElementById("btnOpenViewDetailModal");
        const viewDetailRequestNo = document.getElementById("prqViewDetailRequestNo");
        const viewDetailDescription = document.getElementById("prqViewDetailDescription");
        const viewDetailRecQtyOperator = document.getElementById("prqViewDetailRecQtyOperator");
        const viewDetailRecQty = document.getElementById("prqViewDetailRecQty");
        const viewDetailItemCode = document.getElementById("prqViewDetailItemCode");
        const viewDetailUseDateRange = document.getElementById("prqViewDetailUseDateRange");
        const viewDetailFromDate = document.getElementById("prqViewDetailFromDate");
        const viewDetailToDate = document.getElementById("prqViewDetailToDate");
        const viewDetailSearchButton = document.getElementById("btnPrqViewDetailSearch");
        const viewDetailRows = document.getElementById("prqViewDetailRows");
        const viewDetailEmptyRow = document.getElementById("prqViewDetailEmptyRow");
        const viewDetailPaginationInfo = document.getElementById("prqViewDetailPaginationInfo");
        const viewDetailPagination = document.getElementById("prqViewDetailPagination");
        const viewDetailPageSize = document.getElementById("prqViewDetailPageSize");
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

        let pendingConfirmedFormAction = "";

        const showAttachmentError = (message) => {
            if (!detailAttachmentError) return;
            detailAttachmentError.textContent = message;
            detailAttachmentError.style.display = "block";
        };

        const updateAttachmentDeleteButton = () => {
            if (!detailAttachmentDeleteButton) return;
            detailAttachmentDeleteButton.disabled = !attachmentSelectors.some((checkbox) => checkbox.checked);
        };

        const viewDetailState = {
            pageNumber: 1,
            pageSize: viewDetailPageSize ? Number.parseInt(viewDetailPageSize.value || "10", 10) || 10 : 10,
            totalPages: 1,
            hasLoaded: false,
            loading: false
        };

        const validateAttachmentFile = () => {
            if (!detailAttachmentInput || !detailAttachmentInput.files || detailAttachmentInput.files.length === 0) {
                hideAttachmentError();
                return true;
            }

            const allowedExtensions = getAllowedExtensions();
            const maxSizeBytes = getMaxAttachmentSizeBytes();
            const files = Array.from(detailAttachmentInput.files);

            for (const file of files) {
                const extension = `.${String(file.name.split(".").pop() || "").toLowerCase()}`;

                if (allowedExtensions.length > 0 && !allowedExtensions.includes(extension)) {
                    showAttachmentError(`Attached file extension is invalid for '${file.name}'. Allowed: ${getConfig().allowedAttachmentExtensions}`);
                    detailAttachmentInput.value = "";
                    return false;
                }

                if (maxSizeBytes > 0 && file.size > maxSizeBytes) {
                    showAttachmentError(`Attached file '${file.name}' size must not exceed ${getConfig().maxAttachmentSizeMb} MB.`);
                    detailAttachmentInput.value = "";
                    return false;
                }
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

        const getViewDetailFilter = () => ({
            requestNo: viewDetailRequestNo?.value?.trim() || "",
            description: viewDetailDescription?.value?.trim() || "",
            recQtyOperator: viewDetailRecQtyOperator?.value || "=",
            recQty: viewDetailRecQty?.value?.trim() || "",
            itemCode: viewDetailItemCode?.value?.trim() || "",
            useDateRange: !!viewDetailUseDateRange?.checked,
            fromDate: viewDetailFromDate?.value || "",
            toDate: viewDetailToDate?.value || "",
            pageNumber: viewDetailState.pageNumber,
            pageSize: viewDetailState.pageSize
        });

        const buildViewDetailPages = (currentPage, totalPages) => {
            const items = [];
            if (totalPages <= 7) {
                for (let page = 1; page <= totalPages; page += 1) {
                    items.push(page);
                }
                return items;
            }

            items.push(1);
            if (currentPage > 3) {
                items.push(null);
            }

            const start = Math.max(2, currentPage - 1);
            const end = Math.min(totalPages - 1, currentPage + 1);
            for (let page = start; page <= end; page += 1) {
                items.push(page);
            }

            if (currentPage < totalPages - 2) {
                items.push(null);
            }
            items.push(totalPages);
            return items;
        };

        const renderViewDetailRows = (response) => {
            if (!viewDetailRows || !viewDetailEmptyRow || !viewDetailPaginationInfo || !viewDetailPagination) {
                return;
            }

            viewDetailRows.querySelectorAll("tr[data-view-detail-row='1']").forEach((row) => row.remove());

            const rows = Array.isArray(response?.rows) ? response.rows : [];
            const totalRecords = Number(response?.totalRecords || 0);
            const totalPages = Math.max(1, Number(response?.totalPages || 1));
            const currentPage = Math.max(1, Number(response?.pageNumber || 1));
            const pageSize = Math.max(1, Number(response?.pageSize || viewDetailState.pageSize || 10));
            viewDetailState.totalPages = totalPages;
            viewDetailState.pageNumber = currentPage;
            viewDetailState.pageSize = pageSize;
            if (viewDetailPageSize) {
                viewDetailPageSize.value = String(pageSize);
            }

            if (rows.length === 0) {
                viewDetailEmptyRow.style.display = "";
            } else {
                viewDetailEmptyRow.style.display = "none";
                rows.forEach((row) => {
                    const tr = document.createElement("tr");
                    tr.setAttribute("data-view-detail-row", "1");
                    tr.innerHTML = `
                        <td>${escapeHtml(row.requestNo || "")}</td>
                        <td>${escapeHtml(row.requestDateText || "")}</td>
                        <td><span class="vni-font">${escapeHtml(row.description || "")}</span></td>
                        <td><span class="tcvn3-font">${escapeHtml(row.itemCode || "")}</span> / <span class="tcvn3-font">${escapeHtml(row.itemName || "")}</span></td>
                        <td class="prq-center">${formatNumber(row.prQty || 0)}</td>
                        <td class="prq-center">${formatNumber(row.recQty || 0)}</td>`;
                    viewDetailRows.appendChild(tr);
                });
            }

            if (totalRecords <= 0) {
                viewDetailPaginationInfo.innerHTML = "<small>No records</small>";
            } else {
                const pageStart = ((currentPage - 1) * pageSize) + 1;
                const pageEnd = Math.min(currentPage * pageSize, totalRecords);
                viewDetailPaginationInfo.innerHTML = `<small>Showing ${pageStart} to ${pageEnd} of ${totalRecords} entries</small>`;
            }

            viewDetailPagination.innerHTML = "";
            const appendPageItem = (label, page, disabled, active) => {
                const li = document.createElement("li");
                li.className = `page-item${disabled ? " disabled" : ""}${active ? " active" : ""}`;
                const link = document.createElement(disabled ? "span" : "a");
                link.className = "page-link";
                link.textContent = label;
                if (!disabled) {
                    link.href = "#";
                    link.dataset.page = String(page);
                }
                li.appendChild(link);
                viewDetailPagination.appendChild(li);
            };

            appendPageItem("Prev", currentPage - 1, currentPage <= 1, false);
            buildViewDetailPages(currentPage, totalPages).forEach((page) => {
                if (page == null) {
                    const li = document.createElement("li");
                    li.className = "page-item disabled";
                    li.innerHTML = "<span class='page-link'>...</span>";
                    viewDetailPagination.appendChild(li);
                    return;
                }

                appendPageItem(String(page), page, false, page === currentPage);
            });
            appendPageItem("Next", currentPage + 1, currentPage >= totalPages, false);
        };

        const loadViewDetailRows = async (resetPage) => {
            if (!getConfig().canOpenViewDetailDialog || !getConfig().viewDetailEndpoint || !viewDetailRows) {
                return;
            }

            if (resetPage) {
                viewDetailState.pageNumber = 1;
            }

            const filter = getViewDetailFilter();
            const params = new URLSearchParams();
            if (filter.requestNo) params.set("RequestNo", filter.requestNo);
            if (filter.description) params.set("Description", filter.description);
            if (filter.itemCode) params.set("ItemCode", filter.itemCode);
            if (filter.recQty) {
                params.set("RecQtyOperator", filter.recQtyOperator);
                params.set("RecQty", filter.recQty);
            }
            if (filter.useDateRange) {
                params.set("UseDateRange", "true");
                if (filter.fromDate) params.set("FromDate", filter.fromDate);
                if (filter.toDate) params.set("ToDate", filter.toDate);
            }
            params.set("PageNumber", String(filter.pageNumber));
            params.set("PageSize", String(filter.pageSize));

            const separator = getConfig().viewDetailEndpoint.includes("?") ? "&" : "?";
            const url = `${getConfig().viewDetailEndpoint}${separator}${params.toString()}`;
            viewDetailState.loading = true;

            try {
                const response = await fetch(url, {
                    method: "GET",
                    headers: { "X-Requested-With": "XMLHttpRequest" }
                });

                if (!response.ok) {
                    throw new Error("Cannot load purchase requisition details.");
                }

                const payload = await response.json();
                renderViewDetailRows(payload);
                viewDetailState.hasLoaded = true;
            } catch (error) {
                renderViewDetailRows({ rows: [], totalRecords: 0, totalPages: 1, pageNumber: 1, pageSize: viewDetailState.pageSize });
                if (viewDetailPaginationInfo) {
                    viewDetailPaginationInfo.innerHTML = `<small>${escapeHtml(error?.message || "Cannot load purchase requisition details.")}</small>`;
                }
            } finally {
                viewDetailState.loading = false;
            }
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
                amount: detail.amount,
                remark: detail.remark,
                supplierId: detail.supplierId,
                supplierText: detail.supplierText || "",
                mrRequestNo: detail.mrRequestNo || "",
                mrDetailId: detail.mrDetailId
            })));
        };

        const updateSummary = () => {
            const totalAmount = details.reduce((sum, detail) => sum + toNumber(detail.amount), 0);
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

        const syncDescriptionFromDetailRequestNos = () => {
            const descriptionInput = document.getElementById("Requisition_Description");
            if (!(descriptionInput instanceof HTMLTextAreaElement) || getConfig().isViewMode) {
                return;
            }

            const uniqueRequestNos = [];
            details.forEach((detail) => {
                const requestNo = String(detail.mrRequestNo || "").trim();
                if (!requestNo) {
                    return;
                }

                if (!uniqueRequestNos.includes(requestNo)) {
                    uniqueRequestNos.push(requestNo);
                }
            });

            descriptionInput.value = uniqueRequestNos.length > 0
                ? `MR ${uniqueRequestNos.join(", ")}`
                : "";
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
            details.forEach((detail, index) => {
                const amount = toNumber(detail.amount);
                const rowKey = getRowKey(detail);
                const row = document.createElement("tr");
                row.dataset.row = "1";
                row.dataset.detailId = String(detail.detailId || 0);
                row.dataset.rowKey = rowKey;
                if (Number(detail.detailId) <= 0) {
                    row.classList.add("prq-detail-row-unsaved");
                }
                const selectorCell = getConfig().isViewMode
                    ? String(index + 1)
                    : Number(detail.detailId) <= 0
                        ? `<button type="button" class="btn btn-xs btn-outline-danger border prq-unsaved-remove" data-remove-temp-key="${escapeHtml(detail.tempKey || "")}" title="Remove"><i class="fas fa-trash"></i></button>`
                        : (getConfig().canSelectDetail ? `<input type="checkbox" class="prq-detail-selector" ${selectedRowKey === rowKey ? "checked" : ""} />` : "");
                row.innerHTML = `
                    <td class="prq-center">${selectorCell}</td>
                    <td><span class="vni-font">${escapeHtml(detail.mrRequestNo || "")}</span></td>
                    <td title="${escapeHtml(detail.itemCode || "")}">${escapeHtml(detail.itemCode || "")}</td>
                    <td class="prq-detail-col-item-name" title="${escapeHtml(detail.itemName || "")}">
                        <span class="tcvn3-font">${escapeHtml(detail.itemName || "")}</span>
                    </td>
                    <td class="prq-center">${detail.unit || ""}</td>
                    <td class="prq-center">${formatNumber(detail.qtyFromM)}</td>
                    <td class="prq-center">${formatNumber(detail.qtyPur)}</td>
                    <td class="prq-center">${getConfig().canEditExistingDetailFields
                        ? `<input type="text" inputmode="decimal" class="form-control form-control-sm prq-detail-edit-price" value="${formatNumber(detail.unitPrice)}" />`
                        : formatNumber(detail.unitPrice)}</td>
                    <td class="prq-center prq-detail-col-amount">${getConfig().canEditExistingDetailFields
                        ? `<input type="text" inputmode="decimal" class="form-control form-control-sm prq-detail-edit-amount" value="${formatNumber(amount)}" />`
                        : formatNumber(amount)}</td>
                    <td class="prq-center prq-detail-col-remark">${getConfig().canEditExistingDetailFields
                        ? `<input type="text" class="form-control form-control-sm prq-detail-edit-remark vni-font" value="${escapeHtml(detail.remark || "")}" />`
                        : `<span class="vni-font">${escapeHtml(detail.remark || "")}</span>`}</td>
                    <td><span class="vni-font">${escapeHtml(detail.supplierText || "")}</span></td>`;
                rowsContainer.appendChild(row);
            });

            if (!details.some((detail) => getRowKey(detail) === selectedRowKey)) {
                setSelectedDetail(null);
            } else {
                const selectedDetail = details.find((detail) => getRowKey(detail) === selectedRowKey) || null;
                setSelectedDetail(selectedDetail);
            }
            syncDescriptionFromDetailRequestNos();
            syncHiddenInput();
            updateSummary();
            updateAddDetailAvailability();
        };

        confirmActionButtons.forEach((button) => {
            button.addEventListener("click", () => {
                pendingConfirmedFormAction = button.getAttribute("data-formaction") || "";
                const actionLabel = String(button.getAttribute("data-action-label") || "continue").trim().toLowerCase();
                if (confirmActionMessage) {
                    confirmActionMessage.textContent = `Are you sure you want to ${actionLabel} this purchase requisition?`;
                }
                if (confirmActionModal && window.jQuery) {
                    window.jQuery(confirmActionModal).modal("show");
                }
            });
        });

        confirmActionYesButton?.addEventListener("click", () => {
            const detailForm = document.getElementById("prqDetailForm");
            if (!(detailForm instanceof HTMLFormElement) || !pendingConfirmedFormAction) {
                return;
            }

            detailForm.action = pendingConfirmedFormAction;
            if (confirmActionModal && window.jQuery) {
                window.jQuery(confirmActionModal).modal("hide");
            }
            detailForm.submit();
        });

        rowsContainer.addEventListener("input", (ev) => {
            const target = ev.target;
            if (!(target instanceof HTMLInputElement)) {
                return;
            }

            const row = target.closest("tr[data-row='1']");
            if (!row) {
                return;
            }

            const rowKey = row.getAttribute("data-row-key") || "";
            const detail = details.find((item) => getRowKey(item) === rowKey);
            if (!detail || !getConfig().canEditExistingDetailFields) {
                return;
            }

            if (target.classList.contains("prq-detail-edit-price")) {
                target.value = sanitizeNonNegativeDecimal(target.value);
                detail.unitPrice = toNumber(target.value);
                detail.amount = toNumber(detail.qtyPur) * toNumber(detail.unitPrice);
            } else if (target.classList.contains("prq-detail-edit-amount")) {
                target.value = sanitizeNonNegativeDecimal(target.value);
                detail.amount = toNumber(target.value);
            } else if (target.classList.contains("prq-detail-edit-remark")) {
                detail.remark = target.value || "";
            } else {
                return;
            }

            const amountInput = row.querySelector(".prq-detail-edit-amount");
            if (amountInput instanceof HTMLInputElement) {
                amountInput.value = formatNumber(detail.amount);
            }

            syncHiddenInput();
            updateSummary();
        });

        rowsContainer.addEventListener("blur", (ev) => {
            const target = ev.target;
            if (!(target instanceof HTMLInputElement)) {
                return;
            }

            if (target.classList.contains("prq-detail-edit-price") || target.classList.contains("prq-detail-edit-amount")) {
                clampNonNegativeDecimalInput(target);
                target.dispatchEvent(new Event("input", { bubbles: true }));
            }
        }, true);

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

        const hasDetailByMrDetail = (mrDetailId) => {
            const targetMrDetailId = Number(mrDetailId || 0);
            if (targetMrDetailId <= 0) {
                return false;
            }

            return details.some((detail) => Number(detail.mrDetailId || 0) === targetMrDetailId);
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
                const isAlreadyInPr = hasDetailByMrDetail(mrDetailId);
                const baseBuy = toNumber(row.getAttribute("data-base-buy") || row.getAttribute("data-buy") || "0");
                const remainingBuy = isAlreadyInPr ? 0 : baseBuy;
                const checkbox = row.querySelector(".prq-add-detail-check");
                const subQtyInput = row.querySelector(".prq-add-detail-subqty");
                const buyCell = row.querySelector(".prq-add-detail-buy");

                row.setAttribute("data-current-buy", String(remainingBuy));
                row.style.display = isAlreadyInPr ? "none" : "";

                if (buyCell) {
                    buyCell.textContent = formatNumber(remainingBuy);
                }

                if (checkbox) {
                    checkbox.checked = false;
                    checkbox.disabled = isAlreadyInPr;
                }

                if (subQtyInput) {
                    subQtyInput.disabled = isAlreadyInPr;
                    subQtyInput.value = formatNumber(remainingBuy);
                }

                row.classList.remove("prq-detail-row-selected");
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

            if (event.target.closest(".prq-detail-edit-price, .prq-detail-edit-remark")) {
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
                specificationCell.innerHTML = buildSingleLineEllipsisText(row.getAttribute("data-specification") || "", "vni-font");
            }
            if (noteCell) {
                noteCell.innerHTML = buildSingleLineEllipsisText(row.getAttribute("data-note") || "", "vni-font");
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

                clampNonNegativeDecimalInput(subQtyInput);
                hideAddDetailError();
            });
            subQtyInput?.addEventListener("blur", () => {
                clampNonNegativeDecimalInput(subQtyInput);
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

                const initialAllocatedQty = Number(initialAllocatedQtyByMrDetail[mrDetailId] || 0);

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
                    amount: qtyPurValue * unitPrice,
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
        attachmentSelectors.forEach((checkbox) => {
            checkbox.addEventListener("change", updateAttachmentDeleteButton);
        });

        if (viewDetailUseDateRange && viewDetailFromDate && viewDetailToDate) {
            const syncViewDetailDateRange = () => {
                const enabled = viewDetailUseDateRange.checked;
                viewDetailFromDate.disabled = !enabled;
                viewDetailToDate.disabled = !enabled;
                if (!enabled) {
                    viewDetailFromDate.value = "";
                    viewDetailToDate.value = "";
                }
            };

            viewDetailUseDateRange.addEventListener("change", syncViewDetailDateRange);
            syncViewDetailDateRange();
        }

        openViewDetailButton?.addEventListener("click", () => {
            if (!viewDetailState.hasLoaded) {
                loadViewDetailRows(true);
            }
        });

        viewDetailSearchButton?.addEventListener("click", () => {
            loadViewDetailRows(true);
        });

        viewDetailPageSize?.addEventListener("change", () => {
            viewDetailState.pageSize = Number.parseInt(viewDetailPageSize.value || "10", 10) || 10;
            loadViewDetailRows(true);
        });

        viewDetailPagination?.addEventListener("click", (event) => {
            const link = event.target.closest("[data-page]");
            if (!link) {
                return;
            }

            event.preventDefault();
            const page = Number.parseInt(link.getAttribute("data-page") || "1", 10) || 1;
            if (page <= 0 || page === viewDetailState.pageNumber || viewDetailState.loading) {
                return;
            }

            viewDetailState.pageNumber = page;
            loadViewDetailRows(false);
        });

        [viewDetailRequestNo, viewDetailDescription, viewDetailRecQty, viewDetailItemCode, viewDetailFromDate, viewDetailToDate].forEach((input) => {
            input?.addEventListener("keydown", (event) => {
                if (event.key === "Enter") {
                    event.preventDefault();
                    loadViewDetailRows(true);
                }
            });
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
        updateAttachmentDeleteButton();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initDetail);
    } else {
        initDetail();
    }
})();
