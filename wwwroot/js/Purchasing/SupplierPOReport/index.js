(() => {
    "use strict";

    function initDateRangeToggle() {
        const form = document.getElementById("supplierPoSearchForm");
        const useDateRange = document.getElementById("Filter_UseDateRange");
        const fromDate = document.getElementById("Filter_FromDate");
        const toDate = document.getElementById("Filter_ToDate");
        if (!useDateRange || !fromDate || !toDate) {
            return;
        }

        const syncRangeConstraints = () => {
            const fromValue = fromDate.value;
            const toValue = toDate.value;
            toDate.min = fromValue || "";
            toDate.disabled = !useDateRange.checked || !fromValue;
            if (!fromValue) {
                toDate.value = "";
                return;
            }
            if (toValue && fromValue > toValue) {
                toDate.value = fromValue;
            }
        };

        const sync = () => {
            const disabled = !useDateRange.checked;
            fromDate.disabled = disabled;
            if (disabled) {
                fromDate.value = "";
                toDate.value = "";
            }
            syncRangeConstraints();
        };

        fromDate.addEventListener("change", syncRangeConstraints);
        toDate.addEventListener("change", syncRangeConstraints);
        form?.addEventListener("submit", (ev) => {
            if (!useDateRange.checked) {
                fromDate.value = "";
                toDate.value = "";
                return;
            }
            if (fromDate.value && toDate.value && fromDate.value > toDate.value) {
                ev.preventDefault();
                alert("From Date must be less than or equal to To Date.");
                toDate.focus();
            }
        });

        useDateRange.addEventListener("change", sync);
        sync();
    }

    function initPageSizeSubmit() {
        const form = document.getElementById("supplierPoSearchForm");
        const pageSizeSelect = document.getElementById("supplierPoPageSize");
        if (!form || !pageSizeSelect) {
            return;
        }

        pageSizeSelect.addEventListener("change", () => {
            const pageInput = form.querySelector("input[name='PageNumber']");
            const pageSizeInput = form.querySelector("input[name='PageSize']");
            if (!pageInput || !pageSizeInput) {
                return;
            }

            pageInput.value = "1";
            pageSizeInput.value = pageSizeSelect.value;
            form.submit();
        });
    }

    document.addEventListener("DOMContentLoaded", () => {
        initDateRangeToggle();
        initPageSizeSubmit();
    });
})();
