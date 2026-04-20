(() => {
    "use strict";

    function getConfig() {
        return window.purchaseRequisitionPage || {
            canEdit: false,
            canViewDetail: false,
            canAddAt: false,
            canDisapproval: false,
            detailUrlBase: "",
            viewDetailReportEndpoint: "",
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
            .replace(/\"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function buildCollapsibleText(value, previewLength = 40) {
        const text = String(value ?? "").trim();
        if (!text) {
            return "";
        }

        if (text.length <= previewLength) {
            return `<div class="prq-description-cell prq-add-at-text-cell"><span class="prq-description-full vni-font">${escapeHtml(text)}</span></div>`;
        }

        const preview = `${escapeHtml(text.slice(0, previewLength))}...`;
        const full = escapeHtml(text);
        return `
            <div class="prq-description-cell prq-add-at-text-cell">
                <span class="prq-description-preview vni-font">${preview}</span>
                <button type="button" class="prq-read-more">Read more</button>
                <span class="prq-description-full d-none vni-font">${full}</span>
            </div>`;
    }

    function buildSingleLineEllipsisText(value, extraClass = "") {
        const text = String(value ?? "").trim();
        if (!text) {
            return "";
        }

        const className = extraClass ? ` ${extraClass}` : "";
        return `<span class="prq-add-at-ellipsis${className}" title="${escapeHtml(text)}">${escapeHtml(text)}</span>`;
    }

    function buildDetailUrl(id, mode) {
        const base = getConfig().detailUrlBase || "";
        const params = new URLSearchParams();
        params.set("id", id);
        params.set("mode", mode);
        return `${base}?${params.toString()}`;
    }

    function canEditSelectedRow(row) {
        return !!row && row.getAttribute("data-can-edit") === "true";
    }

    function canApproveSelectedRow(row) {
        return !!row && row.getAttribute("data-can-approve") === "true";
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
                const href = link.getAttribute("href");
                if (href && href !== "#") {
                    window.location.href = href;
                    return;
                }

                const id = row.getAttribute("data-prid");
                if (!id) return;
                const mode = canEditSelectedRow(row) ? "edit" : canApproveSelectedRow(row) ? "approve" : "view";
                if (canEditSelectedRow(row) || canApproveSelectedRow(row) || canViewDetailSelectedRow(row)) {
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
        const btnExportExcel = document.getElementById("btnExportExcel");
        const btnDisapproval = document.getElementById("btnDisapproval");
        const btnViewPrDetail = document.getElementById("btnViewPrDetail");
        const viewDetailModal = document.getElementById("prqViewDetailModal");
        const viewDetailRequestNo = document.getElementById("prqViewDetailRequestNo");
        const viewDetailDescription = document.getElementById("prqViewDetailDescription");
        const viewDetailRecQtyOperator = document.getElementById("prqViewDetailRecQtyOperator");
        const viewDetailRecQty = document.getElementById("prqViewDetailRecQty");
        const viewDetailItemCode = document.getElementById("prqViewDetailItemCode");
        const viewDetailUseDateRange = document.getElementById("prqViewDetailUseDateRange");
        const viewDetailFromDate = document.getElementById("prqViewDetailFromDate");
        const viewDetailToDate = document.getElementById("prqViewDetailToDate");
        const viewDetailSearchButton = document.getElementById("btnPrqViewDetailSearch");
        const viewDetailReportButton = document.getElementById("btnPrqViewDetailReport");
        const viewDetailRows = document.getElementById("prqViewDetailRows");
        const viewDetailEmptyRow = document.getElementById("prqViewDetailEmptyRow");
        const viewDetailPaginationInfo = document.getElementById("prqViewDetailPaginationInfo");
        const viewDetailPagination = document.getElementById("prqViewDetailPagination");
        const viewDetailPageSize = document.getElementById("prqViewDetailPageSize");

        const getSelectedRow = () => {
            const rows = typeof window.getPrqSelectedRows === "function" ? window.getPrqSelectedRows() : [];
            return rows.length === 1 ? rows[0] : null;
        };

        const viewDetailState = {
            pageNumber: 1,
            pageSize: viewDetailPageSize ? Number.parseInt(viewDetailPageSize.value || "10", 10) || 10 : 10,
            totalPages: 1,
            loading: false
        };

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

        const buildViewDetailReportUrl = () => {
            const endpoint = getConfig().viewDetailReportEndpoint || "";
            const requestUrl = new URL(endpoint, window.location.origin);
            const filter = getViewDetailFilter();
            const params = new URLSearchParams();

            Object.entries(filter).forEach(([key, value]) => {
                if (key === "pageNumber" || key === "pageSize" || value === "" || value == null) {
                    return;
                }
                params.set(key, String(value));
            });

            params.forEach((value, key) => {
                requestUrl.searchParams.set(key, value);
            });

            return requestUrl.toString();
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
            viewDetailState.pageNumber = currentPage;
            viewDetailState.pageSize = pageSize;
            viewDetailState.totalPages = totalPages;

            if (viewDetailPageSize) {
                viewDetailPageSize.value = String(pageSize);
            }

            if (!rows.length) {
                viewDetailEmptyRow.style.display = "";
                viewDetailEmptyRow.querySelector("td").textContent = "No detail rows";
            } else {
                viewDetailEmptyRow.style.display = "none";
                rows.forEach((row) => {
                    const tr = document.createElement("tr");
                    tr.dataset.viewDetailRow = "1";
                    const itemCodeText = String(row.itemCode || "").trim();
                    const itemNameText = String(row.itemName || "").trim();
                    const itemTitle = `${itemCodeText}${itemNameText ? ` / ${itemNameText}` : ""}`;
                    const itemCodeHtml = itemCodeText ? `<span class="prq-view-detail-item-code">${escapeHtml(itemCodeText)}</span>` : "";
                    const itemSeparatorHtml = itemCodeText && itemNameText ? '<span class="prq-view-detail-item-separator"> / </span>' : "";
                    const itemNameHtml = itemNameText
                        ? `<span class="prq-view-detail-item-name tcvn3-font prq-view-detail-ellipsis" title="${escapeHtml(itemNameText)}">${escapeHtml(itemNameText)}</span>`
                        : "";
                    tr.innerHTML = `
                        <td>${escapeHtml(row.requestNo || "")}</td>
                        <td>${escapeHtml(row.requestDateText || "")}</td>
                        <td class="prq-view-detail-col-description">${buildSingleLineEllipsisText(row.description || "", "vni-font prq-view-detail-ellipsis")}</td>
                        <td class="prq-view-detail-col-item" title="${escapeHtml(itemTitle)}"><div class="prq-view-detail-item-wrap">${itemCodeHtml}${itemSeparatorHtml}${itemNameHtml}</div></td>
                        <td class="prq-center">${formatNumber(row.prQty || 0)}</td>
                        <td class="prq-center">${formatNumber(row.recQty || 0)}</td>`;
                    viewDetailRows.appendChild(tr);
                });
            }

            const start = totalRecords === 0 ? 0 : ((currentPage - 1) * pageSize) + 1;
            const end = totalRecords === 0 ? 0 : Math.min(currentPage * pageSize, totalRecords);
            viewDetailPaginationInfo.innerHTML = totalRecords === 0
                ? "<small>No records</small>"
                : `<small>Showing ${start} to ${end} of ${totalRecords} entries</small>`;

            viewDetailPagination.innerHTML = "";
            const appendPageItem = (label, page, disabled, active) => {
                const li = document.createElement("li");
                li.className = `page-item${disabled ? " disabled" : ""}${active ? " active" : ""}`;
                const inner = document.createElement(disabled || active ? "span" : "button");
                inner.className = "page-link prq-pager-link";
                inner.textContent = label;
                if (!disabled && !active) {
                    inner.type = "button";
                    inner.dataset.page = String(page);
                }
                li.appendChild(inner);
                viewDetailPagination.appendChild(li);
            };

            appendPageItem("Prev", Math.max(1, currentPage - 1), currentPage <= 1, false);
            buildViewDetailPages(currentPage, totalPages).forEach((page) => {
                if (page == null) {
                    const li = document.createElement("li");
                    li.className = "page-item disabled";
                    li.innerHTML = '<span class="page-link">...</span>';
                    viewDetailPagination.appendChild(li);
                    return;
                }

                appendPageItem(String(page), page, false, page === currentPage);
            });
            appendPageItem("Next", Math.min(totalPages, currentPage + 1), currentPage >= totalPages, false);
        };

        const loadViewDetailRows = async () => {
            if (!viewDetailModal) {
                return;
            }

            const filter = getViewDetailFilter();

            viewDetailState.loading = true;
            if (viewDetailSearchButton) {
                viewDetailSearchButton.disabled = true;
            }
            if (viewDetailEmptyRow) {
                viewDetailEmptyRow.style.display = "";
                viewDetailEmptyRow.querySelector("td").textContent = "Loading detail rows...";
            }

            try {
                const endpoint = getConfig().viewDetailEndpoint || "";
                const requestUrl = new URL(endpoint, window.location.origin);
                const params = new URLSearchParams();
                Object.entries(filter).forEach(([key, value]) => {
                    if (value === "" || value == null) {
                        return;
                    }
                    params.set(key, String(value));
                });

                params.forEach((value, key) => {
                    requestUrl.searchParams.set(key, value);
                });

                const response = await fetch(requestUrl.toString(), {
                    headers: { "X-Requested-With": "XMLHttpRequest" }
                });
                const responseText = await response.text();
                let payload = null;
                try {
                    payload = responseText ? JSON.parse(responseText) : {};
                } catch {
                    throw new Error("Cannot load purchase requisition details.");
                }

                if (!response.ok) {
                    throw new Error(payload?.message || "Cannot load purchase requisition details.");
                }

                renderViewDetailRows(payload);
            } catch (error) {
                renderViewDetailRows({ rows: [], totalRecords: 0, pageNumber: 1, pageSize: viewDetailState.pageSize, totalPages: 1 });
                showDangerModal(error.message || "Cannot load purchase requisition details.");
            } finally {
                viewDetailState.loading = false;
                if (viewDetailSearchButton) {
                    viewDetailSearchButton.disabled = false;
                }
            }
        };

        const syncViewDetailDateRange = () => {
            if (!viewDetailUseDateRange || !viewDetailFromDate || !viewDetailToDate) {
                return;
            }

            const enabled = viewDetailUseDateRange.checked;
            viewDetailFromDate.disabled = !enabled;
            viewDetailToDate.disabled = !enabled || !viewDetailFromDate.value;
            viewDetailToDate.min = viewDetailFromDate.value || "";

            if (!enabled) {
                viewDetailFromDate.value = "";
                viewDetailToDate.value = "";
            }

            if (viewDetailFromDate.value && viewDetailToDate.value && viewDetailFromDate.value > viewDetailToDate.value) {
                viewDetailToDate.value = "";
            }
        };

        const syncState = () => {
            const selectedRow = getSelectedRow();
            const hasSelection = !!selectedRow;
            if (btnDisapproval) btnDisapproval.disabled = !hasSelection || !getConfig().canDisapproval;
        };

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

        btnViewPrDetail?.addEventListener("click", () => {
            const selectedRow = getSelectedRow();
            if (!getConfig().canViewDetail) {
                return;
            }

            const selectedRequestNo = selectedRow ? (selectedRow.getAttribute("data-request-no") || "") : "";
            if (viewDetailRequestNo) {
                viewDetailRequestNo.value = selectedRow && canViewDetailSelectedRow(selectedRow) ? selectedRequestNo : "";
            }
            if (viewDetailDescription) {
                viewDetailDescription.value = "";
            }
            if (viewDetailRecQty) {
                viewDetailRecQty.value = "";
            }
            if (viewDetailItemCode) {
                viewDetailItemCode.value = "";
            }
            if (viewDetailRecQtyOperator) {
                viewDetailRecQtyOperator.value = "=";
            }
            if (viewDetailUseDateRange) {
                viewDetailUseDateRange.checked = false;
            }
            if (viewDetailFromDate) {
                viewDetailFromDate.value = "";
            }
            if (viewDetailToDate) {
                viewDetailToDate.value = "";
            }

            syncViewDetailDateRange();
            viewDetailState.pageNumber = 1;
            viewDetailState.pageSize = viewDetailPageSize ? Number.parseInt(viewDetailPageSize.value || "10", 10) || 10 : 10;

            if (window.jQuery) {
                window.jQuery(viewDetailModal).modal("show");
            }
            loadViewDetailRows();
        });

        viewDetailSearchButton?.addEventListener("click", () => {
            viewDetailState.pageNumber = 1;
            loadViewDetailRows();
        });

        viewDetailReportButton?.addEventListener("click", () => {
            window.open(buildViewDetailReportUrl(), "_blank");
        });

        viewDetailPageSize?.addEventListener("change", () => {
            viewDetailState.pageSize = Number.parseInt(viewDetailPageSize.value || "10", 10) || 10;
            viewDetailState.pageNumber = 1;
            loadViewDetailRows();
        });

        viewDetailPagination?.addEventListener("click", (ev) => {
            const button = ev.target.closest("button[data-page]");
            if (!button || viewDetailState.loading) {
                return;
            }

            const page = Number.parseInt(button.dataset.page || "1", 10);
            if (!Number.isInteger(page) || page <= 0 || page === viewDetailState.pageNumber) {
                return;
            }

            viewDetailState.pageNumber = page;
            loadViewDetailRows();
        });

        viewDetailUseDateRange?.addEventListener("change", syncViewDetailDateRange);
        viewDetailFromDate?.addEventListener("change", syncViewDetailDateRange);
        viewDetailToDate?.addEventListener("change", syncViewDetailDateRange);

        if (window.jQuery && viewDetailModal) {
            window.jQuery(viewDetailModal).on("hidden.bs.modal", () => {
                if (viewDetailRequestNo) viewDetailRequestNo.value = "";
                if (viewDetailDescription) viewDetailDescription.value = "";
                if (viewDetailRecQty) viewDetailRecQty.value = "";
                if (viewDetailItemCode) viewDetailItemCode.value = "";
            });
        }

        document.addEventListener("prq:selection-changed", syncState);
        syncViewDetailDateRange();
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

            const files = attachmentEl.files ? Array.from(attachmentEl.files) : [];
            if (!files.length) {
                return true;
            }

            for (const file of files) {
                const fileName = String(file.name || "");
                const dotIndex = fileName.lastIndexOf(".");
                const extension = dotIndex >= 0 ? fileName.substring(dotIndex).toLowerCase() : "";
                if (allowedExtensions.length && !allowedExtensions.includes(extension)) {
                    showAttachmentError(`Attachment file type is invalid for '${fileName}'. Allowed: ${config.allowedAttachmentExtensions}`);
                    attachmentEl.value = "";
                    return false;
                }

                if (maxFileSizeBytes > 0 && file.size > maxFileSizeBytes) {
                    showAttachmentError(`Attachment '${fileName}' size cannot exceed ${config.maxAttachmentSizeMb} MB.`);
                    attachmentEl.value = "";
                    return false;
                }
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

        const syncDescriptionFromSelection = () => {
            const uniqueRequestNos = [];
            sourceRows
                .filter((row) => row.checked)
                .forEach((row) => {
                    const requestNo = String(row.requestNo || "").trim();
                    if (!requestNo) {
                        return;
                    }

                    if (!uniqueRequestNos.includes(requestNo)) {
                        uniqueRequestNos.push(requestNo);
                    }
                });

            descriptionEl.value = uniqueRequestNos.length > 0
                ? `MR ${uniqueRequestNos.join(", ")}`
                : "";
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
                tr.classList.toggle("selected", !!row.checked);
                tr.innerHTML = `
                    <td class="text-center">
                        <input type="checkbox" class="prq-add-at-check" data-index="${index}" ${row.checked ? "checked" : ""} />
                    </td>
                    <td>${escapeHtml(row.requestNo)}</td>
                    <td title="${escapeHtml(row.itemCode)}">${escapeHtml(row.itemCode)}</td>
                    <td class="prq-add-at-col-item-name"><span class="prq-add-at-ellipsis tcvn3-font" title="${escapeHtml(row.itemName)}">${escapeHtml(row.itemName)}</span></td>
                    <td class="prq-center">${formatNumber(row.buy)}</td>
                    <td class="prq-center">
                        <input type="text" inputmode="decimal" class="form-control form-control-sm prq-add-at-sugbuy" data-index="${index}" value="${formatNumber(row.sugBuy)}" />
                    </td>
                    <td class="prq-center">${escapeHtml(row.unit)}</td>
                    <td class="prq-center">${formatNumber(row.unitPrice)}</td>
                    <td class="prq-add-at-col-specification">${buildSingleLineEllipsisText(row.specification, "vni-font")}</td>
                    <td class="prq-add-at-col-note">${buildSingleLineEllipsisText(row.note, "vni-font")}</td>`;
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

        requestNoDisplayEl.addEventListener("input", syncHiddenValues);
        requestDateDisplayEl.addEventListener("change", syncHiddenValues);
        currencySelectEl.addEventListener("change", syncHiddenValues);
        attachmentEl.addEventListener("change", validateAttachment);

        rowsContainer.addEventListener("change", (ev) => {
            const check = ev.target.closest(".prq-add-at-check");
            if (check) {
                const index = Number.parseInt(check.getAttribute("data-index"), 10);
                if (Number.isInteger(index) && sourceRows[index]) {
                    sourceRows[index].checked = check.checked;
                }
                const row = check.closest("tr");
                if (row) {
                    row.classList.toggle("selected", check.checked);
                }
                syncDescriptionFromSelection();
                return;
            }

            const sugBuyInput = ev.target.closest(".prq-add-at-sugbuy");
            if (!sugBuyInput) return;

            const index = Number.parseInt(sugBuyInput.getAttribute("data-index"), 10);
            if (!Number.isInteger(index) || !sourceRows[index]) return;

            const parsedValue = clampNonNegativeDecimalInput(sugBuyInput);
            sourceRows[index].sugBuy = parsedValue;
        });

        rowsContainer.addEventListener("input", (ev) => {
            const sugBuyInput = ev.target.closest(".prq-add-at-sugbuy");
            if (!sugBuyInput) return;

            const sanitizedValue = sanitizeNonNegativeDecimal(sugBuyInput.value);
            if (sugBuyInput.value !== sanitizedValue) {
                sugBuyInput.value = sanitizedValue;
            }

            const index = Number.parseInt(sugBuyInput.getAttribute("data-index"), 10);
            if (!Number.isInteger(index) || !sourceRows[index]) return;

            sourceRows[index].sugBuy = clampNonNegativeDecimalInput(sugBuyInput);
        });

        addAtForm.addEventListener("submit", (ev) => {
            if (isLoading) {
                ev.preventDefault();
                return;
            }

            syncHiddenValues();

            if (!String(descriptionEl.value || "").trim()) {
                ev.preventDefault();
                showDangerModal("Description is required.");
                descriptionEl.focus();
                return;
            }

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

            const invalidRow = selectedRows.find((row) => row.sugBuy <= 0);
            if (invalidRow) {
                ev.preventDefault();
                showDangerModal("SugBuy must be greater than 0.");
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
