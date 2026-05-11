SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    DECLARE @Today smalldatetime = CAST(GETDATE() AS smalldatetime);

    DECLARE @PreparedId int = (
        SELECT TOP 1 EmployeeID
        FROM dbo.MS_Employee
        WHERE ISNULL(IsActive, 0) = 1
        ORDER BY CASE WHEN EmployeeCode = 'FD031' THEN 0 ELSE 1 END, EmployeeID
    );

    DECLARE @CheckedId int = (
        SELECT TOP 1 EmployeeID
        FROM dbo.MS_Employee
        WHERE ISNULL(IsActive, 0) = 1 AND ISNULL(IsCFO, 0) = 1
        ORDER BY EmployeeID
    );

    DECLARE @ApprovedId int = (
        SELECT TOP 1 EmployeeID
        FROM dbo.MS_Employee
        WHERE ISNULL(IsActive, 0) = 1 AND ISNULL(IsBOD, 0) = 1
        ORDER BY EmployeeID
    );

    IF @PreparedId IS NULL SET @PreparedId = @CheckedId;
    IF @PreparedId IS NULL SET @PreparedId = @ApprovedId;
    IF @CheckedId IS NULL SET @CheckedId = @PreparedId;
    IF @ApprovedId IS NULL SET @ApprovedId = @CheckedId;

    UPDATE dbo.MS_Employee
    SET UrlNomalSign = 'sample-signature.png'
    WHERE EmployeeID IN (@PreparedId, @CheckedId, @ApprovedId)
      AND ISNULL(LTRIM(RTRIM(UrlNomalSign)), '') = '';

    DECLARE @SampleRequestNo varchar(20) = 'PS26050901';
    DECLARE @PrId int;

    SELECT @PrId = PRID
    FROM dbo.PC_PR
    WHERE RequestNo = @SampleRequestNo;

    IF @PrId IS NULL
    BEGIN
        INSERT INTO dbo.PC_PR
        (
            RequestNo, RequestDate, [Description], Currency, [Status], IsAuto, MRNo, PostPO,
            PurId, PurApproDate, CAId, CAApproDate, GDId, GDApproDate, edited
        )
        VALUES
        (
            @SampleRequestNo, @Today, 'Sample PR for testing detail/report alignment + signature block', 1, 4, 0, NULL, 0,
            @PreparedId, CONVERT(varchar(10), GETDATE(), 103), @CheckedId, CONVERT(varchar(10), GETDATE(), 103), @ApprovedId, CONVERT(varchar(10), GETDATE(), 103), 1
        );

        SET @PrId = CAST(SCOPE_IDENTITY() AS int);
    END

    DECLARE @ItemId int = (
        SELECT TOP 1 ItemID FROM dbo.INV_ItemList WHERE ISNULL(IsActive, 1) = 1 ORDER BY ItemID
    );

    IF @ItemId IS NULL
        THROW 50001, 'No active item found in INV_ItemList to seed PR detail sample.', 1;

    IF NOT EXISTS (SELECT 1 FROM dbo.PC_PRDetail WHERE PRID = @PrId)
    BEGIN
        INSERT INTO dbo.PC_PRDetail
        (
            PRID, ItemID, Quantity, UnitPrice, Remark, RecQty, OrdAmount, RecAmount, POed,
            MRRequestNO, SugQty, SupplierID, PoQuantity, PoQuantitySug, MRDetailID
        )
        VALUES
        (
            @PrId, @ItemId, 25, 120000, N'Sample numeric/text alignment row', 0, 3000000, 0, 0,
            NULL, 25, NULL, 0, 25, NULL
        );
    END

    COMMIT;

    PRINT 'Seed completed.';
    PRINT 'Sample RequestNo: PS26050901';
    PRINT 'Signature sample file expected at: Uploads/Admin/Employee/sample-signature.png';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;

