(() => {
    "use strict";

    function syncSupplierSelects(source, target) {
        if (!source || !target) {
            return;
        }

        source.addEventListener("change", () => {
            target.value = source.value;
        });
    }

    function initSupplierSync() {
        const supplierCode = document.getElementById("Filter_SupplierCode");
        const supplierName = document.getElementById("Filter_SupplierName");
        if (!supplierCode || !supplierName) {
            return;
        }

        syncSupplierSelects(supplierCode, supplierName);
        syncSupplierSelects(supplierName, supplierCode);
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
        initSupplierSync();
        initPageSizeSubmit();
        initCloseButtons();
    });
})();
