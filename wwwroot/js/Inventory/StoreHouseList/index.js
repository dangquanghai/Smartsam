(() => {
    "use strict";

    function initSelection() {
        const rows = Array.from(document.querySelectorAll(".shl-row"));
        const rowChecks = Array.from(document.querySelectorAll(".shl-selector"));
        if (!rows.length || !rowChecks.length) return;

        const syncState = () => {
            rows.forEach((row) => {
                const check = row.querySelector(".shl-selector");
                row.classList.toggle("selected", !!check && check.checked);
            });
        };

        rows.forEach((row) => {
            row.addEventListener("click", (ev) => {
                if (ev.target && ev.target.closest("input, a, button")) return;
                const check = row.querySelector(".shl-selector");
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
        const form = document.getElementById("storeHouseSearchForm");
        const pageSizeSelect = document.getElementById("storeHousePageSize");
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

    function initDepartmentSelect2() {
        if (typeof window.jQuery === "undefined" || typeof window.jQuery.fn.select2 !== "function") {
            return;
        }

        const $department = window.jQuery("#Filter_DepartmentId");
        if (!$department.length) {
            return;
        }

        if ($department.hasClass("select2-hidden-accessible")) {
            $department.select2("destroy");
        }

        $department.select2({
            width: "100%",
            allowClear: true,
            placeholder: $department.data("placeholder") || "--- All ---"
        });

        $department.on("select2:open", () => {
            const searchField = document.querySelector(".select2-container--open .select2-search__field");
            if (searchField) {
                searchField.focus();
            }
        });
    }

    function initPage() {
        initSelection();
        initPageSizeSubmit();
        initDepartmentSelect2();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initPage);
    } else {
        initPage();
    }
})();
