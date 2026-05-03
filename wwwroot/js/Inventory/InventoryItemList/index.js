(() => {
    "use strict";

    function initSelection() {
        const rows = Array.from(document.querySelectorAll(".iil-row"));
        const rowChecks = Array.from(document.querySelectorAll(".iil-selector"));
        if (!rows.length || !rowChecks.length) return;

        let selectedRow = null;

        const syncState = () => {
            selectedRow = null;
            rows.forEach((row) => {
                const check = row.querySelector(".iil-selector");
                const isSelected = !!check && check.checked;
                row.classList.toggle("selected", isSelected);
                if (isSelected) {
                    selectedRow = row;
                }
            });
            document.dispatchEvent(new CustomEvent("iil:selection-changed", { detail: { row: selectedRow } }));
        };

        rows.forEach((row) => {
            row.addEventListener("click", (ev) => {
                if (ev.target && ev.target.closest("input, a, button")) return;
                const check = row.querySelector(".iil-selector");
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

        window.getInventoryItemListSelectedRow = () => selectedRow;
        syncState();
    }

    function initPageSizeSubmit() {
        const form = document.getElementById("inventoryItemListSearchForm");
        const pageSizeSelect = document.getElementById("inventoryItemListPageSize");
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

    function initToggleableSpecialViewRadios() {
        const radios = Array.from(document.querySelectorAll(".iil-special-view-radio"));
        if (!radios.length) return;

        radios.forEach((radio) => {
            const label = radio.id ? document.querySelector(`label[for="${radio.id}"]`) : null;
            let wasCheckedBeforeClick = false;

            const rememberState = () => {
                wasCheckedBeforeClick = radio.checked;
            };

            const clearSelectedRadio = () => {
                window.setTimeout(() => {
                    radio.checked = false;
                    wasCheckedBeforeClick = false;
                    radio.dispatchEvent(new Event("change", { bubbles: true }));
                }, 0);
            };

            const clearIfAlreadySelected = (ev) => {
                if (!wasCheckedBeforeClick) {
                    return;
                }

                ev.preventDefault();
                ev.stopPropagation();
                clearSelectedRadio();
            };

            radio.addEventListener("pointerdown", rememberState);
            label?.addEventListener("pointerdown", rememberState);
            radio.addEventListener("mousedown", rememberState);
            label?.addEventListener("mousedown", rememberState);
            radio.addEventListener("click", clearIfAlreadySelected, true);
            label?.addEventListener("click", clearIfAlreadySelected, true);

            radio.addEventListener("change", () => {
                if (!radio.checked) return;

                radios.forEach((item) => {
                    if (item !== radio) {
                        item.checked = false;
                    }
                });
            });

            radio.addEventListener("keydown", (ev) => {
                if ((ev.key !== " " && ev.key !== "Spacebar") || !radio.checked) {
                    return;
                }

                ev.preventDefault();
                radio.checked = false;
                radio.dispatchEvent(new Event("change", { bubbles: true }));
            });
        });
    }

    function sanitizeNonNegativeNumber(input) {
        if (!input) return;

        const rawValue = input.value.trim();
        if (!rawValue) {
            input.setCustomValidity("");
            return;
        }

        const numericValue = Number(rawValue);
        if (rawValue.includes("-") || Number.isNaN(numericValue) || numericValue < 0) {
            input.value = "";
        }

        input.setCustomValidity("");
    }

    function initUnitPriceValidation() {
        const input = document.getElementById("ItemInput_UnitPrice");
        const form = document.getElementById("inventoryItemForm");
        if (!input) return;

        input.addEventListener("keydown", (ev) => {
            if (ev.key === "-" || ev.key === "Subtract") {
                ev.preventDefault();
            }
        });

        input.addEventListener("beforeinput", (ev) => {
            if (typeof ev.data === "string" && ev.data.includes("-")) {
                ev.preventDefault();
            }
        });

        input.addEventListener("input", () => {
            sanitizeNonNegativeNumber(input);
        });

        form?.addEventListener("submit", (ev) => {
            const rawValue = input.value.trim();
            const numericValue = Number(rawValue);
            if (rawValue && (rawValue.includes("-") || Number.isNaN(numericValue) || numericValue < 0)) {
                ev.preventDefault();
                input.setCustomValidity("Unit Price cannot be negative.");
                input.reportValidity();
                return;
            }

            input.setCustomValidity("");
        });
    }

    function setSelectValue(select, value, fallbackText) {
        if (!select) return;

        const normalizedValue = value || "";
        if (normalizedValue && !Array.from(select.options).some((option) => option.value === normalizedValue)) {
            select.add(new Option(fallbackText || `#${normalizedValue}`, normalizedValue));
        }

        select.value = normalizedValue;
        if (typeof window.jQuery !== "undefined" && window.jQuery.fn.select2) {
            window.jQuery(select).trigger("change");
        } else {
            select.dispatchEvent(new Event("change", { bubbles: true }));
        }
    }

    function setChecked(input, value) {
        if (input) {
            input.checked = value === true || value === "true";
        }
    }

    function getSelectedRow() {
        return typeof window.getInventoryItemListSelectedRow === "function"
            ? window.getInventoryItemListSelectedRow()
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

    function toggleParentInput() {
        const isSubItemInput = document.getElementById("ItemInput_IsSubItem");
        const parentInput = document.getElementById("ItemInput_ParentItemCode");
        if (!isSubItemInput || !parentInput) return;

        parentInput.required = isSubItemInput.checked;
        parentInput.closest(".form-group")?.classList.toggle("iil-parent-required", isSubItemInput.checked);
    }

    function setItemModalMode(mode, row) {
        const title = document.getElementById("inventoryItemModalTitle");
        const itemIdInput = document.getElementById("ItemInput_ItemID");
        const itemCodeInput = document.getElementById("ItemInput_ItemCode");
        const itemNameInput = document.getElementById("ItemInput_ItemName");
        const categorySelect = document.getElementById("ItemInput_ItemCatg");
        const unitInput = document.getElementById("ItemInput_Unit");
        const unitPriceInput = document.getElementById("ItemInput_UnitPrice");
        const currencySelect = document.getElementById("ItemInput_Currency");
        const specificationInput = document.getElementById("ItemInput_Specification");
        const groupSelect = document.getElementById("ItemInput_KPGroupItem");
        const reorderInput = document.getElementById("ItemInput_ReOrderPoint");
        const parentInput = document.getElementById("ItemInput_ParentItemCode");
        const isSubItemInput = document.getElementById("ItemInput_IsSubItem");
        const isApartmentInput = document.getElementById("ItemInput_IsApartment");
        const isStockInput = document.getElementById("ItemInput_IsStock");
        const isFixAssetInput = document.getElementById("ItemInput_IsFixAsset");
        const isMaterialInput = document.getElementById("ItemInput_IsMaterial");
        const isPurchaseInput = document.getElementById("ItemInput_IsPurchase");
        const isActiveInput = document.getElementById("ItemInput_IsActive");
        const isNewItemInput = document.getElementById("ItemInput_IsNewItem");
        const isEdit = mode === "edit" && !!row;

        if (title) {
            title.textContent = isEdit ? "Edit Inventory Item" : "Add Inventory Item";
        }

        if (itemIdInput) {
            itemIdInput.value = isEdit ? row.getAttribute("data-item-id") || "0" : "0";
        }
        if (itemCodeInput) {
            itemCodeInput.value = isEdit ? row.getAttribute("data-item-code") || "" : "";
        }
        if (itemNameInput) {
            itemNameInput.value = isEdit ? row.getAttribute("data-item-name") || "" : "";
        }
        setSelectValue(
            categorySelect,
            isEdit ? row.getAttribute("data-item-catg") || "0" : "0",
            isEdit ? row.getAttribute("data-item-catg-text") || "" : ""
        );
        if (unitInput) {
            unitInput.value = isEdit ? row.getAttribute("data-unit") || "" : "";
        }
        if (unitPriceInput) {
            unitPriceInput.value = isEdit ? row.getAttribute("data-unit-price") || "" : "";
            sanitizeNonNegativeNumber(unitPriceInput);
        }
        setSelectValue(
            currencySelect,
            isEdit ? row.getAttribute("data-currency") || "" : "",
            isEdit ? row.getAttribute("data-currency-text") || "" : ""
        );
        if (specificationInput) {
            specificationInput.value = isEdit ? row.getAttribute("data-specification") || "" : "";
        }
        setSelectValue(
            groupSelect,
            isEdit ? row.getAttribute("data-kp-group-item") || "" : "",
            isEdit ? row.getAttribute("data-kp-group-text") || "" : ""
        );
        if (reorderInput) {
            reorderInput.value = isEdit ? row.getAttribute("data-reorder-point") || "" : "";
        }
        if (parentInput) {
            parentInput.value = isEdit ? row.getAttribute("data-parent-item-code") || "" : "";
        }

        setChecked(isSubItemInput, isEdit ? row.getAttribute("data-is-sub-item") : false);
        setChecked(isApartmentInput, isEdit ? row.getAttribute("data-is-apartment") : false);
        setChecked(isStockInput, isEdit ? row.getAttribute("data-is-stock") : true);
        setChecked(isFixAssetInput, isEdit ? row.getAttribute("data-is-fix-asset") : false);
        setChecked(isMaterialInput, isEdit ? row.getAttribute("data-is-material") : true);
        setChecked(isPurchaseInput, isEdit ? row.getAttribute("data-is-purchase") : true);
        setChecked(isActiveInput, isEdit ? row.getAttribute("data-is-active") : true);
        setChecked(isNewItemInput, isEdit ? row.getAttribute("data-is-new-item") : false);
        toggleParentInput();
    }

    function setDeleteModal(row) {
        const itemIdInput = document.getElementById("DeleteItemInput_ItemID");
        const nameEl = document.getElementById("inventoryItemDeleteName");

        if (itemIdInput) {
            itemIdInput.value = row?.getAttribute("data-item-id") || "";
        }

        if (nameEl) {
            const code = row?.getAttribute("data-item-code") || "";
            const name = row?.getAttribute("data-item-name") || "";
            nameEl.textContent = code ? `${code} - ${name}` : name;
        }
    }

    function getValue(source, camelName, pascalName) {
        if (!source) return null;
        if (Object.prototype.hasOwnProperty.call(source, camelName)) {
            return source[camelName];
        }
        return source[pascalName];
    }

    function setText(id, value) {
        const element = document.getElementById(id);
        if (element) {
            element.textContent = value ?? "";
        }
    }

    function formatStockNumber(value) {
        const numericValue = Number(value);
        if (Number.isNaN(numericValue)) {
            return "";
        }

        return numericValue.toLocaleString(undefined, {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        });
    }

    function resetStockModal() {
        const loading = document.getElementById("inventoryItemStockLoading");
        const error = document.getElementById("inventoryItemStockError");
        const content = document.getElementById("inventoryItemStockContent");
        const body = document.getElementById("inventoryItemStockStoreBody");

        loading?.classList.remove("d-none");
        error?.classList.add("d-none");
        content?.classList.add("d-none");
        if (error) {
            error.textContent = "";
        }
        if (body) {
            body.innerHTML = "";
        }
        setText("inventoryItemStockCode", "");
        setText("inventoryItemStockName", "");
        setText("inventoryItemStockMainInventory", "");
        setText("inventoryItemStockUnit", "");
    }

    function showStockError(message) {
        const loading = document.getElementById("inventoryItemStockLoading");
        const error = document.getElementById("inventoryItemStockError");
        const content = document.getElementById("inventoryItemStockContent");

        loading?.classList.add("d-none");
        content?.classList.add("d-none");
        if (error) {
            error.textContent = message || "Cannot load stock data.";
            error.classList.remove("d-none");
        }
    }

    function appendStockCell(row, value, className) {
        const cell = document.createElement("td");
        if (className) {
            cell.className = className;
        }
        cell.textContent = value ?? "";
        row.appendChild(cell);
    }

    function renderStockInfo(item) {
        const loading = document.getElementById("inventoryItemStockLoading");
        const content = document.getElementById("inventoryItemStockContent");
        const body = document.getElementById("inventoryItemStockStoreBody");

        const itemCode = getValue(item, "itemCode", "ItemCode") || "";
        const itemName = getValue(item, "itemName", "ItemName") || "";
        const unit = getValue(item, "unit", "Unit") || "";
        const mainInventory = getValue(item, "mainInventory", "MainInventory");
        const storeBalances = getValue(item, "storeBalances", "StoreBalances") || [];

        setText("inventoryItemStockCode", itemCode);
        setText("inventoryItemStockName", itemName ? ` - ${itemName}` : "");
        setText("inventoryItemStockMainInventory", formatStockNumber(mainInventory));
        setText("inventoryItemStockUnit", unit);

        if (body) {
            body.innerHTML = "";

            if (!storeBalances.length) {
                const row = document.createElement("tr");
                const cell = document.createElement("td");
                cell.colSpan = 5;
                cell.className = "text-center text-muted";
                cell.textContent = "No store balance data";
                row.appendChild(cell);
                body.appendChild(row);
            } else {
                storeBalances.forEach((balance) => {
                    const row = document.createElement("tr");
                    appendStockCell(row, getValue(balance, "year", "Year"));
                    appendStockCell(row, getValue(balance, "storeName", "StoreName"));
                    appendStockCell(row, formatStockNumber(getValue(balance, "bgQuantity", "BGQuantity")), "text-right");
                    appendStockCell(row, formatStockNumber(getValue(balance, "bgAmount", "BGAmount")), "text-right");
                    appendStockCell(row, getValue(balance, "currencyName", "CurrencyName"));
                    body.appendChild(row);
                });
            }
        }

        loading?.classList.add("d-none");
        content?.classList.remove("d-none");
    }

    async function showStockInfo(row) {
        const itemId = row?.getAttribute("data-item-id");
        if (!itemId) return;

        resetStockModal();
        showModal("inventoryItemStockModal");

        const url = new URL(window.location.pathname, window.location.origin);
        url.searchParams.set("handler", "StockInfo");
        url.searchParams.set("itemId", itemId);

        try {
            const response = await fetch(url.toString(), {
                headers: { Accept: "application/json" },
                credentials: "same-origin"
            });
            const data = await response.json();

            if (!response.ok || data.success === false || data.Success === false) {
                showStockError(data.message || data.Message);
                return;
            }

            renderStockInfo(data.item || data.Item);
        } catch {
            showStockError("Cannot load stock data.");
        }
    }

    function initActions() {
        const addBtn = document.getElementById("iilAddBtn");
        const editBtn = document.getElementById("iilEditBtn");
        const deleteBtn = document.getElementById("iilDeleteBtn");
        const stockBtn = document.getElementById("iilStockBtn");
        const isSubItemInput = document.getElementById("ItemInput_IsSubItem");

        const syncButtons = () => {
            const row = getSelectedRow();
            if (editBtn) {
                editBtn.disabled = !row;
            }
            if (deleteBtn) {
                deleteBtn.disabled = !row;
            }
            if (stockBtn) {
                stockBtn.disabled = !row;
            }
        };

        addBtn?.addEventListener("click", () => {
            setItemModalMode("add", null);
            showModal("inventoryItemModal");
        });

        editBtn?.addEventListener("click", () => {
            const row = getSelectedRow();
            if (!row) return;

            setItemModalMode("edit", row);
            showModal("inventoryItemModal");
        });

        deleteBtn?.addEventListener("click", () => {
            const row = getSelectedRow();
            if (!row) return;

            setDeleteModal(row);
            showModal("inventoryItemDeleteModal");
        });

        stockBtn?.addEventListener("click", () => {
            showStockInfo(getSelectedRow());
        });

        isSubItemInput?.addEventListener("change", toggleParentInput);
        document.addEventListener("iil:selection-changed", syncButtons);
        syncButtons();
    }

    function initRecallLostModal() {
        const openBtn = document.getElementById("iilRecallLostOpenBtn");
        const modal = document.getElementById("inventoryItemRecallLostModal");
        const form = document.getElementById("inventoryItemRecallForm");
        const searchInput = document.getElementById("inventoryItemRecallSearch");
        const codeInput = document.getElementById("RecallItemCode");
        const submitBtn = document.getElementById("iilRecallLostSubmitBtn");
        const selectionText = document.getElementById("inventoryItemRecallSelection");
        const rows = Array.from(document.querySelectorAll(".iil-recall-row"));
        const selectors = Array.from(document.querySelectorAll(".iil-recall-selector"));
        if (!openBtn || !modal || !form) return;

        const syncSelection = () => {
            const selected = rows.find((row) => row.querySelector(".iil-recall-selector")?.checked);
            const itemCode = selected?.getAttribute("data-item-code") || "";
            const itemName = selected?.getAttribute("data-item-name") || "";

            rows.forEach((row) => {
                row.classList.toggle("selected", row === selected);
            });

            if (codeInput) {
                codeInput.value = itemCode;
            }
            if (submitBtn) {
                submitBtn.disabled = !itemCode;
            }
            if (selectionText) {
                selectionText.textContent = itemCode ? `Selected: ${itemCode} - ${itemName}` : "No item selected";
                selectionText.classList.toggle("text-danger", false);
                selectionText.classList.toggle("text-muted", true);
            }
        };

        rows.forEach((row) => {
            row.addEventListener("click", (ev) => {
                if (ev.target && ev.target.closest("a, button")) return;

                const selector = row.querySelector(".iil-recall-selector");
                if (!selector) return;

                selectors.forEach((item) => {
                    item.checked = false;
                });
                selector.checked = true;
                syncSelection();
            });
        });

        selectors.forEach((selector) => {
            selector.addEventListener("change", () => {
                if (selector.checked) {
                    selectors.forEach((item) => {
                        if (item !== selector) {
                            item.checked = false;
                        }
                    });
                }
                syncSelection();
            });
        });

        searchInput?.addEventListener("input", () => {
            const term = searchInput.value.trim().toLowerCase();
            rows.forEach((row) => {
                const haystack = row.getAttribute("data-search") || "";
                row.classList.toggle("d-none", !!term && !haystack.includes(term));
            });
        });

        openBtn.addEventListener("click", () => {
            showModal("inventoryItemRecallLostModal");
            window.setTimeout(() => searchInput?.focus(), 150);
        });

        form.addEventListener("submit", (ev) => {
            if (codeInput?.value) return;

            ev.preventDefault();
            if (selectionText) {
                selectionText.textContent = "Please select a lost item.";
                selectionText.classList.remove("text-muted");
                selectionText.classList.add("text-danger");
            }
        });

        syncSelection();
    }

    function initStockModal() {
        const modal = document.getElementById("inventoryItemStockModal");
        if (!modal || modal.getAttribute("data-show-stock") !== "true") return;

        showModal("inventoryItemStockModal");
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
                placeholder: $element.data("placeholder") || "---"
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
        initToggleableSpecialViewRadios();
        initUnitPriceValidation();
        initSelect2("#Filter_ItemCatg");
        initSelect2("#Filter_KPGroupItem");
        initSelect2("#Filter_ActiveStatus");
        initSelect2("#ItemInput_ItemCatg", { dropdownParent: "#inventoryItemModal", allowClear: false });
        initSelect2("#ItemInput_KPGroupItem", { dropdownParent: "#inventoryItemModal" });
        initSelect2("#ItemInput_Currency", { dropdownParent: "#inventoryItemModal" });
        initActions();
        initRecallLostModal();
        initStockModal();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initPage);
    } else {
        initPage();
    }
})();
