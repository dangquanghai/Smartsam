(() => {
    "use strict";

    /**
     * Hàm chính khởi tạo logic cho trang Supplier Detail.
     */
    function initSupplierDetailPage() {
        const saveForm = document.getElementById("supplierDetailForm") || document.querySelector("form[method='post']");
        if (!saveForm) return;

        const supplierCodeInput = document.getElementById("Input_SupplierCode");
        const supplierCodeValidationEl = document.querySelector('[data-valmsg-for="Input.SupplierCode"]');
        const supplierNameInput = document.getElementById("Input_SupplierName");
        const supplierNameValidationEl = document.querySelector('[data-valmsg-for="Input.SupplierName"]');
        const supplierAddressInput = document.getElementById("Input_Address");
        const supplierAddressValidationEl = document.querySelector('[data-valmsg-for="Input.Address"]');
        const isEditMode = (saveForm.dataset.isEdit || "").toLowerCase() === "true";
        const currentId = (saveForm.dataset.currentId || "").trim();

        let supplierCodeCheckToken = 0;
        let allowSubmitPassThrough = false;

        /**
         * Hiển thị/ẩn lỗi cho ô Supplier Code.
         */
        const setSupplierCodeValidation = (message) => {
            if (!supplierCodeInput) return;
            supplierCodeInput.classList.toggle("is-invalid", !!message);
            if (supplierCodeValidationEl) {
                supplierCodeValidationEl.textContent = message || "";
                supplierCodeValidationEl.classList.toggle("field-validation-error", !!message);
                supplierCodeValidationEl.classList.toggle("field-validation-valid", !message);
            }
        };

        /**
         * Validate required cho một input cụ thể.
         */
        const setRequiredValidation = (inputEl, validationEl, message) => {
            if (!inputEl) return true;
            const normalized = (inputEl.value || "").trim();
            const hasError = normalized.length === 0;
            inputEl.classList.toggle("is-invalid", hasError);
            if (validationEl) {
                validationEl.textContent = hasError ? message : "";
                validationEl.classList.toggle("field-validation-error", hasError);
                validationEl.classList.toggle("field-validation-valid", !hasError);
            }
            return !hasError;
        };

        /**
         * Áp dụng trạng thái lỗi do server trả về vào UI.
         */
        const applyExistingServerValidationState = (inputEl, validationEl) => {
            if (!inputEl || !validationEl) return;
            const hasMessage = (validationEl.textContent || "").trim().length > 0;
            inputEl.classList.toggle("is-invalid", hasMessage);
            validationEl.classList.toggle("field-validation-error", hasMessage);
            validationEl.classList.toggle("field-validation-valid", !hasMessage);
        };

        /**
         * Validate các trường bắt buộc của form.
         */
        const validateRequiredFields = () => {
            const isCodeValid = setRequiredValidation(supplierCodeInput, supplierCodeValidationEl, "Supplier code is required.");
            const isNameValid = setRequiredValidation(supplierNameInput, supplierNameValidationEl, "Supplier name is required.");
            const isAddressValid = setRequiredValidation(supplierAddressInput, supplierAddressValidationEl, "Address is required.");
            return !!isCodeValid && !!isNameValid && !!isAddressValid;
        };

        /**
         * Focus vào control lỗi đầu tiên để người dùng sửa nhanh.
         */
        const focusFirstInvalidField = () => {
            const focusControl = (control) => {
                control.focus();
                if ((control.tagName === "INPUT" || control.tagName === "TEXTAREA")
                    && typeof control.setSelectionRange === "function") {
                    const value = control.value || "";
                    control.setSelectionRange(value.length, value.length);
                }
            };

            const controls = saveForm.querySelectorAll("input, textarea, select");
            for (const control of controls) {
                if (control.disabled || control.type === "hidden") continue;

                const fieldName = control.getAttribute("name");
                const validationEl = fieldName
                    ? saveForm.querySelector(`[data-valmsg-for="${fieldName}"]`)
                    : null;
                const hasMessage = !!validationEl && (validationEl.textContent || "").trim().length > 0;
                const hasInvalidClass = control.classList.contains("is-invalid");

                if (hasInvalidClass || hasMessage) {
                    focusControl(control);
                    return true;
                }
            }

            return false;
        };

        /**
         * Gọi server để kiểm tra Supplier Code đã tồn tại chưa.
         */
        const checkSupplierCodeDuplicate = async () => {
            if (!supplierCodeInput) return true;

            const raw = (supplierCodeInput.value || "").trim();
            if (!raw) {
                return false;
            }

            const requestToken = ++supplierCodeCheckToken;
            const url = new URL(window.location.href);
            url.searchParams.set("handler", "CheckSupplierCode");
            url.searchParams.set("supplierCode", raw);
            if (currentId) {
                url.searchParams.set("id", currentId);
            } else {
                url.searchParams.delete("id");
            }

            try {
                const response = await fetch(url.toString(), {
                    method: "GET",
                    headers: { "X-Requested-With": "XMLHttpRequest" }
                });

                if (requestToken !== supplierCodeCheckToken) return true;
                if (!response.ok) return true;

                const data = await response.json();
                if (data && data.exists === true) {
                    setSupplierCodeValidation("Supplier code already exists.");
                    return false;
                }

                setSupplierCodeValidation("");
                return true;
            } catch {
                return true;
            }
        };

        if (supplierCodeInput) {
            supplierCodeInput.addEventListener("blur", () => {
                const isRequiredValid = setRequiredValidation(supplierCodeInput, supplierCodeValidationEl, "Supplier code is required.");
                if (isRequiredValid) {
                    void checkSupplierCodeDuplicate();
                }
            });

            supplierCodeInput.addEventListener("input", () => {
                setSupplierCodeValidation("");
            });
        }

        supplierNameInput?.addEventListener("blur", () => {
            setRequiredValidation(supplierNameInput, supplierNameValidationEl, "Supplier name is required.");
        });
        supplierNameInput?.addEventListener("input", () => {
            setRequiredValidation(supplierNameInput, supplierNameValidationEl, "Supplier name is required.");
        });

        supplierAddressInput?.addEventListener("blur", () => {
            setRequiredValidation(supplierAddressInput, supplierAddressValidationEl, "Address is required.");
        });
        supplierAddressInput?.addEventListener("input", () => {
            setRequiredValidation(supplierAddressInput, supplierAddressValidationEl, "Address is required.");
        });

        applyExistingServerValidationState(supplierCodeInput, supplierCodeValidationEl);
        applyExistingServerValidationState(supplierNameInput, supplierNameValidationEl);
        applyExistingServerValidationState(supplierAddressInput, supplierAddressValidationEl);
        focusFirstInvalidField();

        /**
         * Chặn submit mặc định để chạy validate client + check duplicate trước.
         */
        saveForm.addEventListener("submit", async (e) => {
            const submitter = e.submitter;
            if (submitter && submitter.getAttribute("formaction")?.includes("SubmitApproval")) {
                return;
            }

            if (allowSubmitPassThrough) {
                allowSubmitPassThrough = false;
                return;
            }

            e.preventDefault();
            e.stopImmediatePropagation();

            const requiredOk = validateRequiredFields();
            const duplicateOk = await checkSupplierCodeDuplicate();
            if (!requiredOk || !duplicateOk) {
                focusFirstInvalidField();
                return;
            }

            allowSubmitPassThrough = true;
            if (typeof saveForm.requestSubmit === "function") {
                saveForm.requestSubmit(submitter || undefined);
            } else {
                saveForm.submit();
            }
        }, true);

        /**
         * Tự đề xuất mã Supplier khi ở chế độ Add.
         */
        const loadSuggestedSupplierCode = async () => {
            if (!supplierCodeInput || isEditMode) return;
            if ((supplierCodeInput.value || "").trim()) return;

            try {
                const url = new URL(window.location.href);
                url.searchParams.set("handler", "SuggestSupplierCode");
                const response = await fetch(url.toString(), {
                    method: "GET",
                    headers: { "X-Requested-With": "XMLHttpRequest" }
                });

                if (!response.ok) return;
                const data = await response.json();
                const suggested = (data?.supplierCode || "").trim();
                if (!suggested) return;
                if ((supplierCodeInput.value || "").trim()) return;
                supplierCodeInput.value = suggested;
            } catch {
                // Bỏ qua lỗi gợi ý mã; server vẫn validate khi lưu.
            }
        };

        void loadSuggestedSupplierCode();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initSupplierDetailPage);
    } else {
        initSupplierDetailPage();
    }
})();

