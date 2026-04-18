(function () {
    'use strict';

    function escapeHtml(value) {
        return $('<div/>').text(value || '').html();
    }

    function getModalHeaderClass(kind) {
        if (kind === 'danger') return 'modal-header-danger';
        if (kind === 'info') return 'modal-header-info';
        return 'modal-header-primary';
    }

    function showConfirmModal(options) {
        const modal = $('#actionConfirmModal');
        const header = $('#actionConfirmModalHeader');
        const title = $('#actionConfirmModalLabel');
        const body = $('#actionConfirmModalBody');
        const submitBtn = $('#actionConfirmModalSubmitBtn');

        header.removeClass('modal-header-primary modal-header-info modal-header-danger');
        header.addClass(getModalHeaderClass(options.kind));

        title.text(options.title || 'Confirmation');
        if (options.messageHtml) {
            body.html(options.messageHtml);
        } else {
            body.text(options.message || '');
        }

        submitBtn.off('click').on('click', function () {
            modal.modal('hide');
            if (typeof options.onConfirm === 'function') {
                options.onConfirm();
            }
        });

        modal.modal('show');
    }

    function submitFormByHandler(handler, options) {
        const form = $('#approveSupplierForm');
        if (!form.length) return;

        const submitOptions = options || {};
        form.attr('action', '?handler=' + handler);

        const htmlForm = form[0];
        if (!submitOptions.skipValidation && htmlForm && typeof htmlForm.checkValidity === 'function' && !htmlForm.checkValidity()) {
            htmlForm.reportValidity();
            return;
        }

        if (htmlForm && typeof htmlForm.requestSubmit === 'function') {
            htmlForm.requestSubmit();
            return;
        }

        form.trigger('submit');
    }

    function bindConfirmButtons() {
        const form = $('#approveSupplierForm');
        if (!form.length) return;

        form.find('[data-confirm-handler]').off('click').on('click', function () {
            const $button = $(this);
            if ($button.prop('disabled')) {
                return;
            }

            const handler = $button.data('confirm-handler');
            const title = $button.data('confirm-title') || 'Confirmation';
            const message = $button.data('confirm-message') || '';
            const kind = $button.data('confirm-kind') || 'primary';
            const skipValidation = $button.data('confirm-skip-validation') === true || $button.data('confirm-skip-validation') === 'true';
            const subject = ($button.data('confirm-subject') || '').toString().trim();
            const supplierName = $('#EditSupplier_SupplierName').val() || '';
            const subjectLabel = subject || supplierName || 'N/A';
            const subjectTitle = subject ? 'Target' : 'Supplier';
            const messageHtml = `${escapeHtml(message)}<br><br>${escapeHtml(subjectTitle)}: <strong class="approve-supplier-vni-win">${escapeHtml(subjectLabel)}</strong>`;

            showConfirmModal({
                title: title,
                message: message,
                messageHtml: messageHtml,
                kind: kind,
                onConfirm: function () {
                    submitFormByHandler(handler, { skipValidation: skipValidation });
                }
            });
        });
    }

    function initializePage() {
        bindConfirmButtons();
    }

    $(document).ready(function () {
        initializePage();
    });
})();
