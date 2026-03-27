(() => {
    "use strict";

    function getConfig() {
        return window.purchaseRequisitionPage || {
            canEdit: false,
            canViewDetail: false,
            canAddAt: false,
            canDisapproval: false,
            detailUrlBase: ""
        };
    }

    function toNumber(value) {
        const n = Number.parseFloat(value);
        return Number.isFinite(n) ? n : 0;
    }

    function formatNumber(value) {
        return toNumber(value).toLocaleString("en-US", { minimumFractionDigits: 0, maximumFractionDigits: 2 });
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
                rowChecks.forEach((item) => { item.checked = false; });
                check.checked = true;
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

        rowChecks.forEach((check) => check.addEventListener("change", syncState));
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
        const btnPrintList = document.getElementById("btnPrintList");
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

        btnPrintList?.addEventListener("click", () => window.print());
        btnExportExcel?.addEventListener("click", () => alert("Export Excel is not implemented yet."));
        btnDisapproval?.addEventListener("click", () => alert("Disapproval is not implemented yet."));

        document.addEventListener("prq:selection-changed", syncState);
        syncState();
    }

    function initAddAt() {
        const addAtBtn = document.getElementById("prqAddAtBtn");
        const addAtModal = document.getElementById("prqAddAtModal");
        const requestNoEl = document.getElementById("prqAddAtRequestNo");
        const selectedPrIdEl = document.getElementById("SelectedPrId");
        const detailsJsonEl = document.getElementById("AddAtDetailsJson");
        const addAtForm = document.getElementById("prqAddAtForm");
        const rowsContainer = document.getElementById("prqAddAtDetailRows");
        const emptyRow = document.getElementById("prqAddAtDetailEmptyRow");
        const detailModal = document.getElementById("prqAddAtDetailModal");
        const addDetailBtn = document.getElementById("btnAddAtDetailConfirm");
        const itemSelect = document.getElementById("addAtDetailItemId");
        const unitInput = document.getElementById("addAtDetailUnit");
        const supplierSelect = document.getElementById("addAtDetailSupplierId");
        const qtyFromM = document.getElementById("addAtDetailQtyFromM");
        const qtyPur = document.getElementById("addAtDetailQtyPur");
        const unitPrice = document.getElementById("addAtDetailUnitPrice");
        const amountInput = document.getElementById("addAtDetailAmount");
        const remarkInput = document.getElementById("addAtDetailRemark");
        if (!addAtBtn || !addAtModal || !requestNoEl || !selectedPrIdEl || !detailsJsonEl || !addAtForm || !rowsContainer || !emptyRow) return;

        const details = [];
        const getSelectedRows = () => typeof window.getPrqSelectedRows === "function"
            ? window.getPrqSelectedRows()
            : Array.from(document.querySelectorAll(".prq-row")).filter((row) => row.querySelector(".prq-selector")?.checked);

        const updateAddAtButtonState = () => {
            addAtBtn.disabled = getSelectedRows().length !== 1 || !getConfig().canAddAt;
        };

        const syncHiddenInput = () => {
            detailsJsonEl.value = JSON.stringify(details.map((x) => ({
                itemId: x.itemId,
                itemCode: x.itemCode,
                itemName: x.itemName,
                unit: x.unit,
                qtyFromM: x.qtyFromM,
                qtyPur: x.qtyPur,
                unitPrice: x.unitPrice,
                remark: x.remark,
                supplierId: x.supplierId,
                supplierText: x.supplierText
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
                const tr = document.createElement("tr");
                tr.dataset.row = "1";
                tr.innerHTML = `
                    <td>${d.itemCode}</td>
                    <td>${d.itemName}</td>
                    <td class="prq-center">${d.unit}</td>
                    <td class="prq-center">${formatNumber(d.qtyFromM)}</td>
                    <td class="prq-center">${formatNumber(d.qtyPur)}</td>
                    <td class="prq-center">${formatNumber(d.unitPrice)}</td>
                    <td class="prq-center">${formatNumber(d.qtyPur * d.unitPrice)}</td>
                    <td class="prq-center">${d.remark || ""}</td>
                    <td>${d.supplierText || ""}</td>
                    <td class="text-center"><button type="button" class="btn btn-xs btn-outline-danger border" data-remove-index="${index}">X</button></td>`;
                rowsContainer.appendChild(tr);
            });

            syncHiddenInput();
        };

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

        addAtBtn.addEventListener("click", () => {
            const selectedRows = getSelectedRows();
            if (selectedRows.length !== 1) return;
            const selectedRow = selectedRows[0];
            selectedPrIdEl.value = selectedRow.getAttribute("data-prid") || "";
            requestNoEl.textContent = selectedRow.getAttribute("data-request-no") || "-";
            details.length = 0;
            renderRows();
            resetDetailFields();
        });

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

        addAtForm.addEventListener("submit", (ev) => {
            const selectedRows = getSelectedRows();
            if (selectedRows.length !== 1) {
                ev.preventDefault();
                alert("Please select exactly one requisition row.");
                return;
            }
            if (details.length === 0) {
                ev.preventDefault();
                alert("Please add at least one detail row.");
                return;
            }
            selectedPrIdEl.value = selectedRows[0].getAttribute("data-prid") || "";
            syncHiddenInput();
        });

        if (window.jQuery) {
            window.jQuery(addAtModal).on("hidden.bs.modal", () => {
                details.length = 0;
                renderRows();
                resetDetailFields();
            });
        }

        document.addEventListener("prq:selection-changed", updateAddAtButtonState);
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
