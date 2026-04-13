# Purchase Requisition - Logic nghiệp vụ hiện tại

## 1. Mục đích chức năng

`Purchase Requisition` dùng để lập, theo dõi, cập nhật và duyệt phiếu đề nghị mua hàng.

Chức năng hiện tại hỗ trợ các nghiệp vụ chính sau:

- Xem danh sách phiếu PR.
- Tìm kiếm và phân trang danh sách PR.
- Tạo mới PR thủ công.
- Tạo nhanh PR từ các dòng MR qua chức năng `Add AT`.
- Xem, cập nhật và duyệt PR theo workflow `PU -> CFO -> BOD`.
- Đưa một dòng chi tiết từ PR trở lại MR bằng chức năng `To MR`.
- Quản lý file đính kèm của PR.
- Xem popup `View Detail` để tra cứu danh sách `PC_PRDetail` bằng ajax.
- Xuất Excel danh sách PR hoặc xuất Excel chi tiết của một PR.

## 2. Các bảng dữ liệu chính

### 2.1. Header PR

Bảng: `dbo.PC_PR`

Các cột đang được dùng chính:

- `PRID`
- `RequestNo`
- `RequestDate`
- `Description`
- `Currency`
- `Status`
- `PurId`
- `PurApproDate`
- `CAId`
- `CAApproDate`
- `GDId`
- `GDApproDate`
- `IsAuto`
- `PostPO`
- `edited`

### 2.2. Chi tiết PR

Bảng: `dbo.PC_PRDetail`

Các cột đang được dùng chính:

- `RecordID`
- `PRID`
- `ItemID`
- `Quantity`
- `UnitPrice`
- `OrdAmount`
- `Remark`
- `RecQty`
- `MRRequestNO`
- `SugQty`
- `SupplierID`
- `PoQuantitySug`
- `MRDetailID`

### 2.3. Trạng thái PR

Bảng: `dbo.PC_PRStatus`

Trạng thái đang dùng:

- `1 = New`
- `2 = Waiting For Approve`
- `3 = Pending`
- `4 = Done`

### 2.4. Nguồn dữ liệu MR dùng cho Add AT và Add Detail

Các bảng liên quan:

- `dbo.MATERIAL_REQUEST`
- `dbo.MATERIAL_REQUEST_DETAIL`
- `dbo.INV_ItemList`

### 2.5. File đính kèm

Bảng lưu metadata file:

- `dbo.PC_PR_Doc`

Thư mục lưu file vật lý đọc từ cấu hình:

- `FileUploads:FilePath`

## 3. Màn hình hiện có

## 3.1. Màn hình danh sách PR

File chính:

- `Pages/Purchasing/PurchaseRequisition/Index.cshtml`
- `Pages/Purchasing/PurchaseRequisition/Index.cshtml.cs`
- `wwwroot/js/Purchasing/PurchaseRequisition/index.js`

Màn hình này hỗ trợ:

- Search theo `Request No.`, `Description`, `Status`, khoảng ngày `RequestDate`.
- Phân trang danh sách.
- `Add New`.
- `Add AT`.
- `Export Excel`.
- `View Detail` bằng modal ajax.
- Mở trực tiếp từng PR bằng cột `No.`.

### Rule mở link ở cột `No.`

Thứ tự ưu tiên hiện tại:

1. `mode=edit`
2. `mode=approve`
3. `mode=view`

Nghĩa là:

- Nếu row được phép `edit` thì `No.` mở `edit`.
- Nếu không được `edit` nhưng đang đúng bước workflow để duyệt thì `No.` mở `approve`.
- Nếu không có hai quyền trên nhưng có quyền xem thì `No.` mở `view`.
- Nếu không có quyền thì `No.` chỉ là text thường.

## 3.2. Màn hình chi tiết PR

File chính:

- `Pages/Purchasing/PurchaseRequisition/PurchaseRequisitionDetail.cshtml`
- `Pages/Purchasing/PurchaseRequisition/PurchaseRequisitionDetail.cshtml.cs`
- `wwwroot/js/Purchasing/PurchaseRequisition/PurchaseRequisitionDetail.js`

Header đang dùng:

- `No.`
- `Date`
- `Status`
- `Currency`
- `Description`

Danh sách item đang dùng:

