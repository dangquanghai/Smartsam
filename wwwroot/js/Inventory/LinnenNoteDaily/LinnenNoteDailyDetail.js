(() => {
    "use strict";

    let initialState = "";
    let allowUnload = false;
    let pendingNavigationUrl = "";

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

    function initQuantityInputs() {
        $(document).off("keypress", ".ln-qty").on("keypress", ".ln-qty", function (ev) {
            const key = ev.which || ev.keyCode;
            const field = $(this).data("field");
            const isDigit = key >= 48 && key <= 57;
            const isBackspace = key === 8;
            const isMinus = key === 45;

            if (field === "be") {
                if (!isDigit && !isMinus && !isBackspace) {
                    ev.preventDefault();
                }
            } else if (!isDigit && !isBackspace) {
                ev.preventDefault();
            }
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
            const reportPageUrl = window.linnenNoteDailyDetail?.reportPageUrl || "";
            const noteId = $("#Header_Id").val() || "";
            if (!reportPageUrl || !noteId) {
                alert("Report preview is not available.");
                return;
            }

            window.location.href = `${reportPageUrl}?reportType=pantry&descriptionId=${encodeURIComponent(noteId)}`;
        });
    }

    function loadPrintPreview() {
        const previewUrl = window.linnenNoteDailyDetail?.printPreviewUrl || "";
        if (!previewUrl) {
            alert("Report preview is not available.");
            return;
        }

        const linenCode = ($("#linnenReportLinenCode").val() || "").toString().trim();
        const requestUrl = new URL(previewUrl, window.location.origin);
        if (linenCode) {
            requestUrl.searchParams.set("linenCode", linenCode);
        }

        $("#linnenReportPreviewContainer").html('<div class="text-center text-muted py-5">Loading report data...</div>');

        $.ajax({
            url: requestUrl.toString(),
            type: "GET",
            success: function (response) {
                if (!response || response.success !== true) {
                    const message = response && response.message ? response.message : "Cannot load report preview.";
                    $("#linnenReportPreviewContainer").html(`<div class="alert alert-danger mb-0">${escapeHtml(message)}</div>`);
                    return;
                }

                renderPrintPreview(response);
            },
            error: function (xhr) {
                const message = xhr && xhr.responseText ? xhr.responseText : "Cannot load report preview.";
                $("#linnenReportPreviewContainer").html(`<div class="alert alert-danger mb-0">${escapeHtml(message)}</div>`);
            }
        });
    }

    function renderPrintPreview(response) {
        const columns = Array.isArray(response.columns) ? response.columns : [];
        const rows = Array.isArray(response.rows) ? response.rows : [];
        const noteText = response.description || "";
        const dateText = response.dateCreate || "";

        $("#linnenReportPreviewMeta").text(`Note: ${noteText} | Date: ${dateText}`);

        if (columns.length === 0 || rows.length === 0) {
            $("#linnenReportPreviewContainer").html('<div class="text-center text-muted py-5">No preview data.</div>');
            return;
        }

        let html = '<table class="table table-bordered table-sm mb-0 linnen-report-preview-table"><thead><tr>';
        html += '<th rowspan="2">Pantry</th><th rowspan="2">A/P</th>';
        columns.forEach(function (column) {
            html += `<th colspan="3">${escapeHtml(column.title || "")}</th>`;
        });
        html += '</tr><tr>';
        columns.forEach(function () {
            html += '<th>Be</th><th>De</th><th>Re</th>';
        });
        html += '</tr></thead><tbody>';

        rows.forEach(function (row) {
            html += '<tr>';
            html += `<td class="ln-report-pantry">${escapeHtml(row.pentryName || "")}</td>`;
            html += `<td class="ln-report-time">${row.timeSection === 1 ? "A" : "P"}</td>`;
            columns.forEach(function (column) {
                html += `<td class="text-right">${formatPreviewNumber(row[column.beField])}</td>`;
                html += `<td class="text-right">${formatPreviewNumber(row[column.deField])}</td>`;
                html += `<td class="text-right">${formatPreviewNumber(row[column.reField])}</td>`;
            });
            html += '</tr>';
        });

        html += '</tbody></table>';
        $("#linnenReportPreviewContainer").html(html);
    }

    function formatPreviewNumber(value) {
        const numeric = Number.parseInt(value, 10);
        if (!Number.isFinite(numeric) || numeric === 0) return "";
        return numeric.toString();
    }

    function escapeHtml(value) {
        return $("<div>").text(value || "").html();
    }

    document.addEventListener("DOMContentLoaded", function () {
        initSubmit();
        initRentNote();
        initQuantityInputs();
        initCloseButton();
        initUnsavedChangeGuard();
        initPrintReport();
    });
})();
