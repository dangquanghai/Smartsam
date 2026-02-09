(function () {
    'use strict';
    // ========== DATE RANGE PICKER UTILITIES - STABLE VERSION ==========
    /**
     * Khởi tạo Date Range Picker đơn giản và ổn định
     * @param {string} inputId - ID của input hiển thị
     * @param {string} fromFieldId - ID của hidden field from (optional)
     * @param {string} toFieldId - ID của hidden field to (optional)
     * @param {object} options - Tùy chọn cấu hình
     */
    window.initSimpleDateRange = function (inputId, fromFieldId = null, toFieldId = null, options = {}) {
        const $input = $('#' + inputId);

        // Cấu hình mặc định
        const defaultOptions = {
            locale: {
                format: 'DD/MM/YYYY',
                separator: ' - ',
                applyLabel: 'Apply',
                cancelLabel: 'Clear'
            },
            autoApply: true,
            autoUpdateInput: false,
            showDropdowns: true,
            opens: 'right'
        };

        // Merge options
        const pickerOptions = { ...defaultOptions, ...options };

        // Remove existing picker if any
        if ($input.data('daterangepicker')) {
            $input.data('daterangepicker').remove();
        }

        // Initialize picker
        $input.daterangepicker(pickerOptions);

        // Store the picker instance
        const picker = $input.data('daterangepicker');

        // Apply event
        $input.on('apply.daterangepicker', function (e, picker) {
            // Update visible input
            const displayValue = picker.startDate.format('DD/MM/YYYY') +
                ' - ' +
                picker.endDate.format('DD/MM/YYYY');
            $input.val(displayValue);

            // Update hidden fields if provided
            if (fromFieldId) {
                $(fromFieldId).val(picker.startDate.format('YYYY-MM-DD'));
            }
            if (toFieldId) {
                $(toFieldId).val(picker.endDate.format('YYYY-MM-DD'));
            }
        });

        // Cancel/Clear event
        $input.on('cancel.daterangepicker', function () {
            $input.val('');

            // Clear hidden fields if provided
            if (fromFieldId) {
                $(fromFieldId).val('');
            }
            if (toFieldId) {
                $(toFieldId).val('');
            }
        });

        // Add clear button
        addSimpleClearButton($input, fromFieldId, toFieldId);

        return $input;
    };

    /**
     * Thêm nút Clear đơn giản và ổn định
     */
   function addSimpleClearButton($input, fromFieldId, toFieldId) {
    const $parent = $input.parent();
    
    // Remove existing clear button
    $parent.find('.date-clear-btn').remove();
    
    // Create new clear button với chỉ icon X
    const $clearBtn = $(
        '<button type="button" class="btn btn-sm btn-outline-secondary ml-2 date-clear-btn" title="Clear">' +
            '<i class="fas fa-times"></i>' +  // Chỉ icon, không có chữ
        '</button>'
    );
    
    $parent.append($clearBtn);
    
    $clearBtn.on('click', function (e) {
        e.preventDefault();
        e.stopPropagation();
        
        // Clear the input
        $input.val('');
        
        // Clear hidden fields
        if (fromFieldId) $(fromFieldId).val('');
        if (toFieldId) $(toFieldId).val('');
        
        // Manually trigger cancel event
        $input.trigger('cancel.daterangepicker');
        
        // Hide the picker if open
        const picker = $input.data('daterangepicker');
        if (picker && picker.container && picker.container.is(':visible')) {
            picker.hide();
        }
    });
}

    /**
     * Lấy giá trị từ Date Range Picker
     */
    window.getDateRangeValue = function (inputId) {
        const $input = $('#' + inputId);
        const picker = $input.data('daterangepicker');

        if (!picker || !$input.val() || $input.val().trim() === '') {
            return null;
        }

        try {
            return {
                start: picker.startDate.format('YYYY-MM-DD'),
                end: picker.endDate.format('YYYY-MM-DD'),
                display: $input.val()
            };
        } catch (e) {
            console.error('Error getting date range value:', e);
            return null;
        }
    };

    /**
     * Set giá trị cho Date Range Picker
     */
    window.setDateRangeValue = function (inputId, startDate, endDate) {
        const $input = $('#' + inputId);
        const picker = $input.data('daterangepicker');

        if (!picker) return false;

        try {
            const start = moment(startDate);
            const end = moment(endDate);

            if (!start.isValid() || !end.isValid()) {
                console.error('Invalid date format');
                return false;
            }

            picker.setStartDate(start);
            picker.setEndDate(end);

            // Trigger apply để update UI
            $input.trigger('apply.daterangepicker', picker);
            return true;
        } catch (e) {
            console.error('Error setting date range:', e);
            return false;
        }
    };

    // ========== SELECT2  UTILITIES ==========
    // ========== SELECT2 UTILITIES (SECURE VERSION) ==========
    window.initSelect2 = function (elementId, type) {
        $(elementId).select2({
            width: '100%',
            // AdminLTE 3 đi kèm với CSS cho select2 nhưng thường cần định nghĩa rõ
            placeholder: 'Chọn dữ liệu...',
            allowClear: true,
            minimumResultsForSearch: 0, // Luôn hiện ô search
            ajax: {
                url: '/api/Lookup/' + type,
                dataType: 'json',
                delay: 250,
                data: function (params) {
                    return { term: params.term || "" };
                },
                processResults: function (data) {
                    return { results: data };
                },
                cache: true
            }
        });

        // Fix lỗi focus ô search trên mobile/AdminLTE
        $(elementId).on('select2:open', function () {
            document.querySelector('.select2-search__field').focus();
        });
    };

    window.setSelect2Default = function (elementId, id, text) {
        if (id && text) {
            var option = new Option(text, id, true, true);
            $(elementId).append(option).trigger('change');
        }
    };



})();
