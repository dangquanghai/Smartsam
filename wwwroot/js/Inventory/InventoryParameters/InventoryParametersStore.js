(() => {
    "use strict";

    function initSelection() {
        const rows = Array.from(document.querySelectorAll(".ips-row"));
        const rowChecks = Array.from(document.querySelectorAll(".ips-selector"));
        if (!rows.length || !rowChecks.length) return;

        let selectedRow = null;

        const syncState = () => {
            selectedRow = null;
            rows.forEach((row) => {
                const check = row.querySelector(".ips-selector");
                const isSelected = !!check && check.checked;
                row.classList.toggle("selected", isSelected);
                if (isSelected) {
                    selectedRow = row;
                }
            });
            document.dispatchEvent(new CustomEvent("ips:selection-changed", { detail: { row: selectedRow } }));
        };

        rows.forEach((row) => {
            row.addEventListener("click", (ev) => {
                if (ev.target && ev.target.closest("input, a, button")) return;
                const check = row.querySelector(".ips-selector");
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

        window.getInventoryParameterStoreSelectedRow = () => selectedRow;
        syncState();
    }

    function initPageSizeSubmit() {
        const form = document.getElementById("inventoryParametersStoreSearchForm");
        const pageSizeSelect = document.getElementById("inventoryParametersStorePageSize");
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
        return typeof window.getInventoryParameterStoreSelectedRow === "function"
            ? window.getInventoryParameterStoreSelectedRow()
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

    function setStoreModalMode(mode, row) {
        const form = document.getElementById("inventoryParameterStoreForm");
        const title = document.getElementById("inventoryParameterStoreModalTitle");
        const storeIdInput = document.getElementById("StoreInput_StoreID");
        const storeNameInput = document.getElementById("StoreInput_StoreName");
        const addressInput = document.getElementById("StoreInput_Address");
        const groupSelect = document.getElementById("StoreInput_KPGroupID");
        const storeGroupSelectGroup = document.getElementById("ipsStoreGroupSelectGroup");
        const currentGroupId = form?.getAttribute("data-current-group-id") || "";
        const isEdit = mode === "edit" && !!row;

        if (title) {
            title.textContent = mode === "edit" ? "Edit Inventory Store" : "Add Inventory Store";
        }

        if (storeIdInput) {
            storeIdInput.value = isEdit ? row.getAttribute("data-store-id") || "0" : "0";
        }

        if (storeNameInput) {
            storeNameInput.value = isEdit ? row.getAttribute("data-store-name") || "" : "";
        }

        if (addressInput) {
            addressInput.value = isEdit ? row.getAttribute("data-address") || "" : "";
        }

        setSelectValue(groupSelect, isEdit ? row.getAttribute("data-store-group-id") || currentGroupId : currentGroupId);
        storeGroupSelectGroup?.classList.add("d-none");
        if (groupSelect) {
            groupSelect.required = false;
        }
    }

    function setDeleteModal(row) {
        const storeIdInput = document.getElementById("DeleteStoreInput_StoreID");
        const groupInput = document.getElementById("DeleteStoreInput_KPGroupID");
        const nameEl = document.getElementById("inventoryParameterStoreDeleteName");

        if (storeIdInput) {
            storeIdInput.value = row?.getAttribute("data-store-id") || "";
        }

        if (groupInput) {
            groupInput.value = row?.getAttribute("data-store-group-id") || "";
        }

        if (nameEl) {
            nameEl.textContent = row?.getAttribute("data-store-name") || "";
        }
    }

    function initActions() {
        const addBtn = document.getElementById("ipsAddBtn");
        const editBtn = document.getElementById("ipsEditBtn");
        const deleteBtn = document.getElementById("ipsDeleteBtn");
        const storeIdInput = document.getElementById("StoreInput_StoreID");
        const currentGroupId = document.getElementById("inventoryParameterStoreForm")?.getAttribute("data-current-group-id") || "";

        if (addBtn && !currentGroupId) {
            addBtn.disabled = true;
        }

        const syncButtons = () => {
            const row = getSelectedRow();
            if (editBtn) {
                editBtn.disabled = !row;
            }
            if (deleteBtn) {
                deleteBtn.disabled = !row;
            }
        };

        addBtn?.addEventListener("click", () => {
            setStoreModalMode("add", null);
            showModal("inventoryParameterStoreModal");
        });

        editBtn?.addEventListener("click", () => {
            const row = getSelectedRow();
            if (!row) return;

            setStoreModalMode("edit", row);
            showModal("inventoryParameterStoreModal");
        });

        deleteBtn?.addEventListener("click", () => {
            const row = getSelectedRow();
            if (!row) return;

            setDeleteModal(row);
            showModal("inventoryParameterStoreDeleteModal");
        });

        document.addEventListener("ips:selection-changed", syncButtons);
        syncButtons();

        const storeId = storeIdInput ? Number.parseInt(storeIdInput.value || "0", 10) : 0;
        if (storeId > 0) {
            const editRow = Array.from(document.querySelectorAll(".ips-row"))
                .find((row) => Number.parseInt(row.getAttribute("data-store-id") || "0", 10) === storeId);
            setStoreModalMode("edit", editRow || null);
            showModal("inventoryParameterStoreModal");
        }
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
                placeholder: $element.data("placeholder") || "Select group"
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
        initSelect2("#StoreInput_KPGroupID", { dropdownParent: "#inventoryParameterStoreModal", allowClear: false });
        initActions();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initPage);
    } else {
        initPage();
    }
})();
