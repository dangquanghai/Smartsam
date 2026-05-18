# Linen Report Implementation Plan

## 1) Feature
- Feature name: Laundry & Linen Report
- Legacy function: LinenReport
- Legacy form: frmLinenReport
- Module: Inventory Management
- Proposed Razor route: /Inventory/LinenReport/Index
- Target files:
  - Pages/Inventory/LinenReport/Index.cshtml
  - Pages/Inventory/LinenReport/Index.cshtml.cs
  - wwwroot/js/Inventory/LinenReport/index.js
  - db/migrations/20260514_linen_report_permission_menu.sql

## 2) Required References
- STContract standard: Documents/SmartSAM_STContract_Strict_Standard.md
- Golden reference files:
  - Pages/Sales/STContract/Index.cshtml
  - Pages/Sales/STContract/Index.cshtml.cs
  - wwwroot/js/Sales/STContract/index.js
- VB source:
  - Documents/Linen & Laundry/14. Laundry & Linen Report/ExtractedAssets/Code_extracted_text.txt
- Specification supplement:
  - Documents/Linen & Laundry/Linnen.docx
  - Documents/Linen & Laundry/Linnen_Extracted_Text.md
  - Documents/Linen & Laundry/Image_Order_Map.md
- Report screenshots:
  - Documents/Linen & Laundry/14. Laundry & Linen Report/ExtractedAssets/media/linen_report_form_overview.png
  - Documents/Linen & Laundry/14. Laundry & Linen Report/ExtractedAssets/media/linen_report_pantry_preview.png
  - Documents/Linen & Laundry/14. Laundry & Linen Report/ExtractedAssets/media/linen_report_delivery_preview.png
  - Documents/Linen & Laundry/14. Laundry & Linen Report/ExtractedAssets/media/linen_report_receive_preview.png
  - Documents/Linen & Laundry/14. Laundry & Linen Report/ExtractedAssets/media/linen_report_laundry_record_preview.png
  - Documents/Linen & Laundry/14. Laundry & Linen Report/ExtractedAssets/media/linen_report_laundry_room_balance_preview.png
  - Documents/Linen & Laundry/14. Laundry & Linen Report/ExtractedAssets/media/linen_report_apartment_balance_preview.png

## 3) Scope
- In scope:
  - Single web report form for VB report modes 1-7:
    - Pantry-Linen
    - Delivery
    - Receive
    - Laundry Record
    - Not Receive
    - Laundry.U. Balance
    - Apmt Balance
  - Load correct description/apartment options by selected mode.
  - Preview data from existing legacy tables/views/procs.
  - Permission/menu route wiring for FunctionID = 118.
- Out of scope:
  - Crystal Report toolbar parity.
  - Real chart output.
  - Special Laundry Report (doc 15, separate function).

## 4) VB Business Logic Alignment
- Radio modes map to VB `Flag` 1..7.
- Mode option behavior:
  - Pantry / Delivery / Receive use `Des` combo and current record lists.
  - Laundry Record uses only date range.
  - Not Receive ignores `Des`.
  - Laundry.U. Balance uses date range only.
  - Apmt Balance changes label to `APMT` and loads apartment combo.
- Preview behavior:
  - Pantry executes `LN_MakeLinenRPT` then reads `ViewPentryLinen`.
  - Delivery reads `ViewLinenDelivery` for selected DeliveryID.
  - Receive reads `ViewLinenReceive` for selected ReceiveID.
  - Laundry Record requires From/To in same month, executes `LN_LaundryRecordRPT`, then reads `LN_LaudryRecord_TMP` / `View_LNLinenRecord`.
  - Not Receive executes `LN_MarkFullReceiveOnAllDelevery`, then reads `ViewNotReceive`.
  - Laundry.U. Balance uses `LN_LaundryRoomBalance`.
  - Apmt Balance uses `LN_ApmtLaundryBalance`.

## 5) STContract Implementation Shape
- New module page will keep STContract page shell:
  - left search/filter panel
  - right preview/result panel
  - action footer
- Business deviation:
  - No row grid/detail navigation because VB report function is single-form preview only.
  - Deviation must be reported against checklist item 4 with reason.

## 6) Data and dependency check
- Tables/views/procs involved:
  - LN_DeAndReMT
  - LN_DeliveryMT
  - LN_ReceiveMT
  - AM_Apmt
  - ViewPentryLinen
  - ViewLinenDelivery
  - ViewLinenReceive
  - ViewNotReceive
  - LN_MakeLinenRPT
  - LN_LaundryRecordRPT
  - LN_LaundryRoomBalance
  - LN_ApmtLaundryBalance
  - LN_MarkFullReceiveOnAllDelevery
- Local data gap already observed:
  - receive / laundry-record / balance previews may be empty until more demo data exists.

## 7) Implementation steps
1. Add plan + WORK_LOG entry.
2. Create LinenReport page skeleton following STContract shell.
3. Implement type-dependent option loaders and preview handler.
4. Implement report preview renderers in JS for each VB mode.
5. Add permission/menu migration for FunctionID 118.
6. Verify build and sample preview handlers.

## 8) Verification
- `node --check wwwroot/js/Inventory/LinenReport/index.js`
- `docker exec smartsam-app-local dotnet build /src/SmartSam.csproj --no-restore`
- HTTP smoke on:
  - /Inventory/LinenReport/Index
  - /Inventory/LinenReport/Index?handler=Preview...

