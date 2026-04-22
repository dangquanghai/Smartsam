(() => {
    "use strict";

    async function loadPdfPreview(frame, url, getCurrentUrl) {
        if (!frame || !url) {
            return null;
        }

        const response = await fetch(url, {
            method: "GET",
            headers: { "X-Requested-With": "XMLHttpRequest" }
        });

        if (!response.ok) {
            throw new Error("Cannot load report preview.");
        }

        const blob = await response.blob();
        if (typeof getCurrentUrl === "function" && getCurrentUrl() !== url) {
            return null;
        }

        const previewUrl = URL.createObjectURL(blob);
        frame.src = previewUrl;
        return previewUrl;
    }

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
                minimumResultsForSearch: 0,
                selectionCssClass: "vni-font",
                dropdownCssClass: "vni-font"
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

    function initReportPreview() {
        const previewButton = document.getElementById("btnAspReportPreview");
        const previewModal = document.getElementById("aspReportPreviewModal");
        const previewFrame = document.getElementById("aspReportPreviewFrame");
        const openButton = document.getElementById("btnAspReportOpen");
        if (!previewButton || !previewModal || !previewFrame) {
            return;
        }

        let activeReportUrl = String(previewButton.getAttribute("data-report-url") || "").trim();
        let activePreviewObjectUrl = "";

        previewButton.addEventListener("click", () => {
            activeReportUrl = String(previewButton.getAttribute("data-report-url") || "").trim();
            if (!activeReportUrl || !window.jQuery) {
                return;
            }

            window.jQuery(previewModal).modal("show");
            previewFrame.removeAttribute("src");

            if (activePreviewObjectUrl) {
                URL.revokeObjectURL(activePreviewObjectUrl);
                activePreviewObjectUrl = "";
            }

            loadPdfPreview(previewFrame, activeReportUrl, () => activeReportUrl)
                .then((previewUrl) => {
                    if (previewUrl) {
                        activePreviewObjectUrl = previewUrl;
                    }
                })
                .catch(() => {
                    if (window.jQuery) {
                        window.jQuery(previewModal).modal("hide");
                    }

                    window.open(activeReportUrl, "_blank", "noopener");
                });
        });

        openButton?.addEventListener("click", () => {
            if (!activeReportUrl) {
                return;
            }

            window.open(activeReportUrl, "_blank", "noopener");
        });

        if (window.jQuery) {
            window.jQuery(previewModal).on("show.bs.modal", () => {
                window.jQuery("#Filter_SupplierCode, #Filter_SupplierName").each(function () {
                    const $select = window.jQuery(this);
                    if ($select.hasClass("select2-hidden-accessible")) {
                        $select.select2("close");
                    }
                });

                window.jQuery(".modal-backdrop").last().addClass("asp-report-preview-backdrop");
            });

            window.jQuery(previewModal).on("hidden.bs.modal", () => {
                previewFrame.removeAttribute("src");
                if (activePreviewObjectUrl) {
                    URL.revokeObjectURL(activePreviewObjectUrl);
                    activePreviewObjectUrl = "";
                }

                window.jQuery(".modal-backdrop.asp-report-preview-backdrop").removeClass("asp-report-preview-backdrop");
            });
        }
    }

    document.addEventListener("DOMContentLoaded", () => {
        initSupplierSelect2();
        initSupplierSync();
        initPageSizeSubmit();
        initCloseButtons();
        initReportPreview();
    });
})();
