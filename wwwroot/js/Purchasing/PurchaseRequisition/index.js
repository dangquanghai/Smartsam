(() => {
    "use strict";

    function getConfig() {
        return window.purchaseRequisitionPage || {
            canEdit: false,
            canViewDetail: false,
            canAddAt: false,
            canDisapproval: false,
            detailUrlBase: "",
            allowedAttachmentExtensions: ".doc,.docx,.xls,.xlsx,.pdf,.jpg,.jpeg,.png",
            maxAttachmentSizeMb: 10
        };
    }

    function showDangerModal(message) {
        const dangerModal = document.getElementById("prqDangerModal");
        const messageEl = document.getElementById("prqDangerModalMessage");
        if (!dangerModal || !messageEl || !window.jQuery) {
            return;
        }

        messageEl.textContent = String(message ?? "").trim();
        window.jQuery(dangerModal).modal("show");
    }

    function toNumber(value) {
        const normalized = String(value ?? "")
            .trim()
            .replace(/,/g, "");
        const n = Number.parseFloat(normalized);
        return Number.isFinite(n) ? n : 0;
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

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/\"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function buildCollapsibleText(value, previewLength = 40) {
        const text = String(value ?? "").trim();
        if (!text) {
            return "";
        }

        if (text.length <= previewLength) {
            return `<div class="prq-description-cell prq-add-at-text-cell"><span class="prq-description-full">${escapeHtml(text)}</span></div>`;
        }

        const preview = `${escapeHtml(text.slice(0, previewLength))}...`;
        const full = escapeHtml(text);
        return `
            <div class="prq-description-cell prq-add-at-text-cell">
                <span class="prq-description-preview">${preview}</span>
                <button type="button" class="prq-read-more">Read more</button>
                <span class="prq-description-full d-none">${full}</span>
            </div>`;
    }

    function buildDetailUrl(id, mode) {
        const base = getConfig().detailUrlBase || "";
        return `${base}/${id}?mode=${mode}`;
    }

    function canEditSelectedRow(row) {
        return !!row && row.getAttribute("data-can-edit") === "true";
    }

    function canViewDetailSelectedRow(row) {
        return !!row && row.getAttribute("data-can-view-detail") === "true";
    }

    function initSelection() {
        const rows = Array.from(document.querySelectorAll(".prq-row"));
        const rowChecks = Array.from(document.querySelectorAll(".prq-selector"));
        if (rows.length === 0 || rowChecks.length === 0) return;

        const syncState = () => {
            rows.forEach((row) => {
                const check = row.querySelector(".prq-selector");
                row.classList.toggle("selected", !!check && check.checked);
            });
            document.dispatchEvent(new CustomEvent("prq:selection-changed"));
        };

        rows.forEach((row) => {
            row.addEventListener("click", (ev) => {
                if (ev.target && ev.target.closest("input, a, button")) return;
                const check = row.querySelector(".prq-selector");
                if (!check) return;
                const willSelect = !check.checked;
                rowChecks.forEach((item) => { item.checked = false; });
                check.checked = willSelect;
                syncState();
            });

            const link = row.querySelector(".prq-row-link");
            link?.addEventListener("click", (ev) => {
                ev.preventDefault();
                const id = row.getAttribute("data-prid");
                if (!id) return;
                const mode = canEditSelectedRow(row) ? "edit" : "view";
                if (canEditSelectedRow(row) || canViewDetailSelectedRow(row)) {
                    window.location.href = buildDetailUrl(id, mode);
                }
            });
        });

        rowChecks.forEach((check) => {
            check.addEventListener("mousedown", () => {
                check.dataset.wasChecked = check.checked ? "true" : "false";
            });

            check.addEventListener("change", (ev) => {
                const target = ev.currentTarget;
                if (target.checked) {
                    rowChecks.forEach((item) => {
                        if (item !== target) {
                            item.checked = false;
                        }
                    });
                }
                syncState();
            });

            check.addEventListener("click", (ev) => {
                const target = ev.currentTarget;
                if (target.dataset.wasChecked !== "true") {
                    return;
                }

                ev.preventDefault();
                target.checked = false;
                delete target.dataset.wasChecked;
                syncState();
            });
        });
        window.getPrqSelectedRows = () => rows.filter((row) => row.querySelector(".prq-selector")?.checked);
        syncState();
    }

    function initDateRangeToggle() {
        const form = document.getElementById("prqSearchForm");
        const useDateRange = document.getElementById("Filter_UseDateRange");
        const fromDate = document.getElementById("Filter_FromDate");
        const toDate = document.getElementById("Filter_ToDate");
        if (!useDateRange || !fromDate || !toDate) return;

        const syncRangeConstraints = () => {
            const fromValue = fromDate.value;
            const toValue = toDate.value;
            toDate.min = fromValue || "";
            toDate.disabled = !useDateRange.checked || !fromValue;
            if (!fromValue) {
                toDate.value = "";
                return;
            }
            if (fromValue && toValue && fromValue > toValue) {
                toDate.value = "";
            }
        };

        const sync = () => {
            const disabled = !useDateRange.checked;
            fromDate.disabled = disabled;
            if (disabled) {
                fromDate.value = "";
                toDate.value = "";
            }
            syncRangeConstraints();
        };

        fromDate.addEventListener("change", syncRangeConstraints);
        toDate.addEventListener("change", syncRangeConstraints);
        form?.addEventListener("submit", (ev) => {
            if (!useDateRange.checked) {
                fromDate.value = "";
                toDate.value = "";
                return;
            }
            if (!fromDate.value) {
                toDate.value = "";
                return;
            }
            if (fromDate.value && toDate.value && fromDate.value > toDate.value) {
                ev.preventDefault();
                alert("From Date must be less than or equal to To Date.");
                toDate.focus();
            }
        });

        useDateRange.addEventListener("change", sync);
        sync();
    }

    function initPageSizeSubmit() {
        const form = document.getElementById("prqSearchForm");
        const pageSizeSelect = document.getElementById("prqPageSize");
        if (!form || !pageSizeSelect) return;

        pageSizeSelect.addEventListener("change", () => {
            const pageSizeInput = form.querySelector("input[name='PageSize']");
            const pageInput = form.querySelector("input[name='PageNumber']");
            if (!pageSizeInput || !pageInput) return;
            pageSizeInput.value = pageSizeSelect.value;
            pageInput.value = "1";
            form.submit();
        });
    }

    function initDescriptionToggle() {
        document.addEventListener("click", (ev) => {
            const toggle = ev.target.closest(".prq-read-more");
            if (!toggle) return;

            const container = toggle.closest(".prq-description-cell");
            if (!container) return;

            const preview = container.querySelector(".prq-description-preview");
            const full = container.querySelector(".prq-description-full");
            if (!preview || !full) return;

            const isExpanded = !full.classList.contains("d-none");
            if (isExpanded) return;

            preview.classList.add("d-none");
            full.classList.remove("d-none");
            toggle.remove();
        });
    }

    function initActions() {
        const btnEdit = document.getElementById("btnEdit");
        const btnViewDetail = document.getElementById("btnViewDetail");
        const btnExportExcel = document.getElementById("btnExportExcel");
        const btnDisapproval = document.getElementById("btnDisapproval");
        const config = getConfig();

        const getSelectedRow = () => {
            const rows = typeof window.getPrqSelectedRows === "function" ? window.getPrqSelectedRows() : [];
            return rows.length === 1 ? rows[0] : null;
        };

        const syncState = () => {
            const selectedRow = getSelectedRow();
            const hasSelection = !!selectedRow;
            if (btnEdit) btnEdit.disabled = !hasSelection || !config.canEdit || !canEditSelectedRow(selectedRow);
            if (btnViewDetail) btnViewDetail.disabled = !hasSelection || !config.canViewDetail || !canViewDetailSelectedRow(selectedRow);
            if (btnDisapproval) btnDisapproval.disabled = !hasSelection || !config.canDisapproval;
        };

        btnEdit?.addEventListener("click", () => {
            const row = getSelectedRow();
            if (!row || !canEditSelectedRow(row)) return;
            window.location.href = buildDetailUrl(row.getAttribute("data-prid"), "edit");
        });

        btnViewDetail?.addEventListener("click", () => {
            const row = getSelectedRow();
            if (!row || !canViewDetailSelectedRow(row)) return;
            window.location.href = buildDetailUrl(row.getAttribute("data-prid"), "view");
        });

        btnExportExcel?.addEventListener("click", () => {
            const params = new URLSearchParams();
            const selectedRow = getSelectedRow();
            const requestNo = document.getElementById("Filter_RequestNo")?.value || "";
            const statusId = document.getElementById("Filter_StatusId")?.value || "";
            const description = document.getElementById("Filter_Description")?.value || "";
            const useDateRange = document.getElementById("Filter_UseDateRange")?.checked || false;
            const fromDate = document.getElementById("Filter_FromDate")?.value || "";
            const toDate = document.getElementById("Filter_ToDate")?.value || "";

            if (requestNo) params.set("RequestNo", requestNo);
            if (statusId) params.set("StatusId", statusId);
            if (description) params.set("Description", description);
            params.set("UseDateRange", useDateRange ? "true" : "false");
            if (useDateRange && fromDate) params.set("FromDate", fromDate);
            if (useDateRange && toDate) params.set("ToDate", toDate);
            if (selectedRow) {
                const selectedPrId = selectedRow.getAttribute("data-prid") || "";
                if (selectedPrId) params.set("SelectedPrId", selectedPrId);
            }

            const query = params.toString();
            window.location.href = `${window.location.pathname}?handler=ExportExcel${query ? `&${query}` : ""}`;
        });
        btnDisapproval?.addEventListener("click", () => alert("Disapproval is not implemented yet."));

        document.addEventListener("prq:selection-changed", syncState);
        syncState();
    }

    function initAddAt() {
        const addAtBtn = document.getElementById("prqAddAtBtn");
        const addAtModal = document.getElementById("prqAddAtModal");
        const detailsJsonEl = document.getElementById("AddAtDetailsJson");
        const requestNoHiddenEl = document.getElementById("AddAtRequestNo");
        const requestDateHiddenEl = document.getElementById("AddAtRequestDate");
        const currencyIdHiddenEl = document.getElementById("AddAtCurrencyId");
        const requestNoDisplayEl = document.getElementById("prqAddAtRequestNoDisplay");
        const requestDateDisplayEl = document.getElementById("prqAddAtRequestDateDisplay");
        const currencySelectEl = document.getElementById("prqAddAtCurrencySelect");
        const descriptionEl = document.getElementById("prqAddAtDescription");
        const attachmentEl = document.getElementById("prqAddAtAttachments");
        const attachmentErrorEl = document.getElementById("prqAddAtAttachmentError");
        const addAtForm = document.getElementById("prqAddAtForm");
        const rowsContainer = document.getElementById("prqAddAtDetailRows");
        const emptyRow = document.getElementById("prqAddAtDetailEmptyRow");
        if (!addAtBtn || !addAtModal || !detailsJsonEl || !requestNoHiddenEl || !requestDateHiddenEl || !currencyIdHiddenEl || !requestNoDisplayEl || !requestDateDisplayEl || !currencySelectEl || !descriptionEl || !attachmentEl || !attachmentErrorEl || !addAtForm || !rowsContainer || !emptyRow) return;

        let sourceRows = [];
        let isLoading = false;
        const config = getConfig();
        const allowedExtensions = String(config.allowedAttachmentExtensions || "")
            .split(",")
            .map((item) => item.trim().toLowerCase())
            .filter((item) => item);
        const maxFileSizeBytes = Number(config.maxAttachmentSizeMb || 0) * 1024 * 1024;

        const showAttachmentError = (message) => {
            attachmentErrorEl.textContent = message;
            attachmentErrorEl.classList.remove("d-none");
        };

        const clearAttachmentError = () => {
            attachmentErrorEl.textContent = "";
            attachmentErrorEl.classList.add("d-none");
        };

        const validateAttachment = () => {
            clearAttachmentError();

            const file = attachmentEl.files && attachmentEl.files.length ? attachmentEl.files[0] : null;
            if (!file) {
                return true;
            }

            const fileName = String(file.name || "");
            const dotIndex = fileName.lastIndexOf(".");
            const extension = dotIndex >= 0 ? fileName.substring(dotIndex).toLowerCase() : "";
            if (allowedExtensions.length && !allowedExtensions.includes(extension)) {
                showAttachmentError(`Attachment file type is invalid. Allowed: ${config.allowedAttachmentExtensions}`);
                attachmentEl.value = "";
                return false;
            }

            if (maxFileSizeBytes > 0 && file.size > maxFileSizeBytes) {
                showAttachmentError(`Attachment size cannot exceed ${config.maxAttachmentSizeMb} MB.`);
                attachmentEl.value = "";
                return false;
            }

            return true;
        };

        const updateAddAtButtonState = () => {
            addAtBtn.disabled = !getConfig().canAddAt;
        };

        const setLoadingState = (loading) => {
            isLoading = loading;
            addAtBtn.disabled = loading || !getConfig().canAddAt;
            const submitBtn = addAtForm.querySelector("button[type='submit']");
            if (submitBtn) {
                submitBtn.disabled = loading;
            }

            if (loading) {
                emptyRow.style.display = "";
                emptyRow.querySelector("td").textContent = "Loading MR rows...";
            } else if (!sourceRows.length) {
                emptyRow.style.display = "";
                emptyRow.querySelector("td").textContent = "No MR rows";
            }
        };

        const syncHiddenValues = () => {
            requestNoHiddenEl.value = requestNoDisplayEl.value || "";
            requestDateHiddenEl.value = requestDateDisplayEl.value || "";
            currencyIdHiddenEl.value = currencySelectEl.value || "1";
        };

        const renderRows = () => {
            rowsContainer.querySelectorAll("tr[data-row='1']").forEach((row) => row.remove());
            if (!sourceRows.length) {
                emptyRow.style.display = "";
                return;
            }

            emptyRow.style.display = "none";
            sourceRows.forEach((row, index) => {
                const tr = document.createElement("tr");
                tr.dataset.row = "1";
                tr.innerHTML = `
                    <td class="text-center">
                        <input type="checkbox" class="prq-add-at-check" data-index="${index}" ${row.checked ? "checked" : ""} />
                    </td>
                    <td>${escapeHtml(row.requestNo)}</td>
                    <td>${escapeHtml(row.itemCode)}</td>
                    <td>${escapeHtml(row.itemName)}</td>
                    <td class="prq-center">${formatNumber(row.buy)}</td>
                    <td class="prq-center">
                        <input type="text" inputmode="decimal" class="form-control form-control-sm prq-add-at-sugbuy" data-index="${index}" value="${formatNumber(row.sugBuy)}" />
                    </td>
                    <td class="prq-center">${escapeHtml(row.unit)}</td>
                    <td class="prq-center">${formatNumber(row.unitPrice)}</td>
                    <td>${buildCollapsibleText(row.specification, 36)}</td>
                    <td>${buildCollapsibleText(row.note, 34)}</td>`;
                rowsContainer.appendChild(tr);
            });
        };

        const syncSelectedRowsJson = () => {
            const selectedRows = sourceRows
                .filter((row) => row.checked)
                .map((row) => ({
                    requestNo: row.requestNo,
                    itemId: row.itemId,
                    itemCode: row.itemCode,
                    itemName: row.itemName,
                    unit: row.unit,
                    buy: row.buy,
                    sugBuy: row.sugBuy,
                    unitPrice: row.unitPrice,
                    specification: row.specification,
                    note: row.note,
                    mrDetailId: row.mrDetailId
                }));
            detailsJsonEl.value = JSON.stringify(selectedRows);
        };

        const resetModal = () => {
            sourceRows = [];
            detailsJsonEl.value = "[]";
            requestNoHiddenEl.value = "";
            requestDateHiddenEl.value = "";
            currencyIdHiddenEl.value = "1";
            requestNoDisplayEl.value = "";
            requestDateDisplayEl.value = "";
            currencySelectEl.innerHTML = "";
            descriptionEl.value = "";
            attachmentEl.value = "";
            clearAttachmentError();
            renderRows();
        };

        const bindModalData = (payload) => {
            requestNoDisplayEl.value = payload.requestNo || "";
            requestDateDisplayEl.value = payload.requestDate || "";
            currencySelectEl.innerHTML = "";

            (payload.currencies || []).forEach((currency) => {
                const option = document.createElement("option");
                option.value = currency.id;
                option.textContent = `${currency.name}`;
                if (String(currency.id) === String(payload.currencyId)) {
                    option.selected = true;
                }
                currencySelectEl.appendChild(option);
            });

            sourceRows = (payload.rows || []).map((row) => ({
                requestNo: row.requestNo,
                itemId: row.itemId,
                itemCode: row.itemCode || "",
                itemName: row.itemName || "",
                unit: row.unit || "",
                buy: toNumber(row.buy),
                sugBuy: toNumber(row.sugBuy || row.buy),
                unitPrice: toNumber(row.unitPrice),
                specification: row.specification || "",
                note: row.note || "",
                mrDetailId: Number.parseInt(row.mrDetailId, 10) || 0,
                checked: false
            }));

            syncHiddenValues();
            renderRows();
        };

        const loadSourceRows = async () => {
            setLoadingState(true);
            try {
                const response = await fetch(`${window.location.pathname}?handler=AddAtSource`, {
                    headers: { "X-Requested-With": "XMLHttpRequest" }
                });
                const payload = await response.json();
                if (!response.ok || !payload.success) {
                    throw new Error(payload.message || "Cannot load Add AT source data.");
                }
                bindModalData(payload);
            } catch (error) {
                resetModal();
                showDangerModal(error.message || "Cannot load Add AT source data.");
                if (window.jQuery) {
                    window.jQuery(addAtModal).modal("hide");
                }
            } finally {
                setLoadingState(false);
            }
        };

        addAtBtn.addEventListener("click", () => {
            resetModal();
            if (window.jQuery) {
                window.jQuery(addAtModal).modal("show");
            }
            loadSourceRows();
        });

        currencySelectEl.addEventListener("change", syncHiddenValues);
        attachmentEl.addEventListener("change", validateAttachment);

        rowsContainer.addEventListener("change", (ev) => {
            const check = ev.target.closest(".prq-add-at-check");
            if (check) {
                const index = Number.parseInt(check.getAttribute("data-index"), 10);
                if (Number.isInteger(index) && sourceRows[index]) {
                    sourceRows[index].checked = check.checked;
                }
                return;
            }

            const sugBuyInput = ev.target.closest(".prq-add-at-sugbuy");
            if (!sugBuyInput) return;

            const index = Number.parseInt(sugBuyInput.getAttribute("data-index"), 10);
            if (!Number.isInteger(index) || !sourceRows[index]) return;

            const parsedValue = toNumber(sugBuyInput.value);
            sourceRows[index].sugBuy = parsedValue;
            sugBuyInput.value = formatNumber(parsedValue);
        });

        rowsContainer.addEventListener("input", (ev) => {
            const sugBuyInput = ev.target.closest(".prq-add-at-sugbuy");
            if (!sugBuyInput) return;

            const sanitizedValue = sanitizeNonNegativeDecimal(sugBuyInput.value);
            if (sugBuyInput.value !== sanitizedValue) {
                sugBuyInput.value = sanitizedValue;
            }
        });

        addAtForm.addEventListener("submit", (ev) => {
            if (isLoading) {
                ev.preventDefault();
                return;
            }

            syncHiddenValues();

            if (!validateAttachment()) {
                ev.preventDefault();
                return;
            }

            const selectedRows = sourceRows.filter((row) => row.checked);
            if (!selectedRows.length) {
                ev.preventDefault();
                showDangerModal("Please select at least one MR row.");
                return;
            }

            const invalidRow = selectedRows.find((row) => row.sugBuy <= 0 || row.sugBuy > row.buy);
            if (invalidRow) {
                ev.preventDefault();
                showDangerModal("SugBuy must be greater than 0 and cannot be greater than BUY.");
                return;
            }

            syncSelectedRowsJson();
        });

        if (window.jQuery) {
            window.jQuery(addAtModal).on("hidden.bs.modal", () => {
                resetModal();
                updateAddAtButtonState();
            });
        }

        updateAddAtButtonState();
    }

    function initPage() {
        initSelection();
        initDateRangeToggle();
        initPageSizeSubmit();
        initDescriptionToggle();
        initActions();
        initAddAt();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initPage);
    } else {
        initPage();
    }
})();