- `Item Code`
- `Item Name`
- `Unit`
- `QtyFromM`
- `QtyPur`
- `U.Price`
- `Amount`
- `Remark`
- `Supplier`

## 4. Quyền và logic theo trạng thái

Function của Purchase Requisition là:

- `FUNCTION_ID = 72`

Các mã quyền đang dùng:

- `1 = View List`
- `2 = View Detail`
- `3 = Add / Add AT`
- `4 = Edit`
- `5 = Approve / Disapprove`
- `6 = Change Status`

## 4.1. Quyền hiệu lực theo `SecurityService.FilterPurchaseRequisition`

### Trạng thái `New (1)`

Được phép:

- `View Detail (2)`
- `Add (3)`
- `Edit (4)`
- `Approve (5)`
- `Change Status (6)`

### Trạng thái `Waiting For Approve (2)`

Được phép:

- `View Detail (2)`
- `Add (3)`
- `Approve (5)`

Không được:

- `Edit (4)`
- `Change Status (6)`

### Trạng thái `Pending (3)`

Được phép:

- `View Detail (2)`
- `Add (3)`
- `Change Status (6)`

Không được:

- `Edit (4)`
- `Approve (5)`

### Trạng thái `Done (4)`

Được phép:

- `View Detail (2)`
- `Add (3)`

Không được:

- `Edit`
- `Approve`
- `Change Status`

## 4.2. Rule `mode=edit` ở màn hình detail

`mode=edit` chỉ mở được trong 2 trường hợp:

- `Status = 1` và user có `PermissionEdit`
- `Status = 3` và user có `PermissionChangeStatus` đồng thời là `PU/CFO/BOD/Admin`

Giải thích:

- `Status = 3 (Pending)` được vào `edit` không phải để sửa dữ liệu chứng từ, mà để dùng `CST New`.
- Trong `Pending`, form vẫn bị khóa dữ liệu theo logic readonly.

## 4.3. Rule `mode=approve`

`mode=approve` hiển thị giống `view`, nhưng có thêm nút `Approve` và `Disapprove` theo đúng bước workflow.

Điều kiện truy cập:

- Phải có `PermissionViewDetail`.
- Đồng thời phải đúng bước workflow hiện tại.
- Nếu không đúng bước thì bị trả về danh sách.

## 5. Workflow duyệt PR

Workflow hiện tại là:

1. `PU` tạo PR và duyệt bước đầu.
2. `CFO` duyệt bước thứ hai.
3. `BOD` duyệt bước cuối.

## 5.1. Xác định vai trò workflow

Vai trò lấy từ bảng `MS_Employee`:

- `IsPurchaser`
- `IsCFO`
- `IsBOD`

## 5.2. Approve

### Bước 1: PU Approve

Điều kiện:

- `Status = 1`
- user là `PU` hoặc `Admin`

Khi approve:

- cập nhật `PC_PR.Status = 2`
- ghi `PurId`
- ghi `PurApproDate`
- gửi mail cho `CFO`

### Bước 2: CFO Approve

Điều kiện:

- `Status = 2`
- `CAId` chưa có
- user là `CFO` hoặc `Admin`

Khi approve:

- ghi `CAId`
- ghi `CAApproDate`
- không đổi trạng thái
- gửi mail cho `BOD`

### Bước 3: BOD Approve

Điều kiện:

- `Status = 2`
- `CAId` đã có
- `GDId` chưa có
- user là `BOD` hoặc `Admin`

Khi approve:

- ghi `GDId`
- ghi `GDApproDate`
- cập nhật `Status = 4`

## 5.3. Disapprove

### CFO Disapprove

Điều kiện:

- đúng bước `CFO`

Khi disapprove:

- cập nhật `Status = 3`
- ghi `CAId`
- ghi `CAApproDate`

### BOD Disapprove

Điều kiện:

- đúng bước `BOD`

Khi disapprove:

- cập nhật `Status = 3`
- ghi `GDId`
- ghi `GDApproDate`

## 5.4. CST New

`CST New` dùng để đưa PR từ `Pending` về `New`.

Điều kiện hiển thị nút:

- PR đã tồn tại
- `Status = 3`
- user có `PermissionChangeStatus (6)`
- user là `PU/CFO/BOD/Admin`

Khi chạy:

- cập nhật `Status = 1`
- xóa thông tin workflow:
  - `PurId`, `PurApproDate`
  - `CAId`, `CAApproDate`
  - `GDId`, `GDApproDate`
