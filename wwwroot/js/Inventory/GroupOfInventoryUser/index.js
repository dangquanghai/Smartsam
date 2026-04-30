(() => {
    "use strict";

    function initSelection() {
        const rows = Array.from(document.querySelectorAll(".giu-row"));
        const rowChecks = Array.from(document.querySelectorAll(".giu-selector"));
        if (!rows.length || !rowChecks.length) return;

        const syncState = () => {
            rows.forEach((row) => {
                const check = row.querySelector(".giu-selector");
                row.classList.toggle("selected", !!check && check.checked);
            });
        };

        rows.forEach((row) => {
            row.addEventListener("click", (ev) => {
                if (ev.target && ev.target.closest("input, a, button")) return;
                const check = row.querySelector(".giu-selector");
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

        syncState();
    }

    function initPageSizeSubmit() {
        const form = document.getElementById("inventoryUserSearchForm");
        const pageSizeSelect = document.getElementById("inventoryUserPageSize");
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

    function initFilterSelect2(selector) {
        if (typeof window.jQuery === "undefined" || typeof window.jQuery.fn.select2 !== "function") {
            return;
        }

        const $filter = window.jQuery(selector);
        if (!$filter.length) {
            return;
        }

        if ($filter.hasClass("select2-hidden-accessible")) {
            $filter.select2("destroy");
        }

        $filter.select2({
            width: "100%",
            allowClear: true,
            placeholder: $filter.data("placeholder") || "--- All ---"
        });

        $filter.on("select2:open", () => {
            const searchField = document.querySelector(".select2-container--open .select2-search__field");
            if (searchField) {
                searchField.focus();
            }
        });
    }

    function initPage() {
        initSelection();
        initPageSizeSubmit();
        initFilterSelect2("#Filter_StoreGroupId");
        initFilterSelect2("#Filter_DepartmentId");
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initPage);
    } else {
        initPage();
    }
})();
