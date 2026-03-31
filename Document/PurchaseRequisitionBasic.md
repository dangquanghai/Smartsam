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
- Tuy nhiên page hiện tại chưa có luồng tự động sinh PR từ MR. Phần này mới dừng ở mức lưu dấu vết dữ liệu hoặc cột dự phòng.

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
  - `View Detail`
  - `Add AT`
  - `Export Excel`

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

Bộ trạng thái đã đổi theo yêu cầu hiện tại:

- `1 = New`
- `2 = Waiting For Approve`
- `3 = Pending`
- `4 = Done`

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

- Page hiện tại đã lọc supplier theo `IsDeleted = 0` ở các chỗ liên quan popup chọn supplier.

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
- `PC_PRDetail` là các dòng vật tư hoặc hàng hóa của phiếu.
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

- sinh `RequestNo` theo mẫu hiện tại của hệ thống
- insert header vào `PC_PR`
- gán:
  - `IsAuto = 0`
  - `MRNo = NULL`
  - `PostPO = 0`
  - `PurId` = user đang login hoặc fallback theo rule hiện tại trong code
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

- Đây là kiểu save replace-all, không phải update từng dòng detail riêng lẻ.

### 6.3. Add AT hiện tại trong code web

Chức năng `Add AT` ở màn hình danh sách hiện đang chạy theo kiểu:

- chọn đúng 1 PR có sẵn trên danh sách
- nhập thêm các dòng detail
- insert bổ sung trực tiếp vào `PC_PRDetail`
- không sửa header
- không tạo mới `PC_PR`

Kết luận:

- `Add AT` hiện tại trên web mới là phiên bản rút gọn, chưa đúng hoàn toàn với flow VB6 cũ.

## 7. Quyền hiện tại của chức năng

Function:

- `SYS_RolePermission.FunctionID = 72`

Các mã quyền đang thấy trong DB và code:

- `1`: quyền vào trang/list
- `2`: view detail
- `3`: add new
- `4`: edit
- `6`: disapproval

Trong code hiện tại:

- List page và detail page đều bám quyền theo service bảo mật hiện tại.
- Nếu admin (`IsAdminRole = true`) thì được cấp full quyền giả lập.

## 8. Workflow nghiệp vụ đang thấy từ data

Dựa trên dữ liệu thật trong `PC_PR`, có thể đọc được luồng nghiệp vụ cơ bản như sau:

### Bước 1. Purchaser tạo PR

- `PurId` là người lập hoặc chịu trách nhiệm đầu.

### Bước 2. Chief Accountant kiểm tra

- `CAId` là người kiểm tra kế toán.

### Bước 3. General Director hoặc BOD phê duyệt

- `GDId` là người duyệt cuối.

### Bước 4. Hoàn tất

- chứng từ đi qua các trạng thái nghiệp vụ tương ứng cho đến khi `Done`.

Ghi chú:

- workflow này thấy rõ trong dữ liệu thật của bảng `PC_PR`
- nhưng page hiện tại chưa có code submit/approve/disapprove hoàn chỉnh trên UI

## 9. Phân tích lại chức năng Add AT theo flow VB6 và ảnh nghiệp vụ

### 9.1. Mục đích thật của Add AT

`Add AT` trong flow cũ không chỉ là thêm vài dòng detail thủ công vào một PR đang có sẵn.
Nó là luồng `Gen PR` để gom các dòng `Material Request` đủ điều kiện thành một phiếu `Purchase Requisition` mới.

Hiểu ngắn gọn:

- Nguồn dữ liệu đầu vào là các dòng `MATERIAL_REQUEST_DETAIL` còn nhu cầu mua.
- Người dùng chọn các dòng cần gom.
- Hệ thống tạo mới một `PC_PR`.
- Sau đó insert các dòng đã chọn vào `PC_PRDetail`.
- Đồng thời cập nhật ngược lại dữ liệu MR để đánh dấu đã đưa vào PR hoặc giảm số lượng còn phải mua.

### 9.2. Flow màn hình Add AT theo code VB6

#### Bước 1. Mở cửa sổ Gen PR

Khi mở form:

- `RequestDate` mặc định = `Now`
- `RequestNo` được sinh tự động bằng `exec HaiAutoNumPR null`
- `Currency` được nạp từ bảng `MS_CurrencyFL`
- Lưới dữ liệu được nạp từ các dòng MR đủ điều kiện

Query nguồn của lưới gồm:

