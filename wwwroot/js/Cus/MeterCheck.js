(function () {

    const pageUrl = window.location.pathname;

    function getToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    }

    /* ================= RECOGNIZE ================= */
    async function recognizeSelected() {
        const statusDiv = document.getElementById("statusMessage");
        statusDiv.style.display = "block";
        statusDiv.innerText = "⏳ Hệ thống đang nhận dạng, vui lòng chờ...";

        const selectedIds = Array.from(
            document.querySelectorAll("input[name$='.Id']")
        ).map(i => parseInt(i.value));

        if (selectedIds.length === 0) {
            alert("Không tìm thấy bản ghi nào.");
            return;
        }

        try {
            const response = await fetch(pageUrl + "?handler=RecognizeAjax", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": getToken()
                },
                body: JSON.stringify(selectedIds)
            });

            const data = await response.json();

            if (response.status === 401) {
                alert(data.message || "Không có quyền thực hiện");
                return;
            }

            if (data.success) {
                statusDiv.innerText = "✅ Nhận dạng xong!";
                location.reload();
            } else {
                statusDiv.innerText = "❌ Lỗi: " + (data.message || "Có lỗi xảy ra");
            }
        } catch (err) {
            console.error(err);
            statusDiv.innerText = "❌ Không thể kết nối server.";
        }
    }

    /* ================= UPDATE ALL ================= */

    async function updateAll() {
        const statusDiv = document.getElementById("statusMessageEnd");
        statusDiv.style.display = "block";
        statusDiv.innerText = "⏳ Đang cập nhật dữ liệu, vui lòng chờ...";

        const records = [];

        document.querySelectorAll("tbody tr").forEach(tr => {
            records.push({
                Id: parseInt(tr.querySelector('input[name$=".Id"]').value),
                ApartmentCode: tr.querySelector('input[name$=".ApartmentCode"]').value,
                ElectricIndex: parseInt(tr.querySelector('input[name$=".ElectricIndex"]').value || 0),
                RawText: tr.querySelector('textarea[name$=".RawText"]').value,
                FileName: tr.querySelector('input[name$=".FileName"]').value
            });
        });

        const btn = document.getElementById("btnUpdateAll");
        btn.disabled = true;
        btn.innerText = "Đang cập nhật...";

        const response = await fetch(pageUrl + "?handler=UpdateAllAjax", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": getToken()
            },
            body: JSON.stringify(records)
        });

        const data = await response.json();

        if (data.success) {
            statusDiv.innerText = "✅ Cập nhật xong!";
            location.reload();
        } else {
            statusDiv.innerText = "❌ Lỗi: " + (data.message || "Có lỗi xảy ra");
            btn.disabled = false;
            btn.innerText = "✅ Update All";
        }
    }

    /* ================= IMAGE ZOOM ================= */

    function initImageZoom() {
        document.querySelectorAll(".zoomable").forEach(img => {
            img.addEventListener("click", () => {
                img.classList.toggle("zoomed");
            });
        });
    }

    /* ================= EMAIL POPUP ================= */

    function initApartmentDblClick() {
        document.querySelectorAll("input[name$='.ApartmentCode']").forEach(input => {
            input.addEventListener("dblclick", function () {
                const tr = this.closest("tr");
                document.getElementById("popupFileName").value =
                    tr.querySelector("input[name$='.FileName']").value;
                document.getElementById("popupApartment").value = this.value;
                document.getElementById("emailPopup").style.display = "block";
            });
        });
    }

    async function sendEmail() {
        const email = document.getElementById("emailInput").value;
        if (!email) {
            alert("⚠️ Vui lòng nhập Email!");
            return;
        }

        const payload = {
            email,
            fileName: document.getElementById("popupFileName").value,
            apartment: document.getElementById("popupApartment").value
        };

        const response = await fetch(pageUrl + "?handler=SendEmail", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": getToken()
            },
            body: JSON.stringify(payload)
        });

        const result = await response.json();
        if (result.success) {
            alert("📨 Email sent successfully!");
            closeEmailPopup();
        } else {
            alert("❌ Error: " + result.message);
        }
    }

    function closeEmailPopup() {
        document.getElementById("emailPopup").style.display = "none";
    }

    /* ================= INIT ================= */

    document.addEventListener("DOMContentLoaded", function () {
        document.getElementById("btnRecognize")?.addEventListener("click", recognizeSelected);
        document.getElementById("btnUpdateAll")?.addEventListener("click", updateAll);
        initImageZoom();
        initApartmentDblClick();
    });

    /* expose functions used by HTML (nếu còn) */
    window.sendEmail = sendEmail;
    window.closeEmailPopup = closeEmailPopup;

})();
