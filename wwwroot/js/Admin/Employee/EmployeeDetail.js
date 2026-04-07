
$(document).ready(function () {
    console.log("Employee Detail JS Loaded");

    // Khởi tạo Select2 cho các dropdown nếu cần (trừ Nhóm kho anh đã bảo bỏ)
    if ($('.select2').length > 0) {
        $('.select2').select2({
            theme: 'bootstrap4'
        });
    }
});

// Hàm xem trước ảnh khi chọn từ máy tính
function previewImage(input, previewId, hiddenId) {
    if (input.files && input.files[0]) {
        var reader = new FileReader();

        reader.onload = function (e) {
            // Hiển thị ảnh lên thẻ img
            $('#' + previewId).attr('src', e.target.result);

            // Lưu ý: e.target.result hiện tại là Base64. 
            // Nếu anh muốn lưu link URL, anh cần một API upload ảnh riêng.
            // Tạm thời gán vào hidden field để submit form
            $('#' + hiddenId).val(e.target.result);
        };

        reader.readAsDataURL(input.files[0]);
    }
}

// Hàm hỗ trợ xóa ảnh nếu cần
function resetImage(previewId, hiddenId, defaultSrc) {
    $('#' + previewId).attr('src', defaultSrc);
    $('#' + hiddenId).val('');
}