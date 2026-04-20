(() => {
    "use strict";

    function initSupplierSelect2() {
        if (typeof window.jQuery === "undefined" || typeof window.jQuery.fn.select2 !== "function") {
            return;
        }

        window.jQuery("#Filter_SupplierCode, #Filter_SupplierName").each(function () {
            const $select = window.jQuery(this);
            if ($select.hasClass("select2-hidden-accessible")) {
                $select.select2("destroy");
            }

            $select.select2({
                width: "100%",
                allowClear: true,
                placeholder: $select.data("placeholder") || "",
                minimumResultsForSearch: 0
            });
        });
    }

    function initSupplierSync() {
        const $ = window.jQuery;
        if (typeof $ === "undefined") {
            return;
        }

        const supplierCode = $("#Filter_SupplierCode");
        const supplierName = $("#Filter_SupplierName");
        if (!supplierCode.length || !supplierName.length) {
            return;
        }

        let isSyncing = false;
        const sync = ($source, $target) => {
            $source.off("change.analyzingSuppliersSync").on("change.analyzingSuppliersSync", () => {
                if (isSyncing) {
                    return;
                }

                const nextValue = $source.val() || "";
                if (($target.val() || "") === nextValue) {
                    return;
                }

                isSyncing = true;
                $target.val(nextValue).trigger("change.select2");
                isSyncing = false;
            });
        };

        sync(supplierCode, supplierName);
        sync(supplierName, supplierCode);
    }

    function initPageSizeSubmit() {
        const form = document.getElementById("analyzingSuppliersForm");
        const pageSizeSelect = document.getElementById("analyzingSuppliersPageSize");
        if (!form || !pageSizeSelect) {
            return;
        }

        pageSizeSelect.addEventListener("change", () => {
            const pageSizeInput = form.querySelector("input[name='PageSize']");
            const pageNumberInput = form.querySelector("input[name='PageNumber']");
            if (!pageSizeInput || !pageNumberInput) {
                return;
            }

            pageSizeInput.value = pageSizeSelect.value;
            pageNumberInput.value = "1";
            form.submit();
        });
    }

    function initCloseButtons() {
        const buttons = [
            document.getElementById("btnAnalyzingSuppliersCloseTop"),
            document.getElementById("btnAnalyzingSuppliersCloseBottom")
        ].filter(Boolean);

        buttons.forEach((button) => {
            button.addEventListener("click", () => {
                if (window.history.length > 1) {
                    window.history.back();
                    return;
                }

                window.location.href = "/";
            });
        });
    }

    document.addEventListener("DOMContentLoaded", () => {
        initSupplierSelect2();
        initSupplierSync();
        initPageSizeSubmit();
        initCloseButtons();
    });
})();
