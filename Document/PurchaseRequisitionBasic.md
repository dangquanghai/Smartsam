# Purchase Requisition - Mô tả cơ bản

## 1. Mục đích chức năng

`Purchase Requisition` dùng để lập phiếu đề nghị mua hàng.

Trong hệ thống hiện tại, page đang hỗ trợ các nghiệp vụ chính sau:

- Tìm kiếm danh sách phiếu đề nghị mua hàng.
- Tạo mới phiếu PR thủ công.
- Sửa phiếu PR.
- Xem chi tiết phiếu PR.
- Thêm dòng chi tiết trực tiếp trong form detail.
- Thêm nhanh chi tiết bằng chức năng `Add AT` từ màn hình danh sách.

Ghi chú:

- Dữ liệu thực tế trong DB cho thấy bảng `PC_PR` vẫn còn các cột liên quan đến quy trình từ `MR` như `MRNo`, `MRRequestNO`.
- Tuy nhiên page hiện tại chưa có luồng tự động sinh PR từ MR. Phần này mới dừng ở mức lưu dấu vết dữ liệu/cột dự phòng.

## 2. Màn hình hiện có

### 2.1. Màn hình danh sách

File:

- `Pages/Purchasing/PurchaseRequisition/Index.cshtml`
- `Pages/Purchasing/PurchaseRequisition/Index.cshtml.cs`
- `wwwroot/js/Purchasing/PurchaseRequisition/index.js`

Chức năng:

- Search theo:
  - `Request No`
  - `Description`
  - khoảng ngày `RequestDate`
- Hiển thị danh sách:
  - `No.`
  - `Date`
  - `Description`
  - `Status`
  - `Purchaser`
  - `Chief_A`
  - `G.Director`
- Cho phép:
  - `Add New`
  - `Edit`
  - `View`
  - `View Detail`
  - `Add AT`

Ghi chú:

- `Export Excel` và `Disapproval` hiện mới có nút trên UI, JS đang báo `not implemented yet`.

### 2.2. Màn hình chi tiết

File:

- `Pages/Purchasing/PurchaseRequisition/PurchaseRequisitionDetail.cshtml`
- `Pages/Purchasing/PurchaseRequisition/PurchaseRequisitionDetail.cshtml.cs`
- `wwwroot/js/Purchasing/PurchaseRequisition/PurchaseRequisitionDetail.js`

Thông tin header đang nhập:

- `No.`
- `Currency`
- `Date`
- `Status`
- `Description`

Thông tin detail đang nhập:

- `Item`
- `Unit`
- `Supplier`
- `QtyFromM`
- `QtyPur`
- `U.Price`
- `Amount`
- `Remark`

## 3. Bảng dữ liệu chính

### 3.1. Header PR

Bảng: `dbo.PC_PR`

Một số cột đang được page sử dụng thực tế:

- `PRID`
- `RequestNo`
- `RequestDate`
- `Description`
- `Currency`
- `Status`
- `IsAuto`
- `MRNo`
- `PostPO`
- `PurId`
- `PurApproDate`
- `CAId`
- `CAApproDate`
- `GDId`
- `GDApproDate`
- `noted`
- `edited`

### 3.2. Chi tiết PR

Bảng: `dbo.PC_PRDetail`

Các cột chính:

- `RecordID`
- `PRID`
- `ItemID`
- `Quantity`
- `UnitPrice`
- `Remark`
- `RecQty`
- `OrdAmount`
- `RecAmount`
- `RecDate`
- `POed`
- `MRRequestNO`
- `SugQty`
- `SupplierID`
- `PoQuantity`
- `PoQuantitySug`
- `MRDetailID`

### 3.3. Trạng thái PR

Bảng: `dbo.PC_PRStatus`

Trạng thái thực tế hiện có:

- `0 = Preparing`
- `1 = Done`
- `2 = In Progress`
- `3 = Disapproved`

### 3.4. Master item

Bảng: `dbo.INV_ItemList`

Page hiện lấy item theo điều kiện:

- `IsPurchase = 1`

Cột đang dùng:

- `ItemID`
- `ItemCode`
- `ItemName`
- `Unit`

### 3.5. Master supplier

Bảng: `dbo.PC_Suppliers`

Cột đang dùng:

- `SupplierID`
- `SupplierCode`
- `SupplierName`

Ghi chú:

- Page hiện tại load supplier không lọc theo `IsDeleted` hay `Status`.
- Nghĩa là đang lấy toàn bộ supplier và sort theo `SupplierCode`.

## 4. Quan hệ dữ liệu

Quan hệ đang có trong DB:

- `PC_PR.Status` -> `PC_PRStatus.PRStatusID`
- `PC_PR.PurId` -> `MS_Employee.EmployeeID`
- `PC_PR.CAId` -> `MS_Employee.EmployeeID`
- `PC_PR.GDId` -> `MS_Employee.EmployeeID`
- `PC_PRDetail.PRID` -> `PC_PR.PRID`
- `PC_PRDetail.ItemID` -> `INV_ItemList.ItemID`
- `PC_PRDetail.SupplierID` -> `PC_Suppliers.SupplierID`

Hiểu ngắn gọn:

- `PC_PR` là header của phiếu.
- `PC_PRDetail` là các dòng vật tư/hàng hóa của phiếu.
- Mỗi dòng PR detail tham chiếu tới 1 item và có thể chọn 1 supplier.

## 5. Mapping field của form detail

### 5.1. Item

Nguồn:

- `dbo.INV_ItemList`

Điều kiện lấy:

- `IsPurchase = 1`

Lưu vào:

- `PC_PRDetail.ItemID`

### 5.2. Unit

Nguồn:

- lấy từ item đã chọn (`INV_ItemList.Unit`)

Hành vi:

- tự fill, readonly trên form

Không lưu riêng vì:

- đơn vị được hiểu theo item

### 5.3. Supplier

Nguồn:

- `dbo.PC_Suppliers`

Lưu vào:

- `PC_PRDetail.SupplierID`

### 5.4. QtyFromM

Người dùng nhập.

Lưu vào:

- `PC_PRDetail.SugQty`
- `PC_PRDetail.PoQuantitySug`

Ghi chú:

- tên field trên UI là `QtyFromM`
- nhưng trong detail thực tế đang map vào cột suggestion quantity

### 5.5. QtyPur

Người dùng nhập.

Lưu vào:

- `PC_PRDetail.Quantity`

### 5.6. U.Price

Người dùng nhập.

Lưu vào:

- `PC_PRDetail.UnitPrice`

### 5.7. Amount

Frontend:

- tự tính `QtyPur * U.Price`
- readonly

Backend:

- tính lại khi save
- lưu vào `PC_PRDetail.OrdAmount`

### 5.8. Remark

Người dùng nhập.

Lưu vào:

- `PC_PRDetail.Remark`

## 6. Logic lưu dữ liệu hiện tại

### 6.1. Tạo mới PR

Khi add mới:

- sinh `RequestNo` theo mẫu `PRxx/MMyy`
- insert header vào `PC_PR`
- gán:
  - `IsAuto = 0`
  - `MRNo = NULL`
  - `PostPO = 0`
  - `PurId` = user đang login
  - nếu không tìm được user đang login thì fallback `FD031`, sau đó `FD031X`
- sau đó insert toàn bộ dòng detail vào `PC_PRDetail`

### 6.2. Sửa PR

Khi edit:

- update lại header:
  - `RequestNo`
  - `RequestDate`
  - `Description`
  - `Currency`
  - `Status`
  - `edited = 1`
- xóa toàn bộ detail cũ của PR
- insert lại toàn bộ detail mới từ UI

Ghi chú:

- đây là kiểu save replace-all, không phải update từng dòng detail riêng lẻ

### 6.3. Add AT

Chức năng `Add AT` ở màn hình danh sách:

- chọn đúng 1 PR
- nhập thêm các dòng detail
- insert bổ sung trực tiếp vào `PC_PRDetail`
- không sửa header

## 7. Quyền hiện tại của chức năng

Function:

- `SYS_RolePermission.FunctionID = 72`

Các mã quyền đang thấy trong DB/code:

- `1`: quyền vào trang/list
- `2`: view detail
- `3`: add new
- `4`: edit / add AT
- `5`: có trong DB nhưng page hiện tại chưa triển khai hành động riêng trên UI
- `6`: nút `Disapproval` có hiển thị theo quyền nhưng chưa có xử lý thực tế

Trong code hiện tại:

- List page dùng `PermissionService`
- Detail page dùng `PermissionService`
- Nếu admin (`IsAdminRole = True`) thì đang được cấp full quyền giả lập

## 8. Workflow nghiệp vụ đang thấy từ data

Dựa trên dữ liệu thật trong `PC_PR`, có thể đọc được luồng nghiệp vụ cơ bản như sau:

### Bước 1. Purchaser tạo PR

- `PurId` là người lập/chịu trách nhiệm đầu
- ví dụ dữ liệu thật đang dùng nhiều:
  - `FD031`

### Bước 2. Chief Accountant kiểm tra

- `CAId` là người kiểm tra kế toán
- ví dụ dữ liệu thật:
  - `FD001X`

### Bước 3. General Director/BOD phê duyệt

- `GDId` là người duyệt cuối
- ví dụ dữ liệu thật:
  - `BOD011`

### Bước 4. Hoàn tất hoặc trả lại

Các trạng thái đang thấy:

- `0 - Preparing`: đang soạn / chờ xử lý tiếp
- `2 - In Progress`: đã đi vào luồng xử lý nhưng chưa hoàn tất
- `1 - Done`: hoàn tất duyệt
- `3 - Disapproved`: bị trả lại / không duyệt

Ghi chú rất quan trọng:

- workflow này thấy rõ trong dữ liệu thật của bảng `PC_PR`
- nhưng page hiện tại chưa có code submit/approve/disapprove hoàn chỉnh trên UI
- nghĩa là dữ liệu và schema đã có luồng nghiệp vụ, còn màn hình hiện tại mới triển khai phần lập phiếu và nhập dòng chi tiết là chính

## 9. Những gì page hiện tại chưa làm

So với dữ liệu và ý nghĩa nghiệp vụ tổng thể, page hiện tại chưa triển khai hoặc mới dừng ở mức placeholder:

- chưa có luồng `Submit by Purchaser`
- chưa có luồng `Check by Chief of accounting`
- chưa có luồng `Approval by BOD`
- chưa có `Export Excel` thật
- chưa có `Disapproval` thật
- chưa có luồng tự sinh PR từ MR
- chưa có liên kết thật sang PO dù bảng còn cột `PostPO`, `POed`, `PoQuantity`

## 10. Kết luận ngắn

`Purchase Requisition` hiện tại là chức năng lập và quản lý phiếu đề nghị mua hàng với 2 lớp dữ liệu:

- `PC_PR`: header phiếu
- `PC_PRDetail`: chi tiết vật tư/hàng hóa

Page hiện chạy tốt cho các nghiệp vụ:

- search list
- add new
- edit
- view
- add detail
- add AT

Workflow duyệt nhiều cấp đã có dấu vết rõ trong DB:

- Purchaser
- Chief Accountant
- General Director/BOD

Nhưng phần UI/code hiện tại mới triển khai một phần đầu của quy trình, chưa triển khai đầy đủ luồng duyệt và chuyển tiếp nghiệp vụ.