- `MATERIAL_REQUEST_DETAIL.REQUEST_NO`
- `INV_ItemList.ItemID`
- `INV_ItemList.ItemCode`
- `INV_ItemList.ItemName`
- `MATERIAL_REQUEST_DETAIL.BUY`
- `INV_ItemList.UnitPrice`
- `INV_ItemList.Specification`
- `MATERIAL_REQUEST_DETAIL.NOTE`
- `MATERIAL_REQUEST_DETAIL.ID`

Quan hệ lấy dữ liệu:

- `MATERIAL_REQUEST` join `MATERIAL_REQUEST_DETAIL` theo `REQUEST_NO`
- `MATERIAL_REQUEST_DETAIL` join `INV_ItemList` theo `ITEMCODE = ItemCode`

Điều kiện lấy dòng MR:

- `MATERIAL_REQUEST_DETAIL.BUY > 0`
- `PostedPR = 0 OR PostedPR IS NULL`
- và thỏa một trong hai điều kiện:
  - `MATERIAL_REQUEST.MATERIALSTATUSID = 3` và `BUY > 0`
  - hoặc `MATERIAL_REQUEST.MATERIALSTATUSID = 2` và `MATERIAL_REQUEST.IS_AUTO = 1`

Sắp xếp:

- theo `REQUEST_NO`
- rồi `ItemCode`

#### Bước 2. Hệ thống thêm 2 cột thao tác vào lưới

Sau khi bind lưới, chương trình thêm 2 cột động:

- `Check`
  - kiểu checkbox
  - mặc định = `0`
  - dùng để chọn dòng MR sẽ đưa vào PR
- `SugBuy`
  - mặc định = giá trị `BUY`
  - cho phép người dùng sửa lại số lượng đề nghị mua trước khi insert vào `PC_PRDetail`

Ý nghĩa nghiệp vụ:

- `BUY` là số lượng hệ thống đề xuất hoặc còn phải mua từ MR
- `SugBuy` là số lượng thực tế user chốt để đưa vào PR lần này

#### Bước 3. User nhập Description và chọn các dòng MR

User thao tác trên form:

- nhập `Description`
- check chọn các dòng cần đưa vào PR
- có thể sửa `SugBuy` cho từng dòng
- bấm `OK`

#### Bước 4. Nhấn OK để tạo PR mới

Nếu `Description` rỗng:

- hệ thống không cho tạo
- báo `Fill Description`

Nếu `Description` hợp lệ:

- insert mới vào `PC_PR`
- dữ liệu ghi:
  - `RequestNo`
  - `RequestDate`
  - `Description`
  - `Currency = 1`
  - `Status = 1`
- sau đó lấy `PRID` mới tạo bằng `SELECT max(PRID)`
- gọi tiếp `InsertToPR`
- mở màn hình `PR Detail` ở mode `EDIT`

### 9.3. Logic InsertToPR trong flow cũ

Với mỗi dòng MR được check:

- tạo mới một dòng `PC_PRDetail`
- map dữ liệu như sau:
  - `PRID` = PR vừa tạo
  - `ItemID` = từ `INV_ItemList.ItemID`
  - `Quantity` = giá trị `SugBuy`
  - `Remark` = `INV_ItemList.Specification`
  - `UnitPrice` = `INV_ItemList.UnitPrice`, nếu null thì = `0`
  - `OrdAmount` = `UnitPrice * SugBuy`
  - `MRRequestNO` = `MATERIAL_REQUEST.REQUEST_NO`
  - `MRDetailID` = `MATERIAL_REQUEST_DETAIL.ID`

Sau khi insert detail, hệ thống cập nhật ngược `MATERIAL_REQUEST_DETAIL`:

- nếu `SugBuy < BUY`:
  - cập nhật lại `BUY = BUY - SugBuy`
- nếu `SugBuy >= BUY`:
  - cập nhật `PostedPR = 1`

Ý nghĩa nghiệp vụ:

- một phần nhu cầu mua còn lại thì giữ tiếp ở MR
- đủ hết thì đánh dấu dòng MR đã được đưa sang PR

### 9.4. Khác biệt giữa flow cũ và code hiện tại của page PurchaseRequisition

Flow cũ VB6:

- `Add AT` tạo mới một PR mới từ các dòng MR đủ điều kiện
- dữ liệu nguồn của popup là từ `MATERIAL_REQUEST` / `MATERIAL_REQUEST_DETAIL`
- có `Check`
- có `SugBuy`
- có ràng buộc cập nhật ngược MR sau khi tạo PR
- sau khi xong thì mở ngay `PR Detail` của phiếu vừa tạo

