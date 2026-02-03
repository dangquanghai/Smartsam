(function () {

    const pageUrl = window.location.pathname;

    function getToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    }

    const btn = document.getElementById("btnUpload");
    const input = document.getElementById("fileInput");
    const statusDiv = document.getElementById("status");
    const progressContainer = document.getElementById("progressContainer");
    const progressBar = document.getElementById("progressBar");

    // AdminLTE custom-file-input label
    input.addEventListener("change", function () {
        const count = this.files.length;

        if (count > 0) {
            this.nextElementSibling.innerText = `${count} file đã chọn`;

            statusDiv.innerHTML = `
            <div class="text-info">
                📁 Đã chọn tổng cộng <b>${count}</b> file
            </div>
        `;
        } else {
            this.nextElementSibling.innerText = "Choose files";
            statusDiv.innerHTML = "";
        }
    });


    btn.addEventListener("click", async () => {
        let successCount = 0;
        let failCount = 0;
        const files = input.files;
        if (!files || files.length === 0) {
            alert("Vui lòng chọn ít nhất một ảnh.");
            return;
        }

        btn.disabled = true;
        btn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Đang upload...';
        statusDiv.innerHTML = "";
        progressContainer.style.display = "block";
        progressBar.style.width = "0%";
        progressBar.innerText = "0%";

        let uploaded = 0;

        for (let i = 0; i < files.length; i++) {
            const file = files[i];
            const formData = new FormData();
            formData.append("uploadImage", file);

            try {
                const resp = await fetch('/MeterUpload?handler=UploadSingle', {
                    method: "POST",
                    body: formData,
                    headers: {
                        "RequestVerificationToken": getToken()
                    }
                });

                if (resp.ok) {
                    successCount++;
                    statusDiv.innerHTML += `<div class="text-success">✔ ${file.name} upload thành công</div>`;
                } else {
                    failCount++;
                    statusDiv.innerHTML += `<div class="text-danger">❌ ${file.name} upload thất bại</div>`;
                }
            } catch (err) {
                failCount++;
                console.error(err);
                statusDiv.innerHTML += `<div class="text-danger">❌ ${file.name} lỗi mạng</div>`;
            }

            uploaded++;
            let percent = Math.round((uploaded / files.length) * 100);
            progressBar.style.width = percent + "%";
            progressBar.innerText = percent + "%";
        }
            statusDiv.innerHTML += `
            <hr />
            <div class="alert alert-info">
            <b>Tổng kết:</b><br />
            📁 Tổng file: <b>${files.length}</b><br />
            ✅ Thành công: <b class="text-success">${successCount}</b><br />
            ❌ Thất bại: <b class="text-danger">${failCount}</b>
            </div>
            `;

        btn.disabled = false;
        btn.innerHTML = '<i class="fas fa-upload"></i> Upload';
    });

})();