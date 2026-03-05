(() => {
    "use strict";

    function escapeHtml(value) {
        return (value || "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function resolveModalClass(kind) {
        const value = (kind || "").toLowerCase();
        if (value === "danger") return "modal-header-danger";
        if (value === "info") return "modal-header-info";
        return "modal-header-primary";
    }

    function showActionModal({ title, message, messageHtml, kind, onConfirm }) {
        const modal = document.getElementById("actionConfirmModal");
        if (!modal) return;

        const header = modal.querySelector("#actionConfirmModalHeader");
        const titleEl = modal.querySelector("#actionConfirmModalLabel");
        const bodyEl = modal.querySelector("#actionConfirmModalBody");
        const submitBtn = modal.querySelector("#actionConfirmModalSubmitBtn");

        if (!header || !titleEl || !bodyEl || !submitBtn) return;

        header.classList.remove("modal-header-primary", "modal-header-info", "modal-header-danger");
        header.classList.add(resolveModalClass(kind));

        titleEl.textContent = title || "Notification";
        if (messageHtml) {
            bodyEl.innerHTML = messageHtml;
        } else {
            bodyEl.textContent = message || "";
        }

        submitBtn.onclick = () => {
            if (window.jQuery) {
                window.jQuery(modal).modal("hide");
            }
            if (typeof onConfirm === "function") {
                onConfirm();
            }
        };

        if (window.jQuery) {
            window.jQuery(modal).modal("show");
        }
    }

    function initActionConfirmHandlers() {
        const form = document.getElementById("approveSupplierForm");
        if (!form) return;

        const buttons = form.querySelectorAll("[data-confirm-handler]");
        buttons.forEach((btn) => {
            btn.addEventListener("click", () => {
                if (btn.disabled) return;

                const handler = btn.getAttribute("data-confirm-handler");
                const title = btn.getAttribute("data-confirm-title") || "Confirmation";
                const message = btn.getAttribute("data-confirm-message") || "";
                const kind = btn.getAttribute("data-confirm-kind") || "primary";
                let messageHtml = "";

                if (handler === "Approve" || handler === "Disapprove") {
                    const supplierNameEl = document.getElementById("EditSupplier_SupplierName");
                    const supplierName = supplierNameEl ? supplierNameEl.value : "";
                    const safeMessage = escapeHtml(message);
                    const safeSupplierName = escapeHtml(supplierName || "N/A");
                    messageHtml = `${safeMessage}<br><br>Supplier: <strong>${safeSupplierName}</strong>`;
                }

                showActionModal({
                    title,
                    message,
                    messageHtml,
                    kind,
                    onConfirm: () => {
                        form.action = `?handler=${handler}`;
                        if (!form.checkValidity()) {
                            form.reportValidity();
                            return;
                        }
                        if (typeof form.requestSubmit === "function") {
                            form.requestSubmit();
                        } else {
                            form.submit();
                        }
                    }
                });
            });
        });
    }

    async function reloadResultPanel(url) {
        const panel = document.getElementById("supplierResultPanelContainer");
        if (!panel) {
            window.location.href = url;
            return;
        }

        try {
            const response = await fetch(url, {
                method: "GET",
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });

            if (!response.ok) {
                window.location.href = url;
                return;
            }

            const html = await response.text();
            const doc = new DOMParser().parseFromString(html, "text/html");
            const newPanel = doc.getElementById("supplierResultPanelContainer");
            if (!newPanel) {
                window.location.href = url;
                return;
            }

            panel.replaceWith(newPanel);
            if (window.history?.replaceState) {
                window.history.replaceState({}, "", url);
            }
            if (typeof window.initApproveAnnualResultPanel === "function") {
                window.initApproveAnnualResultPanel();
            }
        } catch {
            window.location.href = url;
        }
    }

    function getSelectedRows(rows) {
        return rows.filter((row) => {
            const checkbox = row.querySelector(".supplier-selector");
            return !!checkbox && checkbox.checked;
        });
    }

    function setButtonState(button, disabled) {
        if (!button) return;
        button.disabled = disabled;
        if (disabled) {
            button.setAttribute("disabled", "disabled");
            button.setAttribute("aria-disabled", "true");
        } else {
            button.removeAttribute("disabled");
            button.setAttribute("aria-disabled", "false");
        }
    }

    function initApproveAnnualResultPanel() {
        const panel = document.getElementById("supplierResultPanelContainer");
        if (!panel) return;

        const rows = Array.from(panel.querySelectorAll(".supplier-row"));
        const approveBtn = panel.querySelector("#submitSupplierBtn");
        const selectedSupplierIdInput = panel.querySelector("#selectedSupplierIdInput");
        const selectedSupplierIdsCsvInput = panel.querySelector("#selectedSupplierIdsCsvInput");

        function syncSelectionState() {
            const selectedRows = getSelectedRows(rows);
            rows.forEach((row) => row.classList.toggle("selected", selectedRows.includes(row)));

            const count = selectedRows.length;
            const singleRow = count === 1 ? selectedRows[0] : null;
            const singleId = singleRow?.getAttribute("data-supplier-id") || null;
            const selectedIds = selectedRows
                .map((row) => row.getAttribute("data-supplier-id"))
                .filter((id) => !!id);

            if (selectedSupplierIdInput) selectedSupplierIdInput.value = singleId || "";
            if (selectedSupplierIdsCsvInput) selectedSupplierIdsCsvInput.value = selectedIds.join(",");

            setButtonState(approveBtn, count === 0);
        }

        window.syncApproveAnnualSelection = syncSelectionState;
        window.toggleApproveAnnualRowSelection = (ev, row) => {
            if (!row) return;
            if (ev?.target?.closest?.(".supplier-selector")) {
                syncSelectionState();
                return;
            }

            const checkbox = row.querySelector(".supplier-selector");
            if (!checkbox) return;
            checkbox.checked = !checkbox.checked;
            syncSelectionState();
        };

        rows.forEach((row) => {
            row.addEventListener("dblclick", () => {
                const detailUrl = row.getAttribute("data-detail-url");
                if (detailUrl) {
                    window.location.href = detailUrl;
                }
            });
        });

        panel.querySelectorAll(".supplier-pager-link").forEach((link) => {
            link.addEventListener("click", (e) => {
                const href = link.getAttribute("href");
                if (!href || href === "#" || link.closest(".page-item.disabled")) {
                    e.preventDefault();
                    return;
                }
                e.preventDefault();
                void reloadResultPanel(href);
            });
        });

        syncSelectionState();
    }

    function initPage() {
        window.initApproveAnnualResultPanel = initApproveAnnualResultPanel;
        initApproveAnnualResultPanel();
        initActionConfirmHandlers();

        window.ApproveAnnualModal = {
            confirm: (title, message, onConfirm) => showActionModal({ title, message, kind: "primary", onConfirm }),
            info: (title, message) => showActionModal({ title, message, kind: "info" }),
            danger: (title, message) => showActionModal({ title, message, kind: "danger" })
        };
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initPage);
    } else {
        initPage();
    }
})();
