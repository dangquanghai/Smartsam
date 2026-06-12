(function (window, $) {
    'use strict';

    function parseDisplayDate(value) {
        var text = String(value || '').trim();
        if (!text) return '';
        var match = text.match(/^(\d{1,2})\/(\d{1,2})\/(\d{4})$/);
        if (!match) return null;
        var day = Number(match[1]);
        var month = Number(match[2]);
        var year = Number(match[3]);
        if (day <= 0 || month <= 0 || month > 12 || year <= 0) return null;
        var date = new Date(year, month - 1, day);
        if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day) return null;
        return year.toString().padStart(4, '0') + '-' + month.toString().padStart(2, '0') + '-' + day.toString().padStart(2, '0');
    }

    function isoToDisplay(value) {
        var text = String(value || '').trim();
        if (!text) return '';
        var match = text.match(/^(\d{4})-(\d{2})-(\d{2})/);
        if (!match) return text;
        return match[3] + '/' + match[2] + '/' + match[1];
    }

    function initDatePicker(scope) {
        if (!$ || !$.fn || !$.fn.datepicker) return;
        $(scope || document).find('.app-date-picker').each(function () {
            var $display = $(this);
            var hiddenSelector = $display.data('hidden-target');
            var $hidden = hiddenSelector ? $(hiddenSelector) : $();

            if ($display.data('datepicker')) {
                $display.datepicker('destroy');
            }

            $display.datepicker({
                dateFormat: 'dd/mm/yy',
                changeMonth: true,
                changeYear: true,
                showButtonPanel: true,
                onSelect: function () {
                    if ($hidden.length) {
                        $hidden.val(parseDisplayDate($display.val()) || '');
                    }
                }
            });

            $display.on('change.appDatePicker', function () {
                if ($hidden.length) {
                    $hidden.val(parseDisplayDate($display.val()) || '');
                }
            });
        });
    }

    window.AppDatePicker = {
        init: initDatePicker,
        parseDisplayDate: parseDisplayDate,
        isoToDisplay: isoToDisplay
    };
})(window, window.jQuery);