- set `edited = 1`

## 6. Logic tạo mới PR

## 6.1. Add New

Tại màn detail khi tạo mới:

- `RequestNo` sinh bằng stored procedure:
  - `EXEC dbo.HaiAutoNumPR NULL`
- `Date` mặc định là ngày hiện tại
- `Currency` mặc định là `1`
- `Status` mặc định là `1`

Khi lưu:

- insert vào `PC_PR`
- insert các dòng item vào `PC_PRDetail`
- nếu có dòng mới đi từ MR thì cập nhật ngược lại MR theo rule ở mục 8

## 6.2. Add AT

`Add AT` dùng để tạo nhanh PR từ danh sách dòng MR đủ điều kiện.

Nguồn dữ liệu popup `Add AT` lấy từ:

- `MATERIAL_REQUEST`
- `MATERIAL_REQUEST_DETAIL`
- `INV_ItemList`

Điều kiện load:

- `BUY > 0`
- `PostedPR = 0` hoặc `NULL`
- và thỏa một trong hai điều kiện:
  - `MATERIALSTATUSID = 3`
  - hoặc `MATERIALSTATUSID = 2` và `IS_AUTO = 1`

Khi bấm `OK` trong `Add AT`:

- tạo mới một bản ghi `PC_PR`
- `IsAuto = 1`
- `PostPO = 0`
- insert từng dòng vào `PC_PRDetail`
- nếu có file đính kèm thì lưu luôn vào `PC_PR_Doc`
- sau đó chuyển sang trang detail ở `mode=edit`

## 7. Logic lưu chi tiết PR ở màn detail

## 7.1. Dòng item đã có sẵn trong PR

Hiện tại chỉ cho `PU` sửa trực tiếp:

- `U.Price`
- `Remark`

Không cho sửa nữa:

- `QtyPur`

Khi lưu các dòng đã có sẵn:

- chỉ `UPDATE` trên `PC_PRDetail`
- cập nhật:
  - `UnitPrice`
  - `Remark`
  - `OrdAmount = Quantity * UnitPrice`
- không check ngược lại `MR`
- không cập nhật `BUY/PostedPR` của MR cho các dòng đã tồn tại

## 7.2. Dòng item mới add từ modal `Add Detail`

Nguồn dữ liệu `Add Detail` dùng cùng source với `Add AT`:

- `MATERIAL_REQUEST`
- `MATERIAL_REQUEST_DETAIL`
- `INV_ItemList`

Các item đã nằm trong PR sẽ không còn hiện lại trong modal `Add Detail`.

Các item đã add tạm nhưng chưa lưu cũng bị loại khỏi modal ngay để tránh add lặp.

Khi lưu:

- backend tách `detail cũ` và `detail mới`
- `detail cũ` chỉ update ở `PC_PRDetail`
- `detail mới` sẽ:
  - được gộp trước khi save nếu trùng cùng `MRDetailId`, hoặc cùng `ItemId + SupplierId` cho dòng không có MR
  - insert vào `PC_PRDetail`
  - cập nhật ngược lại MR theo rule ở mục 8

## 8. Logic cập nhật ngược lại MR

Đây là rule nghiệp vụ đang bám đúng theo logic cũ của hệ thống:

- nếu `SugBuy < BUY`:
  - `UPDATE MATERIAL_REQUEST_DETAIL.BUY = BUY - SugBuy`
- nếu `SugBuy >= BUY`:
  - `UPDATE MATERIAL_REQUEST_DETAIL.PostedPR = 1`

Rule này đang áp dụng cho:

- `Add AT`
- `Add Detail` với các dòng item mới đi từ MR

Lưu ý:

- Với các dòng đã có sẵn trong PR, khi chỉ sửa `U.Price` và `Remark`, hệ thống không cập nhật lại MR.

## 9. Logic To MR

`To MR` dùng để đưa một dòng item từ PR trở lại MR.

Điều kiện:

- PR đã tồn tại
- `Status = 1`
- user có quyền lưu chứng từ
- chọn đúng một dòng chi tiết có `MRDetailID`

Khi chạy:

- đọc `ItemID`, `MRDetailID`, `Quantity`, `SugQty` từ `PC_PRDetail`
- nếu `Quantity < SugQty`:
  - cộng trả lại `BUY = BUY + Quantity`
  - set `PostedPR = 0`
