(() => {
    "use strict";

    /**
     * Đồng bộ trạng thái chọn row ở grid list.
     */
    /**
     * Hiá»ƒn thá»‹ summary cho Status checkbox multiselect.
     */
    function initStatusCheckboxDropdown() {
        const dropdownBtn = document.getElementById("mrStatusDropdownBtn");
        const menu = document.querySelector(".mr-status-menu");
        const checkboxes = Array.from(document.querySelectorAll(".mr-status-checkbox"));
        if (!dropdownBtn || !menu || checkboxes.length === 0) return;

        const updateCaption = () => {
            const selected = checkboxes
                .filter((x) => x.checked)
                .map((x) => x.getAttribute("data-label") || "")
                .filter((x) => x.length > 0);

            const fullCaption = selected.join("; ");
            dropdownBtn.setAttribute("title", fullCaption || "All status");

            if (selected.length === 0) {
                dropdownBtn.textContent = "All status";
                return;
            }

            if (selected.length === 1) {
                dropdownBtn.textContent = selected[0];
                return;
            }

            dropdownBtn.textContent = `${selected.length} statuses selected`;
        };

        menu.addEventListener("click", (event) => {
            event.stopPropagation();
        });

        checkboxes.forEach((checkbox) => {
            checkbox.addEventListener("change", updateCaption);
        });

        updateCaption();
    }

    /**
     * Chỉ cho phép chọn 1 group điều kiện search.
     * Group không được chọn sẽ disable toàn bộ control bên trong.
     */
    function initConditionModeSwitcher() {
        const radios = Array.from(document.querySelectorAll('input[name="ConditionMode"]'));
        const groups = Array.from(document.querySelectorAll("[data-condition-group]"));
        if (radios.length === 0 || groups.length === 0) return;

        const applyMode = () => {
            const selectedMode = radios.find((x) => x.checked)?.value || "allUsers";
            groups.forEach((group) => {
                const groupMode = group.getAttribute("data-condition-group") || "";
                const enabled = groupMode === selectedMode;
                group.classList.toggle("mr-condition-group-disabled", !enabled);

                const controls = group.querySelectorAll("input, select, textarea, button");
                controls.forEach((ctrl) => {
                    if (ctrl.name === "ConditionMode") return;
                    ctrl.disabled = !enabled;
                });
            });
        };

        radios.forEach((radio) => {
            radio.addEventListener("change", applyMode);
        });

        applyMode();
    }

    /**
     * Render data cho table search item trong popup Create MR.
     */
    function renderSearchRows(body, items) {
        body.innerHTML = "";
        if (!items || items.length === 0) {
            body.innerHTML = '<tr><td colspan="4" class="text-center text-muted">No data</td></tr>';
            return;
        }

        items.forEach((item) => {
            const tr = document.createElement("tr");
            tr.dataset.itemCode = item.itemCode || "";
            tr.dataset.itemName = item.itemName || "";
            tr.dataset.unit = item.unit || "";
            tr.dataset.inStock = item.inStock || 0;
            tr.dataset.storeGroupId = item.storeGroupId || 0;
            tr.innerHTML = `
                <td><input type="checkbox" class="create-mr-search-checkbox" /></td>
                <td>${item.itemCode || ""}</td>
                <td>${item.itemName || ""}</td>
                <td>${item.unit || ""}</td>
            `;
            body.appendChild(tr);
        });
    }

    /**
     * Render list item đã chọn trong popup Create MR.
     */
    function renderSelectedRows(body, selectedItems) {
        body.innerHTML = "";
        if (!selectedItems || selectedItems.length === 0) {
            body.innerHTML = '<tr><td colspan="4" class="text-center text-muted">No selected item</td></tr>';
            return;
        }

        selectedItems.forEach((item) => {
            const tr = document.createElement("tr");
            tr.dataset.itemCode = item.itemCode;
            tr.innerHTML = `
                <td><input type="checkbox" class="create-mr-selected-checkbox" /></td>
                <td>${item.itemCode || ""}</td>
                <td>${item.itemName || ""}</td>
                <td>${item.unit || ""}</td>
            `;
            body.appendChild(tr);
        });
    }

    /**
     * Gọi handler SearchItems ở Index để tìm item.
     */
    async function searchItems(keyword, checkBalanceInStore, storeGroup) {
        const url = new URL(window.location.href);
        url.searchParams.set("handler", "SearchItems");
        if (keyword) {
            url.searchParams.set("keyword", keyword);
        }
        if (checkBalanceInStore) {
            url.searchParams.set("checkBalanceInStore", "true");
        }
        if (storeGroup !== null && storeGroup !== undefined && Number.isFinite(storeGroup)) {
            url.searchParams.set("storeGroup", String(storeGroup));
        }

        const response = await fetch(url.toString(), {
            method: "GET",
            headers: { "X-Requested-With": "XMLHttpRequest" }
        });

        if (!response.ok) {
            throw new Error("Cannot load items.");
        }

        const json = await response.json();
        if (!json.success) {
            throw new Error(json.message || "Cannot load items.");
        }

        return json.data || [];
    }

    /**
     * Khởi tạo popup Create MR theo flow tài liệu:
     * Search -> chọn item -> '>' -> nhập Description -> Create MR.
     */
    function initCreateMrPopup() {
        const openBtn = document.getElementById("openCreateMrPopupBtn");
        const modal = document.getElementById("createMrModal");
        const searchBtn = document.getElementById("createMrSearchBtn");
        const moveRightBtn = document.getElementById("createMrMoveRightBtn");
        const moveLeftBtn = document.getElementById("createMrMoveLeftBtn");
        const confirmBtn = document.getElementById("createMrConfirmBtn");
        const keywordInput = document.getElementById("createMrKeyword");
        const checkBalanceInput = document.getElementById("createMrCheckBalance");
        const searchBody = document.getElementById("createMrSearchResultBody");
        const selectedBody = document.getElementById("createMrSelectedBody");
        const descriptionInput = document.getElementById("createMrDescription");
        const descriptionPostInput = document.getElementById("createMrDescriptionInput");
        const linesJsonInput = document.getElementById("createMrLinesJsonInput");
        const storeGroupPostInput = document.getElementById("createMrStoreGroupInput");
        const validation = document.getElementById("createMrValidation");
        if (!openBtn || !modal || !searchBody || !selectedBody) return;

        const selectedMap = new Map();

        const getCurrentStoreGroupValue = () => {
            const storeGroupSelect = document.getElementById("StoreGroup");
            if (!storeGroupSelect) {
                return null;
            }

            const raw = (storeGroupSelect.value || "").trim();
            if (!raw) {
                return null;
            }

            const parsed = Number.parseInt(raw, 10);
            return Number.isFinite(parsed) ? parsed : null;
        };

        const showValidation = (message) => {
            if (!validation) return;
            validation.textContent = message || "";
            validation.classList.toggle("d-none", !message);
        };

        const syncConfirmState = () => {
            if (!confirmBtn) return;
            const hasItems = selectedMap.size > 0;
            const hasDescription = ((descriptionInput?.value || "").trim().length > 0);
            confirmBtn.disabled = !(hasItems && hasDescription);
        };

        const redrawSelected = () => {
            renderSelectedRows(selectedBody, Array.from(selectedMap.values()));
            syncConfirmState();
        };

        openBtn.addEventListener("click", async () => {
            if (typeof window.$ === "function") {
                window.$(modal).modal({
                    backdrop: "static",
                    keyboard: false,
                    show: true
                });
            }

            selectedMap.clear();
            redrawSelected();
            showValidation("");
            if (descriptionInput) {
                descriptionInput.value = "";
            }
            if (storeGroupPostInput) {
                const storeGroupValue = getCurrentStoreGroupValue();
                storeGroupPostInput.value = storeGroupValue === null ? "" : String(storeGroupValue);
            }
            syncConfirmState();

            try {
                const items = await searchItems("", !!checkBalanceInput?.checked, getCurrentStoreGroupValue());
                renderSearchRows(searchBody, items);
            } catch (err) {
                console.error(err);
                renderSearchRows(searchBody, []);
            }
        });

        searchBtn?.addEventListener("click", async () => {
            showValidation("");
            try {
                const items = await searchItems(
                    (keywordInput?.value || "").trim(),
                    !!checkBalanceInput?.checked,
                    getCurrentStoreGroupValue()
                );
                renderSearchRows(searchBody, items);
            } catch (err) {
                console.error(err);
                showValidation("Cannot load item list.");
                renderSearchRows(searchBody, []);
            }
        });

        searchBody.addEventListener("click", (event) => {
            const tr = event.target.closest("tr");
            if (!tr || !tr.dataset.itemCode) return;
            const checkbox = tr.querySelector(".create-mr-search-checkbox");
            if (checkbox && !event.target.closest("input")) {
                checkbox.checked = !checkbox.checked;
            }
        });

        searchBody.addEventListener("dblclick", (event) => {
            const tr = event.target.closest("tr");
            if (!tr || !tr.dataset.itemCode) return;
            const itemCode = tr.dataset.itemCode || "";
            selectedMap.set(itemCode, {
                itemCode,
                itemName: tr.dataset.itemName || "",
                unit: tr.dataset.unit || "",
                inStock: Number.parseFloat(tr.dataset.inStock || "0") || 0,
                storeGroupId: Number.parseInt(tr.dataset.storeGroupId || "0", 10) || 0
            });
            showValidation("");
            redrawSelected();
        });

        moveRightBtn?.addEventListener("click", () => {
            const checkedRows = Array.from(searchBody.querySelectorAll(".create-mr-search-checkbox:checked"))
                .map((x) => x.closest("tr"))
                .filter((x) => !!x);
            if (checkedRows.length === 0) {
                showValidation("Please choose item(s) from search result.");
                return;
            }

            checkedRows.forEach((tr) => {
                const itemCode = tr.dataset.itemCode || "";
                if (!itemCode) return;
                selectedMap.set(itemCode, {
                    itemCode,
                    itemName: tr.dataset.itemName || "",
                    unit: tr.dataset.unit || "",
                    inStock: Number.parseFloat(tr.dataset.inStock || "0") || 0,
                    storeGroupId: Number.parseInt(tr.dataset.storeGroupId || "0", 10) || 0
                });
            });

            showValidation("");
            redrawSelected();
        });

        moveLeftBtn?.addEventListener("click", () => {
            const selectedChecks = selectedBody.querySelectorAll(".create-mr-selected-checkbox:checked");
            selectedChecks.forEach((check) => {
                const tr = check.closest("tr");
                const itemCode = tr?.dataset.itemCode || "";
                if (itemCode) {
                    selectedMap.delete(itemCode);
                }
            });
            redrawSelected();
        });

        selectedBody.addEventListener("dblclick", (event) => {
            const tr = event.target.closest("tr");
            const itemCode = tr?.dataset.itemCode || "";
            if (!itemCode) return;
            selectedMap.delete(itemCode);
            redrawSelected();
        });

        descriptionInput?.addEventListener("input", () => {
            syncConfirmState();
        });

        confirmBtn?.addEventListener("click", (event) => {
            const selectedItems = Array.from(selectedMap.values());
            const description = (descriptionInput?.value || "").trim();

            if (selectedItems.length === 0) {
                event.preventDefault();
                showValidation("Please select at least one item.");
                return;
            }

            if (!description) {
                event.preventDefault();
                showValidation("Description is required.");
                descriptionInput?.focus();
                return;
            }

            if (descriptionPostInput) {
                descriptionPostInput.value = description;
            }
            if (linesJsonInput) {
                linesJsonInput.value = JSON.stringify(selectedItems);
            }
            if (storeGroupPostInput) {
                const storeGroupValue = getCurrentStoreGroupValue();
                storeGroupPostInput.value = storeGroupValue === null ? "" : String(storeGroupValue);
            }
            showValidation("");
            syncConfirmState();
        });
    }

    /**
     * Khởi tạo trang Material Request Index.
     */
    function initMaterialRequestIndexPage() {
        const table = document.getElementById("mrTable");
        const rows = table ? Array.from(table.querySelectorAll(".mr-row")) : [];

        if (table) {
            rows.forEach((row) => {
                row.addEventListener("dblclick", () => {
                    const detailUrl = row.getAttribute("data-detail-url");
                    if (detailUrl) {
                        window.location.href = detailUrl;
                    }
                });
            });
        }

        initConditionModeSwitcher();
        initStatusCheckboxDropdown();
        initCreateMrPopup();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initMaterialRequestIndexPage);
    } else {
        initMaterialRequestIndexPage();
    }
})();