Code hiện tại ở page `PurchaseRequisition`:

- `Add AT` đang yêu cầu chọn đúng 1 dòng PR có sẵn trên danh sách
- dữ liệu popup hiện đi theo `PurchaseRequisitionDetailInput`
- backend chỉ gọi `AddDetails(prId, details)` để insert thêm vào `PC_PRDetail`
- không tạo mới `PC_PR`
- không đọc từ `MATERIAL_REQUEST`
- không cập nhật ngược `MATERIAL_REQUEST_DETAIL.BUY` hoặc `PostedPR`
- chưa lưu `MRRequestNO` và `MRDetailID` theo flow cũ của Add AT

Kết luận:

- `Add AT` hiện tại mới là phiên bản rút gọn kiểu “thêm nhanh detail vào PR đang có”
- chưa đúng với flow nghiệp vụ cũ của `Gen PR`

### 9.5. Gợi ý mapping field của popup Add AT theo flow cũ

Nếu làm lại đúng theo VB6, popup Add AT cần tối thiểu các cột sau:

- `Check`
- `RequestNo`
- `ItemCode`
- `ItemName`
- `BUY`
- `SugBuy`
- `Unit`
- `UnitPrice`
- `Specification`
- `MRDetailID`

Trong đó:

- `Check` là cột chọn dòng
- `SugBuy` là cột người dùng có thể sửa
- `BUY` là số lượng còn phải mua từ MR
- `MRDetailID` nên ẩn nhưng phải giữ để lưu vết truy xuất về MR

### 9.6. Logic list page theo ảnh nghiệp vụ

Ảnh nghiệp vụ cho thấy list page `Purchase Requisition` có thêm ý nghĩa phân vai theo bước duyệt:

- `PU` nhìn theo trạng thái mặc định `1`
- `CFO` và `BOD` nhìn theo trạng thái mặc định `2`
- `CFO` có thêm điều kiện mặc định là các mốc ký của CFO đang null
- `BOD` có thêm điều kiện mặc định là các mốc ký của BOD đang null

Điều này cho thấy về lâu dài page list không chỉ search đơn thuần, mà còn cần:

- lọc mặc định theo vai trò nghiệp vụ
- thay đổi điều kiện list theo bước duyệt hiện tại
- dùng dữ liệu người ký trong `PC_PR` để quyết định chứng từ nào đang chờ ai xử lý

### 9.7. Trạng thái PR cần hiểu lại theo ảnh và code cũ

Theo ảnh và mô tả nghiệp vụ, ý nghĩa thực tế của list có vẻ gần với luồng:

- `1 = New`
- `2 = Waiting For Approve`
- `3 = Pending`
- `4 = Done`

Điểm này phù hợp hơn với việc:

- `PU` tạo PR mới ở trạng thái đầu
- sau submit thì chứng từ chuyển sang trạng thái chờ duyệt
- các bước ký tiếp theo dùng điều kiện chữ ký null để xác định chứng từ đang ở ai

### 9.8. Khoảng trống dữ liệu đang thiếu trong MS_Employee

Theo yêu cầu nghiệp vụ mới, vai trò duyệt cần phân biệt bằng các cờ bool trong bảng `MS_Employee`:

- `IsPachaser` hoặc `IsPurchaser`
- `IsCFO`
- `IsBOD`

Kiểm tra schema DB hiện tại cho thấy:

- bảng `MS_Employee` chưa có các cột trên

Điều này có nghĩa là trước khi code đầy đủ flow Add AT / Submit / Approve theo vai trò mới, cần bổ sung data schema cho `MS_Employee`.

### 9.9. Kết luận cho bước phân tích hiện tại

Ở bước hiện tại có thể chốt như sau:

- `Add AT` theo nghiệp vụ gốc là chức năng `Gen PR` từ MR, không phải chỉ thêm detail vào PR có sẵn
- popup Add AT phải lấy nguồn từ `MATERIAL_REQUEST` và `MATERIAL_REQUEST_DETAIL`
- khi lưu phải tạo mới `PC_PR`, insert `PC_PRDetail`, rồi cập nhật ngược lại dữ liệu MR
- role `PU`, `CFO`, `BOD` cần được xác định bằng cờ bool ở `MS_Employee`, nhưng schema hiện tại đang thiếu
- phần này mới nên dừng ở mức tài liệu phân tích; chưa nên sửa code cho tới khi chốt lại đầy đủ rule và cấu trúc data cần bổ sung
