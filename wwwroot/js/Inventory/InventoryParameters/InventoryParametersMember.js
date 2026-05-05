(() => {
    "use strict";

    function initSelection() {
        const rows = Array.from(document.querySelectorAll(".ipm-row"));
        const rowChecks = Array.from(document.querySelectorAll(".ipm-selector"));
        if (!rows.length || !rowChecks.length) return;

        let selectedRow = null;

        const syncState = () => {
            selectedRow = null;
            rows.forEach((row) => {
                const check = row.querySelector(".ipm-selector");
                const isSelected = !!check && check.checked;
                row.classList.toggle("selected", isSelected);
                if (isSelected) {
                    selectedRow = row;
                }
            });
            document.dispatchEvent(new CustomEvent("ipm:selection-changed", { detail: { row: selectedRow } }));
        };

        rows.forEach((row) => {
            row.addEventListener("click", (ev) => {
                if (ev.target && ev.target.closest("input, a, button")) return;
                const check = row.querySelector(".ipm-selector");
                if (!check) return;

                const willSelect = !check.checked;
                rowChecks.forEach((item) => {
                    item.checked = false;
                });
                check.checked = willSelect;
                syncState();
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

        window.getInventoryParameterMemberSelectedRow = () => selectedRow;
        syncState();
    }

    function initPageSizeSubmit() {
        const form = document.getElementById("inventoryParametersMemberSearchForm");
        const pageSizeSelect = document.getElementById("inventoryParametersMemberPageSize");
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

    function setSelectValue(select, value) {
        if (!select) return;

        select.value = value || "";
        if (typeof window.jQuery !== "undefined" && window.jQuery.fn.select2) {
            window.jQuery(select).trigger("change");
        } else {
            select.dispatchEvent(new Event("change", { bubbles: true }));
        }
    }

    function getSelectedRow() {
        return typeof window.getInventoryParameterMemberSelectedRow === "function"
            ? window.getInventoryParameterMemberSelectedRow()
            : null;
    }

    function closeOpenSelect2() {
        if (typeof window.jQuery === "undefined" || typeof window.jQuery.fn.select2 !== "function") {
            return;
        }

        window.jQuery(".select2-hidden-accessible").select2("close");
    }

    function showModal(id) {
        const modal = document.getElementById(id);
        if (!modal || typeof window.jQuery === "undefined") return;

        closeOpenSelect2();
        window.jQuery(modal).modal("show");
    }

    function setMemberModalMode() {
        const form = document.getElementById("inventoryParameterMemberForm");
        const title = document.getElementById("inventoryParameterMemberModalTitle");
        const detailInput = document.getElementById("MemberInput_DetailID");
        const groupInput = document.getElementById("MemberInput_KPGroupID");
        const storeGroupSelectGroup = document.getElementById("ipmStoreGroupSelectGroup");
        const employeeSelect = document.getElementById("MemberInput_EmployeeID");
        const currentGroupId = form?.getAttribute("data-current-group-id") || "";

        if (title) {
            title.textContent = "Add Group Member";
        }

        if (detailInput) {
            detailInput.value = "0";
        }

        setSelectValue(groupInput, currentGroupId);
        storeGroupSelectGroup?.classList.add("d-none");
        if (groupInput) {
            groupInput.required = false;
        }

        if (employeeSelect) {
            employeeSelect.disabled = false;
            employeeSelect.required = true;
        }

        setSelectValue(employeeSelect, "");
    }

    function setDeleteModal(row) {
        const detailInput = document.getElementById("DeleteMemberInput_DetailID");
        const groupInput = document.getElementById("DeleteMemberInput_KPGroupID");
        const nameEl = document.getElementById("inventoryParameterMemberDeleteName");

        if (detailInput) {
            detailInput.value = row?.getAttribute("data-detail-id") || "";
        }

        if (groupInput) {
            groupInput.value = row?.getAttribute("data-store-group-id") || "";
        }

        if (nameEl) {
            const code = row?.getAttribute("data-employee-code") || "";
            const name = row?.getAttribute("data-employee-name") || "";
            nameEl.textContent = [code, name].filter(Boolean).join(" - ");
        }
    }

    function initActions() {
        const addBtn = document.getElementById("ipmAddBtn");
        const deleteBtn = document.getElementById("ipmDeleteBtn");
        const currentGroupId = document.getElementById("inventoryParameterMemberForm")?.getAttribute("data-current-group-id") || "";

        if (addBtn && !currentGroupId) {
            addBtn.disabled = true;
        }

        const syncButtons = () => {
            const row = getSelectedRow();
            if (deleteBtn) {
                deleteBtn.disabled = !row;
            }
        };

        addBtn?.addEventListener("click", () => {
            setMemberModalMode();
            showModal("inventoryParameterMemberModal");
        });

        deleteBtn?.addEventListener("click", () => {
            const row = getSelectedRow();
            if (!row) return;

            setDeleteModal(row);
            showModal("inventoryParameterMemberDeleteModal");
        });

        document.addEventListener("ipm:selection-changed", syncButtons);
        syncButtons();
    }

    function initSelect2(selector, options = {}) {
        if (typeof window.jQuery === "undefined" || typeof window.jQuery.fn.select2 !== "function") {
            return;
        }

        window.jQuery(selector).each((_, element) => {
            const $element = window.jQuery(element);

            if ($element.hasClass("select2-hidden-accessible")) {
                $element.select2("destroy");
            }

            const select2Options = {
                width: "100%",
                allowClear: options.allowClear !== false,
                placeholder: $element.data("placeholder") || "--- All ---"
            };

            if (options.dropdownParent) {
                select2Options.dropdownParent = window.jQuery(options.dropdownParent);
            }

            $element.select2(select2Options);
        });

        window.jQuery(selector).on("select2:open", () => {
            const searchField = document.querySelector(".select2-container--open .select2-search__field");
            if (searchField) {
                searchField.focus();
            }
        });
    }

    function initPage() {
        initSelection();
        initPageSizeSubmit();
        initSelect2("#Filter_DepartmentId");
        initSelect2("#Filter_ActiveStatus");
        initSelect2("#MemberInput_KPGroupID", { dropdownParent: "#inventoryParameterMemberModal", allowClear: false });
        initSelect2("#MemberInput_EmployeeID", { dropdownParent: "#inventoryParameterMemberModal", allowClear: false });
        initActions();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initPage);
    } else {
        initPage();
    }
})();
