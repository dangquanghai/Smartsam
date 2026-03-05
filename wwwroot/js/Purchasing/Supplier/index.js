(() => {
    "use strict";

    /**
     * Validate năm copy theo ngưỡng min/max trên input.
     */
    function validateCopyYearInput(copyYearInput, copyYearValidation, showError) {
        if (!copyYearInput) return false;

        const raw = (copyYearInput.value || "").trim();
        const min = Number(copyYearInput.getAttribute("min") || "2000");
        const max = Number(copyYearInput.getAttribute("max") || "2099");
        const year = Number(raw);
        const isValid = raw.length > 0
            && Number.isInteger(year)
            && raw.length === 4
            && year >= min
            && year <= max;

        copyYearInput.classList.toggle("is-invalid", !!showError && !isValid);
        if (copyYearValidation) {
            if (showError && !isValid) {
                copyYearValidation.textContent = `Enter a year from ${min} to ${max}.`;
                copyYearValidation.classList.remove("d-none");
                copyYearValidation.style.display = "block";
            } else {
                copyYearValidation.classList.add("d-none");
                copyYearValidation.style.display = "";
            }
        }

        return isValid;
    }

    /**
     * Đồng bộ trạng thái bật/tắt nút Copy trong modal.
     */
    function syncCopyModalButtonState(copyYearInput, copyYearValidation, copyConfirmCheckbox, copyYearSubmitBtn, showYearError = false) {
        if (!copyYearSubmitBtn || !copyConfirmCheckbox) return;
        const isYearValid = validateCopyYearInput(copyYearInput, copyYearValidation, showYearError);
        copyYearSubmitBtn.disabled = !copyConfirmCheckbox.checked || !isYearValid;
    }

    /**
     * Reload riêng panel kết quả để phân trang mượt, không refresh full page.
     */
    async function reloadSupplierResultPanel(url) {
        const panel = document.getElementById("supplierResultPanelContainer");
        if (!panel) {
            window.location.href = url;
            return;
        }

        try {
            const response = await fetch(url, {
                method: "GET",
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            if (!response.ok) {
                window.location.href = url;
                return;
            }

            const html = await response.text();
            const doc = new DOMParser().parseFromString(html, "text/html");
            const newPanel = doc.getElementById("supplierResultPanelContainer");
            if (!newPanel) {
                window.location.href = url;
                return;
            }

            panel.replaceWith(newPanel);
            if (window.history?.replaceState) {
                window.history.replaceState({}, "", url);
            }
            if (typeof window.initSupplierResultPanel === "function") {
                window.initSupplierResultPanel();
            }
        } catch {
            window.location.href = url;
        }
    }

    /**
     * Disable/enable UI control và xử lý accessibility cho anchor/button.
     */
    function setUiDisabled(btn, disabled) {
        if (!btn) return;
        if (btn.tagName === "A") {
            if (disabled) {
                btn.classList.add("disabled");
                btn.setAttribute("aria-disabled", "true");
                btn.dataset.prevTabIndex = btn.getAttribute("tabindex") || "";
                btn.setAttribute("tabindex", "-1");
            } else {
                btn.classList.remove("disabled");
                btn.setAttribute("aria-disabled", "false");
                if ((btn.dataset.prevTabIndex || "") === "") btn.removeAttribute("tabindex");
                else btn.setAttribute("tabindex", btn.dataset.prevTabIndex);
            }
            return;
        }

        btn.disabled = disabled;
        btn.setAttribute("aria-disabled", disabled ? "true" : "false");
    }

    /**
     * Bật/tắt submit button theo trạng thái chọn dòng.
     */
    function setSubmitButtonState(btn, disabled) {
        if (!btn) return;
        btn.disabled = disabled;
        if (disabled) {
            btn.setAttribute("disabled", "disabled");
            btn.setAttribute("aria-disabled", "true");
        } else {
            btn.removeAttribute("disabled");
            btn.setAttribute("aria-disabled", "false");
        }
    }

    /**
     * Lấy danh sách các row đang được check.
     */
    function getSelectedRows(rows) {
        return rows.filter((row) => {
            const checkbox = row.querySelector(".supplier-selector");
            return !!checkbox && checkbox.checked;
        });
    }

    /**
     * Khởi tạo toàn bộ hành vi cho panel kết quả (selection, paging, dblclick).
     */
    function initSupplierResultPanel() {
        const panel = document.getElementById("supplierResultPanelContainer");
        if (!panel) return;

        const rows = Array.from(panel.querySelectorAll(".supplier-row"));
        const submitBtn = panel.querySelector("#submitSupplierBtn");
        const selectedSupplierIdInput = panel.querySelector("#selectedSupplierIdInput");
        const selectedSupplierIdsCsvInput = panel.querySelector("#selectedSupplierIdsCsvInput");
        const copySelectedSupplierIdsCsvInput = document.getElementById("copySelectedSupplierIdsCsvInput");
        const addBtn = panel.querySelector("#addSupplierBtn");
        const copyBtn = panel.querySelector("#copySupplierBtn");

        /**
         * Đồng bộ hidden input + trạng thái nút theo danh sách row được chọn.
         */
        function syncSelectionState() {
            const selectedRows = getSelectedRows(rows);
            rows.forEach((row) => row.classList.toggle("selected", selectedRows.includes(row)));

            const count = selectedRows.length;
            const singleRow = count === 1 ? selectedRows[0] : null;
            const singleId = singleRow?.getAttribute("data-supplier-id") || null;
            const selectedIds = selectedRows
                .map((row) => row.getAttribute("data-supplier-id"))
                .filter((id) => !!id);

            if (selectedSupplierIdInput) selectedSupplierIdInput.value = singleId || "";
            if (selectedSupplierIdsCsvInput) selectedSupplierIdsCsvInput.value = selectedIds.join(",");
            if (copySelectedSupplierIdsCsvInput) copySelectedSupplierIdsCsvInput.value = selectedIds.join(",");

            const hasAnySelected = count > 0;
            setSubmitButtonState(submitBtn, !hasAnySelected);

            const isMultiSelectMode = count > 1;
            setUiDisabled(addBtn, isMultiSelectMode);
            setUiDisabled(copyBtn, !hasAnySelected);
        }

        window.syncSupplierSelection = syncSelectionState;
        window.toggleSupplierRowSelection = (ev, row) => {
            if (!row) return;
            if (ev?.target?.closest?.(".supplier-selector")) {
                syncSelectionState();
                return;
            }

            const checkbox = row.querySelector(".supplier-selector");
            if (!checkbox) return;
            checkbox.checked = !checkbox.checked;
            syncSelectionState();
        };

        rows.forEach((row) => {
            row.addEventListener("dblclick", () => {
                const detailUrl = row.getAttribute("data-detail-url");
                if (detailUrl) {
                    window.location.href = detailUrl;
                }
            });
        });

        panel.querySelectorAll(".supplier-pager-link").forEach((link) => {
            link.addEventListener("click", (e) => {
                const href = link.getAttribute("href");
                if (!href || href === "#" || link.closest(".page-item.disabled")) {
                    e.preventDefault();
                    return;
                }
                e.preventDefault();
                void reloadSupplierResultPanel(href);
            });
        });

        syncSelectionState();
    }

    /**
     * Bật/tắt input năm theo radio Current/By Year.
     */
    function syncYearInputState(yearInput, currentList, byYear, yearGroup) {
        if (!yearInput || !currentList || !byYear) return;
        const showYear = byYear.checked;
        yearInput.disabled = !showYear;
        if (yearGroup) {
            yearGroup.classList.toggle("show", showYear);
        }
        if (!showYear) yearInput.value = "";
    }

    /**
     * Entry point của trang Supplier Index.
     */
    function initSupplierIndexPage() {
        const yearInput = document.querySelector(".year-input");
        const currentList = document.getElementById("viewCurrent");
        const byYear = document.getElementById("viewByYear");
        const yearGroup = document.getElementById("yearGroup");
        const copyYearModal = document.getElementById("copyYearModal");
        const copyYearInput = document.getElementById("copyYearInput");
        const copyConfirmCheckbox = document.getElementById("ConfirmCopy");
        const copyYearSubmitBtn = document.getElementById("copyYearSubmitBtn");
        const copyYearValidation = document.getElementById("copyYearValidation");

        window.initSupplierResultPanel = initSupplierResultPanel;

        currentList?.addEventListener("change", () => syncYearInputState(yearInput, currentList, byYear, yearGroup));
        byYear?.addEventListener("change", () => syncYearInputState(yearInput, currentList, byYear, yearGroup));

        syncYearInputState(yearInput, currentList, byYear, yearGroup);
        initSupplierResultPanel();

        if (copyYearModal && copyYearInput && window.jQuery) {
            $(copyYearModal).on("shown.bs.modal", function () {
                if (copyYearInput.value === "0") {
                    copyYearInput.value = "";
                }
                if (copyConfirmCheckbox) {
                    copyConfirmCheckbox.checked = false;
                }
                copyYearInput.classList.remove("is-invalid");
                if (copyYearValidation) {
                    copyYearValidation.classList.add("d-none");
                    copyYearValidation.style.display = "";
                }
                syncCopyModalButtonState(copyYearInput, copyYearValidation, copyConfirmCheckbox, copyYearSubmitBtn, false);
                copyYearInput.focus();
            });

            $(copyYearModal).on("hidden.bs.modal", function () {
                if (copyConfirmCheckbox) {
                    copyConfirmCheckbox.checked = false;
                }
                copyYearInput.classList.remove("is-invalid");
                if (copyYearValidation) {
                    copyYearValidation.classList.add("d-none");
                    copyYearValidation.style.display = "";
                }
                syncCopyModalButtonState(copyYearInput, copyYearValidation, copyConfirmCheckbox, copyYearSubmitBtn, false);
            });
        }

        copyConfirmCheckbox?.addEventListener("change", () => {
            syncCopyModalButtonState(copyYearInput, copyYearValidation, copyConfirmCheckbox, copyYearSubmitBtn, false);
        });
        copyYearInput?.addEventListener("input", () => {
            syncCopyModalButtonState(copyYearInput, copyYearValidation, copyConfirmCheckbox, copyYearSubmitBtn, false);
        });
        copyYearInput?.addEventListener("blur", () => {
            syncCopyModalButtonState(copyYearInput, copyYearValidation, copyConfirmCheckbox, copyYearSubmitBtn, true);
        });
        syncCopyModalButtonState(copyYearInput, copyYearValidation, copyConfirmCheckbox, copyYearSubmitBtn, false);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initSupplierIndexPage);
    } else {
        initSupplierIndexPage();
    }
})();

