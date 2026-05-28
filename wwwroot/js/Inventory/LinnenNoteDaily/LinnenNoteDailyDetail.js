(() => {
    "use strict";

    let initialState = "";
    let allowUnload = false;
    let pendingNavigationUrl = "";
    let activeLinnenReportPreviewObjectUrl = "";

    function readInteger(input) {
        const rawValue = (input?.value || "").trim();
        if (rawValue === "") return 0;

        const value = Number.parseInt(rawValue, 10);
        return Number.isFinite(value) ? value : 0;
    }

    function collectDetails() {
        const records = Array.from(document.querySelectorAll(".linnen-note-detail-record"));
        return records.map((record) => {
            const detailId = record.getAttribute("data-id") || "0";
            const getInput = (field) => document.querySelector(`input[data-detail-id='${detailId}'][data-field='${field}']`);
            return {
                id: Number.parseInt(detailId, 10),
                headerId: Number.parseInt(record.getAttribute("data-header-id") || "0", 10),
                pentry: Number.parseInt(record.getAttribute("data-pentry") || "0", 10),
                pentryName: record.getAttribute("data-pentry-name") || "",
                timeSection: Number.parseInt(record.getAttribute("data-time-section") || "0", 10),
                linenCode: record.getAttribute("data-linen-code") || "",
                linenName: record.getAttribute("data-linen-name") || "",
                be: readInteger(getInput("be")),
                de: readInteger(getInput("de")),
                re: readInteger(getInput("re"))
            };
        });
    }

    function serializeFormState() {
        const note = document.getElementById("Header_Description");
        const date = document.getElementById("Header_DateCreate");
        const rent = document.getElementById("Header_IsRent");

        return JSON.stringify({
            note: (note?.value || "").trim(),
            date: date?.value || "",
            rent: !!rent?.checked,
            details: collectDetails()
        });
    }

    function hasUnsavedChanges() {
        if (window.linnenNoteDailyDetail?.isView) return false;
        return serializeFormState() !== initialState;
    }

    function refreshInitialState() {
        initialState = serializeFormState();
    }

    function resetRedirectFlags() {
        $("#ReturnToIndex").val("false");
        $("#RedirectUrl").val("");
    }

    function initAutoDismissAlerts() {
        $(".js-auto-dismiss-alert").each(function () {
            const $alert = $(this);
            const timeout = Number.parseInt($alert.data("timeout"), 10);
            const delay = Number.isFinite(timeout) && timeout > 0 ? timeout : 20000;

            window.setTimeout(function () {
                if ($alert.hasClass("show")) {
                    $alert.alert("close");
                } else {
                    $alert.remove();
                }
            }, delay);
        });
    }

    function validateMainForm() {
        const note = document.getElementById("Header_Description");
        if (!note || !note.value.trim()) {
            alert("Please enter/select: Note");
            focusErrorField($(note));
            return false;
        }
        if (note.value.trim().length > 100) {
            alert("Note cannot exceed 100 characters.");
            focusErrorField($(note));
            return false;
        }

        return true;
    }

    function validateDetailValues() {
        const inputs = Array.from(document.querySelectorAll(".ln-qty"));
        for (const input of inputs) {
            const value = (input.value || "").trim();
            const field = input.getAttribute("data-field");
            const pattern = field === "be" ? /^-?\d+$/ : /^\d+$/;

            if (value !== "" && !pattern.test(value)) {
                alert("Please enter integer quantity only.");
                focusErrorField($(input));
                return false;
            }
        }

        return true;
    }

    function focusErrorField($el) {
        setTimeout(() => $el.focus(), 300);
    }

    function initSubmit() {
        const $form = $("#linnenNoteDetailForm");
        const $detailsJson = $("#DetailsJson");
        if ($form.length === 0 || $detailsJson.length === 0) return;

        $form.off("submit").on("submit", function (ev) {
            if (window.linnenNoteDailyDetail?.isView) return;

            ev.preventDefault();
            if (!validateMainForm()) {
                resetRedirectFlags();
                return;
            }
            if (!validateDetailValues()) {
                resetRedirectFlags();
                return;
            }

            $detailsJson.val(JSON.stringify(collectDetails()));
            allowUnload = true;
            $form.off("submit").submit();
        });
    }

    function focusNextQuantityInput(currentInput) {
        const currentRow = currentInput.closest("tr");
        const columnKey = currentInput.getAttribute("data-nav-column") || "";
        if (!currentRow || !columnKey) {
            return;
        }

        let nextRow = currentRow.nextElementSibling;
        while (nextRow) {
            const nextInput = nextRow.querySelector(`.ln-qty[data-nav-column="${columnKey}"]`);
            if (nextInput && !nextInput.readOnly && !nextInput.disabled) {
                nextInput.focus();
                nextInput.select();
                return;
            }

            nextRow = nextRow.nextElementSibling;
        }
    }

    function initQuantityInputs() {
        $(document).off("keypress", ".ln-qty").on("keypress", ".ln-qty", function (ev) {
            const key = ev.which || ev.keyCode;
            const field = $(this).data("field");
            const isDigit = key >= 48 && key <= 57;
            const isBackspace = key === 8;
            const isMinus = key === 45;

            if (key === 13) {
                return;
            }

            if (field === "be") {
                if (!isDigit && !isMinus && !isBackspace) {
                    ev.preventDefault();
                }
            } else if (!isDigit && !isBackspace) {
                ev.preventDefault();
            }
        });

        $(document).off("keydown", ".ln-qty").on("keydown", ".ln-qty", function (ev) {
            if (ev.key !== "Enter") {
                return;
            }

            ev.preventDefault();
            focusNextQuantityInput(this);
        });
    }

    function initRentNote() {
        $("#Header_IsRent").off("change").on("change", function () {
            const $note = $("#Header_Description");
            const current = $note.val() || "";

            if ($(this).is(":checked")) {
                if (current.indexOf("(Rent)") < 0) {
                    $note.val(current + "(Rent)");
                }
            } else {
                const pos = current.indexOf("(");
                if (pos >= 0) {
                    $note.val(current.substring(0, pos));
                }
            }
        });
    }

    function initCloseButton() {
        $("#btnCloseLinnenNoteDetail").off("click").on("click", function () {
            const indexUrl = $(this).data("index-url") || window.linnenNoteDailyDetail?.indexUrl || "/Inventory/LinnenNoteDaily";

            if (window.linnenNoteDailyDetail?.isView) {
                allowUnload = true;
                window.location.href = indexUrl;
                return;
            }

            if (!hasUnsavedChanges()) {
                allowUnload = true;
                window.location.href = indexUrl;
                return;
            }

            openUnsavedDialog(indexUrl);
        });
    }

    function isGuardedLink($link) {
        const href = $link.attr("href") || "";
        if (!href || href === "#" || href.startsWith("javascript:")) return false;
        if (href.startsWith("mailto:") || href.startsWith("tel:")) return false;
        if ($link.attr("target") === "_blank" || $link.attr("download")) return false;
        if ($link.data("skip-unsaved-check") === true) return false;
        return true;
    }

    function openUnsavedDialog(targetUrl) {
        pendingNavigationUrl = targetUrl || "";
        $("#linnenNoteUnsavedModal").modal("show");
    }

    function navigateWithoutSaving(targetUrl) {
        allowUnload = true;
        $("#linnenNoteUnsavedModal").modal("hide");
        if (targetUrl) {
            window.location.href = targetUrl;
        }
    }

    function saveAndNavigate(targetUrl) {
        resetRedirectFlags();
        $("#RedirectUrl").val(targetUrl || "");
        $("#linnenNoteUnsavedModal").modal("hide");
        $("#linnenNoteDetailForm").trigger("submit");
    }

    function initUnsavedChangeGuard() {
        if (window.linnenNoteDailyDetail?.isView) return;

        refreshInitialState();
        resetRedirectFlags();

        $(document).off("input.unsaved change.unsaved", "#linnenNoteDetailForm input, #linnenNoteDetailForm textarea, #linnenNoteDetailForm select");
        $(document).on("input.unsaved change.unsaved", "#linnenNoteDetailForm input, #linnenNoteDetailForm textarea, #linnenNoteDetailForm select", function () {
            allowUnload = false;
        });

        $(document).off("click.unsaved", "a[href]").on("click.unsaved", "a[href]", function (ev) {
            if (!hasUnsavedChanges()) return;
            const $link = $(this);
            if (!isGuardedLink($link)) return;

            const href = $link.attr("href") || "";
            if (href === window.location.pathname + window.location.search) return;

            ev.preventDefault();
            openUnsavedDialog(href);
        });

        window.addEventListener("beforeunload", function (ev) {
            if (allowUnload || !hasUnsavedChanges()) return;
            ev.preventDefault();
            ev.returnValue = "";
        });

        $("#btnUnsavedSave").off("click").on("click", function () {
            saveAndNavigate(pendingNavigationUrl);
        });

        $("#btnUnsavedDiscard").off("click").on("click", function () {
            navigateWithoutSaving(pendingNavigationUrl);
        });

        $("#linnenNoteUnsavedModal").off("shown.bs.modal").on("shown.bs.modal", function () {
            $("#btnUnsavedCancel").trigger("focus");
        });

        $("#linnenNoteUnsavedModal").off("hidden.bs.modal").on("hidden.bs.modal", function () {
            pendingNavigationUrl = "";
        });
    }

    function initPrintReport() {
        $("#btnPrintLinnenNoteDetail").off("click").on("click", function () {
            if (!(window.linnenNoteDailyDetail?.reportPdfUrl || "")) {
                alert("Report preview is not available.");
                return;
            }

            const $modal = $("#linnenReportModal");
            const frame = document.getElementById("linnenReportFrame");
            if ($modal.length === 0 || !frame) {
                alert("Report preview modal is not available.");
                return;
            }

            $modal.modal("show");
            previewLinnenReportPdf();
        });

        $("#btnPreviewLinnenNoteReport").off("click").on("click", function () {
            previewLinnenReportPdf();
        });
    }

    function initReportModal() {
        $("#linnenReportModal").off("hidden.bs.modal").on("hidden.bs.modal", function () {
            const frame = document.getElementById("linnenReportFrame");
            clearLinnenReportPreview(frame);
        });
    }

    function previewLinnenReportPdf() {
        const reportPdfUrl = buildLinnenReportPdfUrl();
        const $loading = $("#linnenReportLoading");
        const $meta = $("#linnenReportMeta");
        const frame = document.getElementById("linnenReportFrame");
        const linenCode = ($("#linnenNoteReportLinenCode").val() || "").toString();
        const description = ($("#linnenNoteReportDescription option:selected").text() || "").trim();
        const linenLabel = ($("#linnenNoteReportLinenCode option:selected").text() || "All").trim();

        if (!reportPdfUrl || !frame) {
            alert("Report preview is not available.");
            return;
        }

        $meta.text(`Pantry-Linen | ${description}${linenCode ? ` | ${linenLabel}` : ""}`);
        $loading.show();
        clearLinnenReportPreview(frame);

        loadLinnenReportPdfPreview(frame, reportPdfUrl)
            .then(function (objectUrl) {
                if (objectUrl) {
                    activeLinnenReportPreviewObjectUrl = objectUrl;
                }
                $loading.hide();
            })
            .catch(function (error) {
                $loading.hide();
                alert(error?.message || "Cannot load PDF preview.");
            });
    }

    function buildLinnenReportPdfUrl() {
        const baseUrl = window.linnenNoteDailyDetail?.reportPdfUrl || "";
        if (!baseUrl) {
            return "";
        }

        const url = new URL(baseUrl, window.location.origin);
        const linenCode = ($("#linnenNoteReportLinenCode").val() || "").toString().trim();
        if (linenCode) {
            url.searchParams.set("linenCode", linenCode);
        } else {
            url.searchParams.delete("linenCode");
        }

        return `${url.pathname}${url.search}`;
    }

    async function loadLinnenReportPdfPreview(frame, url) {
        const response = await fetch(url, {
            method: "GET",
            credentials: "same-origin",
            headers: { "X-Requested-With": "XMLHttpRequest" }
        });

        if (!response.ok) {
            throw new Error("Cannot load report preview.");
        }

        const contentType = String(response.headers.get("content-type") || "").toLowerCase();
        if (!contentType.includes("application/pdf")) {
            if (contentType.includes("application/json")) {
                const result = await response.json();
                throw new Error(result?.message || "Cannot load report preview.");
            }

            throw new Error("Cannot load report preview.");
        }

        const blob = await response.blob();
        const previewUrl = URL.createObjectURL(blob);
        frame.src = previewUrl;
        return previewUrl;
    }

    function clearLinnenReportPreview(frame) {
        if (frame) {
            frame.removeAttribute("src");
        }

        if (activeLinnenReportPreviewObjectUrl) {
            URL.revokeObjectURL(activeLinnenReportPreviewObjectUrl);
            activeLinnenReportPreviewObjectUrl = "";
        }
    }

    function initPopupMode() {
        if (window.linnenNoteDailyDetail?.isPopup) {
            document.body.classList.add("linnen-note-popup-body");
        }
    }

    document.addEventListener("DOMContentLoaded", function () {
        initAutoDismissAlerts();
        initPopupMode();
        initSubmit();
        initRentNote();
        initQuantityInputs();
        initCloseButton();
        initUnsavedChangeGuard();
        initPrintReport();
        initReportModal();
    });
})();
