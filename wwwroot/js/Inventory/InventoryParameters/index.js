(() => {
    "use strict";

    function initSelection() {
        const rows = Array.from(document.querySelectorAll(".ip-row"));
        const rowChecks = Array.from(document.querySelectorAll(".ip-selector"));
        if (!rows.length || !rowChecks.length) return;

        let selectedRow = null;

        const syncState = () => {
            selectedRow = null;
            rows.forEach((row) => {
                const check = row.querySelector(".ip-selector");
                const isSelected = !!check && check.checked;
                row.classList.toggle("selected", isSelected);
                if (isSelected) {
                    selectedRow = row;
                }
            });
            document.dispatchEvent(new CustomEvent("ip:selection-changed", { detail: { row: selectedRow } }));
        };

        rows.forEach((row) => {
            row.addEventListener("click", (ev) => {
                if (ev.target && ev.target.closest("input, a, button")) return;
                const check = row.querySelector(".ip-selector");
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

        window.getInventoryParameterSelectedRow = () => selectedRow;
        syncState();
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

    function setModalMode(mode, row) {
        const title = document.getElementById("inventoryParameterGroupModalTitle");
        const idInput = document.getElementById("GroupInput_KPGroupID");
        const nameInput = document.getElementById("GroupInput_KPGroupName");
        const departmentSelect = document.getElementById("GroupInput_DepID");
        const adminInput = document.getElementById("GroupInput_IsAdminGroup");

        if (title) {
            title.textContent = mode === "edit" ? "Edit Inventory Group" : "Add Inventory Group";
        }

        if (idInput) {
            idInput.value = mode === "edit" && row ? row.getAttribute("data-group-id") || "0" : "0";
        }

        if (nameInput) {
            nameInput.value = mode === "edit" && row ? row.getAttribute("data-group-name") || "" : "";
        }

        setSelectValue(departmentSelect, mode === "edit" && row ? row.getAttribute("data-department-id") || "" : "");

        if (adminInput) {
            adminInput.checked = mode === "edit" && row ? row.getAttribute("data-is-admin-group") === "true" : false;
        }
    }

    function showGroupModal() {
        const modal = document.getElementById("inventoryParameterGroupModal");
        if (!modal || typeof window.jQuery === "undefined") return;

        window.jQuery(modal).modal("show");
    }

    function getSelectedRow() {
        return typeof window.getInventoryParameterSelectedRow === "function"
            ? window.getInventoryParameterSelectedRow()
            : null;
    }

    function initActions() {
        const addBtn = document.getElementById("ipAddBtn");
        const editBtn = document.getElementById("ipEditBtn");
        const viewMemberBtn = document.getElementById("ipViewMemberBtn");
        const viewStoreBtn = document.getElementById("ipViewStoreBtn");
        const idInput = document.getElementById("GroupInput_KPGroupID");

        const syncButtons = () => {
            const row = getSelectedRow();
            if (editBtn) {
                editBtn.disabled = !row;
            }
            if (viewMemberBtn) {
                viewMemberBtn.disabled = !row;
            }
            if (viewStoreBtn) {
                viewStoreBtn.disabled = !row;
            }
        };

        addBtn?.addEventListener("click", () => {
            setModalMode("add", null);
            showGroupModal();
        });

        editBtn?.addEventListener("click", () => {
            const row = getSelectedRow();
            if (!row) return;

            setModalMode("edit", row);
            showGroupModal();
        });

        viewMemberBtn?.addEventListener("click", () => {
            const row = getSelectedRow();
            const url = row?.getAttribute("data-member-url");
            if (url) {
                window.location.href = url;
            }
        });

        viewStoreBtn?.addEventListener("click", () => {
            const row = getSelectedRow();
            const url = row?.getAttribute("data-store-url");
            if (url) {
                window.location.href = url;
            }
        });

        document.addEventListener("ip:selection-changed", syncButtons);
        syncButtons();

        if (idInput && Number.parseInt(idInput.value || "0", 10) > 0) {
            showGroupModal();
        }
    }

    function initPageSizeSubmit() {
        const form = document.getElementById("inventoryParametersSearchForm");
        const pageSizeSelect = document.getElementById("inventoryParametersPageSize");
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
        initSelect2("#GroupInput_DepID", { dropdownParent: "#inventoryParameterGroupModal", allowClear: false });
        initActions();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initPage);
    } else {
        initPage();
    }
})();