- nếu `Quantity >= SugQty`:
  - chỉ set `PostedPR = 0`
- sau đó xóa dòng khỏi `PC_PRDetail` theo:
  - `PRID`
  - `ItemID`
  - `MRDetailID`

## 10. View Detail trên màn danh sách

Nút `View Detail` nằm ở màn danh sách PR.

Hành vi hiện tại:

- nút luôn active nếu user có quyền `View Detail` ở mức page.
- nếu chưa chọn row nào:
  - mở modal và load toàn bộ `PC_PRDetail` thuộc các PR mà user có quyền xem.
- nếu đã chọn một PR:
  - modal tự fill `Request No.` của PR đó và lọc theo số phiếu.

Dữ liệu popup lấy từ:

- `PC_PR`
- `PC_PRDetail`
- `INV_ItemList`

Filter trong modal:

- `Request No.`
- `Description`
- `Item Code`
- `Rec Qty.` với toán tử so sánh
- `From / To`

Phân trang và search đều chạy bằng `ajax`.

## 11. File đính kèm

Cả `Add AT` và `PurchaseRequisitionDetail` đều dùng cấu hình:

- `FileUploads:FilePath`
- `FileUploads:AllowedExtensions`
- `FileUploads:MaxFileSizeMb`

### Tại Add AT

- cho phép chọn nhiều file
- validate từng file theo extension và dung lượng
- lưu file vật lý vào thư mục cấu hình
- lưu metadata vào `PC_PR_Doc`

### Tại màn detail

- cho phép upload nhiều file
- xem danh sách file đã upload
- tải file về
- xóa nhiều file bằng checkbox + nút `Delete`

Trong `mode=view`:

- vẫn mở được popup `Attached Files`
- chỉ xem và tải file
- không upload, không xóa

## 12. Logic gửi mail workflow

Chức năng gửi mail nằm ở `PurchaseRequisitionDetail.cshtml.cs`.

### Khi nào gửi mail

- `PU approve` -> gửi mail cho `CFO`
- `CFO approve` -> gửi mail cho `BOD`
- `BOD approve` -> không gửi tiếp bước duyệt nào nữa

### Cách lấy người nhận

- `CFO`: lấy từ `MS_Employee.IsCFO = 1`
- `BOD`: lấy từ `MS_Employee.IsBOD = 1`

### Cách gửi

- gửi từng mail riêng cho từng người nhận
- `Dear` theo format:
  - `Dear Tên (Mã nhân viên),`
- có `CC` cố định
- `Open page` trong mail trỏ tới:
  - `PurchaseRequisitionDetail?id={id}&mode=approve`

### Subject test theo cấu hình

Subject không còn hardcode tiền tố `TEST`.

Thay vào đó hệ thống đọc:

- `EmailSettings:TestFunctionIDs`
- `EmailSettings:PrefixSubject`

Nếu `FUNCTION_ID = 72` nằm trong `TestFunctionIDs` thì subject sẽ được thêm tiền tố cấu hình.

## 13. Một số quy ước giao diện và hành vi hiện tại

- `Date` ở detail đang khóa không cho sửa.
- `Currency` được mở lại khi không ở `mode=view`.
- `Total Amount` hiển thị theo format số hiện tại của màn hình:
  - có dấu phẩy hàng nghìn
  - bỏ số `0` dư cuối
  - có thêm `Currency`
- Trong danh sách PR:
  - nếu `No.` mở `edit` hoặc `approve` thì giữ màu link hiện tại
  - nếu chỉ `view` thì hiện màu đen đậm

## 14. Tóm tắt nghiệp vụ ngắn gọn

- `Add AT` dùng để sinh nhanh PR từ MR.
- `Add Detail` ở màn chi tiết cũng lấy cùng nguồn MR như `Add AT`.
- Item cũ trong PR chỉ sửa `giá` và `remark`.
- Item mới thêm từ MR mới làm thay đổi ngược lại `MATERIAL_REQUEST_DETAIL`.
- `To MR` trả item từ PR về MR theo `MRDetailID`.
- Workflow duyệt là `PU -> CFO -> BOD`.
- `Pending` dùng để chờ chỉnh lại và có thể `CST New` về `New`.
- `Done` là trạng thái hoàn tất duyệt.
