$(document).ready(function () {
    
    initializePage(); // <--- QUAN TRỌNG: Phải gọi thì alert mới hiện!
    // ========== XỬ LÝ AJAX CASCADING DROPDOWN ==========
    $('#AgentCompanyId').on('change', function () {
        var companyId = $(this).val();
        var $personSelect = $('#AgentPersonId');

        // 1. Reset trạng thái thẻ Person
        $personSelect.empty().append('<option value="">-- Select Person --</option>');

        // Nếu không chọn công ty nào thì dừng lại
        if (!companyId) return;

        // 2. Gọi Ajax lấy dữ liệu
        $.ajax({
            url: '/Sales/STContract/GetAgentPersons', // Đường dẫn tới Action bạn vừa tạo
            type: 'GET',
            data: { companyId: companyId },
            success: function (data) {
                // 'data' lúc này là danh sách SelectListItem từ Helper C#
                if (data && data.length > 0) {
                    $.each(data, function (i, item) {
                        // Lưu ý: SelectListItem trả về JSON thường có key là 'text' và 'value' (viết thường)
                        // Hoặc 'Text' và 'Value' (viết hoa) tùy cấu hình JSON của dự án.
                        // Ta dùng toán tử || để "bắt" cả 2 trường hợp cho chắc chắn.
                        var optionText = item.text || item.Text;
                        var optionValue = item.value || item.Value;

                        $personSelect.append(new Option(optionText, optionValue));
                    });
                }
            },
            error: function (xhr) {
                console.error("Lỗi khi tải danh sách nhân viên:", xhr.responseText);
            }
        });
    });


    // ========== INITIALIZE ==========
    function initializePage() {
        console.log('Initializing page components...');

        // Kiểm tra xem jQuery và Select2 đã sẵn sàng chưa
        if (typeof window.initSelect2 !== 'function') {
            console.error("Lỗi: Hàm window.initSelect2 chưa được nạp từ common.js!");
            return;
        }
        // 1. SELECT2 CHO COMPANY 
        window.initSelect2('#CompanyId', 'company');
        window.initSelect2('#AgentCompanyId', 'company');
    }

    // 1. Tính toán ngay khi đang gõ (Dùng số thuần để tính)
    $('#CurrentRentRate, #PerVAT').on('input', function () {
        calculateNetPrice();
    });

    // 2. Định dạng dấu phẩy khi rời chuột đi (blur)
    $('#CurrentRentRate').on('blur', function () {
        let value = $(this).val();
        if (value) {
            let num = parseFloat(value.replace(/[^0-9.]/g, '')) || 0;
            $(this).val(num.toLocaleString('en-US'));
        }
    });

    // 3. Khi nhấn vào lại ô đó (focus), xóa dấu phẩy
    $('#CurrentRentRate').on('focus', function () {
        let value = $(this).val();
        if (value) {
            $(this).val(value.replace(/,/g, ''));
        }
    });

    function calculateNetPrice() {
        let grossValue = $('#CurrentRentRate').val() || "0";
        let cleanGross = grossValue.toString().replace(/[^0-9.]/g, '');
        let grossPrice = parseFloat(cleanGross) || 0;

        let vatValue = $('#PerVAT').val() || "0";
        let vat = parseFloat(vatValue.toString().replace(/[^0-9.]/g, '')) || 0;

        if (grossPrice > 0) {
            let netPrice = grossPrice / (1 + (vat / 100));
            $('#TotalPriceExcVAT').val(Math.round(netPrice).toLocaleString('en-US'));
        } else {
            $('#TotalPriceExcVAT').val('0');
        }
    }
    $('#AgentCompanyId').on('change', function () {
        var companyId = $(this).val();
        var $personSelect = $('#AgentPersonId');

        // Reset về trạng thái chờ
        $personSelect.empty().append('<option value="">-- Loading... --</option>');

        if (companyId) {
            $.ajax({
                url: '/Sales/STContract/GetPersonsByCompany',
                type: 'GET',
                data: { companyId: companyId },
                success: function (response) {
                    $personSelect.empty().append('<option value="">-- Select Person --</option>');

                    if (response && response.length > 0) {
                        $.each(response, function (i, item) {
                            // Thêm option vào thẻ select thường
                            $personSelect.append(new Option(item.name, item.id));
                        });
                    }
                },
                error: function () {
                    $personSelect.empty().append('<option value="">-- Error loading data --</option>');
                }
            });
        } else {
            $personSelect.empty().append('<option value="">-- Select Person --</option>');
        }
    });


});