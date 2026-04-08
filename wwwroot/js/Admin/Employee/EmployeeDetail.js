$(document).ready(function () {
    console.log("Employee Detail JS Loaded");

    // Khởi tạo Select2 cho các dropdown nếu cần
    if ($('.select2').length > 0) {
        $('.select2').select2({
            theme: 'bootstrap4'
        });
    }
});

// Hàm xem trước ảnh khi chọn từ máy tính
function previewImage(input, previewId, hiddenBase64Id) {
    if (input.files && input.files[0]) {
        var reader = new FileReader();
        reader.onload = function (e) {
            $('#' + previewId).attr('src', e.target.result);
            // Gán chuỗi Base64 vào trường ẩn để gửi lên C# xử lý lưu file
            $('#' + hiddenBase64Id).val(e.target.result);
        };
        reader.readAsDataURL(input.files[0]);
    }
}

// Hàm hỗ trợ xóa ảnh
function resetImage(previewId, hiddenId, defaultSrc) {
    $('#' + previewId).attr('src', defaultSrc);
    $('#' + hiddenId).val('');
}

// Sự kiện Click nút Lưu
$('#btnSaveEmployee').on('click', function (e) {
    e.preventDefault();

    // 1. Thu thập dữ liệu
    var dataToPost = new URLSearchParams();

    // Quét toàn bộ input có trong vùng chứa (ví dụ card-body)
    $('.card-body input, .card-body select').each(function () {
        var name = $(this).attr('name');
        if (name) {
            if ($(this).is(':checkbox')) {
                dataToPost.append(name, $(this).is(':checked'));
            } else {
                dataToPost.append(name, $(this).val());
            }
        }
    });

    // 2. Gửi Ajax theo handler 'Save'
    $.ajax({
        url: window.location.pathname + '?handler=Save',
        type: 'POST',
        data: dataToPost.toString(),
        headers: {
            // Đảm bảo trong file .cshtml anh có dòng @Html.AntiForgeryToken()
            "RequestVerificationToken": $('input[name="__RequestVerificationToken"]').val()
        },
        beforeSend: function () {
            $('#btnSaveEmployee').prop('disabled', true).html('<i class="fas fa-spinner fa-spin"></i> Đang lưu...');
        },
        success: function (res) {
            // Vì C# của anh trả về Redirect hoặc JsonResult
            // Nếu anh dùng return JsonResult(new { success = true }) thì check res.success
            if (res.success !== false) {
                alert("Lưu thông tin nhân viên thành công.");
                window.location.href = "/Admin/Employee/Index";
            } else {
                alert("Lỗi: " + res.message);
                $('#btnSaveEmployee').prop('disabled', false).html('<i class="fas fa-save"></i> Save Changes');
            }
        },
        error: function (xhr) {
            console.error(xhr);
            alert("Lỗi kết nối server hoặc lỗi xác thực (400/500).");
            $('#btnSaveEmployee').prop('disabled', false).html('<i class="fas fa-save"></i> Save Changes');
        }
    });
});