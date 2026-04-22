SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    IF EXISTS
    (
        SELECT 1
        WHERE NOT EXISTS (SELECT 1 FROM dbo.INV_KPGroup WHERE KPGroupID = 1)
           OR NOT EXISTS (SELECT 1 FROM dbo.INV_KPGroup WHERE KPGroupID = 2)
           OR NOT EXISTS (SELECT 1 FROM dbo.INV_KPGroup WHERE KPGroupID = 3)
    )
        SET IDENTITY_INSERT dbo.INV_KPGroup ON;

    IF NOT EXISTS (SELECT 1 FROM dbo.INV_KPGroup WHERE KPGroupID = 1)
        INSERT INTO dbo.INV_KPGroup (KPGroupID, KPGroupName, IsAdminGroup, DepID) VALUES (1, 'HK-Housekeeping', 0, 1);
    IF NOT EXISTS (SELECT 1 FROM dbo.INV_KPGroup WHERE KPGroupID = 2)
        INSERT INTO dbo.INV_KPGroup (KPGroupID, KPGroupName, IsAdminGroup, DepID) VALUES (2, 'EN-Engineering', 0, 2);
    IF NOT EXISTS (SELECT 1 FROM dbo.INV_KPGroup WHERE KPGroupID = 3)
        INSERT INTO dbo.INV_KPGroup (KPGroupID, KPGroupName, IsAdminGroup, DepID) VALUES (3, 'OF-Office Supply', 0, 3);

    IF EXISTS
    (
        SELECT 1
        WHERE EXISTS (SELECT 1 FROM dbo.INV_KPGroup WHERE KPGroupID = 1)
          AND EXISTS (SELECT 1 FROM dbo.INV_KPGroup WHERE KPGroupID = 2)
          AND EXISTS (SELECT 1 FROM dbo.INV_KPGroup WHERE KPGroupID = 3)
    )
        SET IDENTITY_INSERT dbo.INV_KPGroup OFF;

    DECLARE @SampleMr TABLE
    (
        RequestNo numeric(18, 0) NOT NULL,
        StoreGroup numeric(18, 0) NOT NULL,
        DateCreate datetime NULL,
        AccordingTo varchar(300) NULL
    );

    INSERT INTO @SampleMr (RequestNo, StoreGroup, DateCreate, AccordingTo)
    VALUES
        (990001, 1, '2026-04-01', 'Sample MR for housekeeping supply batch and report UI verification'),
        (990002, 2, '2026-04-03', 'Sample MR for engineering replacement items and report UI verification'),
        (990003, 3, '2026-04-05', 'Sample MR for office consumables and report UI verification'),
        (990004, 1, '2026-04-07', 'Sample MR for long-list rendering and total aggregation check'),
        (990005, 2, '2026-04-09', 'Sample MR for item name wrapping and department text display'),
        (990006, 3, '2026-04-11', 'Sample MR for export and page-size testing'),
        (990007, 1, '2026-04-13', 'Sample MR for repeated supplier detail combinations'),
        (990008, 2, '2026-04-15', 'Sample MR for responsive table test with long content'),
        (990009, 3, '2026-04-17', 'Sample MR for final UI verification scenario');

    INSERT INTO dbo.MATERIAL_REQUEST
    (
        REQUEST_NO,
        STORE_GROUP,
        DATE_CREATE,
        ACCORDINGTO,
        APPROVAL,
        POST_PR,
        IS_AUTO,
        FROM_DATE,
        TO_DATE,
        APPROVAL_END,
        MATERIALSTATUSID,
        NO_ISSUE
    )
    SELECT
        mr.RequestNo,
        mr.StoreGroup,
        mr.DateCreate,
        mr.AccordingTo,
        1,
        0,
        0,
        mr.DateCreate,
        mr.DateCreate,
        1,
        1,
        0
    FROM @SampleMr mr
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.MATERIAL_REQUEST x
        WHERE x.REQUEST_NO = mr.RequestNo
    );

    ;WITH POSeed AS
    (
        SELECT
            p.POID,
            p.PONo,
            ROW_NUMBER() OVER (ORDER BY p.POID) AS Seq
        FROM dbo.PC_PO p
        WHERE p.PONo LIKE 'AS26%'
    ),
    ItemSeed AS
    (
        SELECT
            i.ItemID,
            i.ItemCode,
            i.ItemName,
            i.Unit,
            ROW_NUMBER() OVER (ORDER BY i.ItemID) AS Seq
        FROM dbo.INV_ItemList i
        WHERE i.ItemCode LIKE 'PRSMP-ITEM-%'
    ),
    Num AS
    (
        SELECT 1 AS n
        UNION ALL
        SELECT n + 1
        FROM Num
        WHERE n < 84
    ),
    SourceRows AS
    (
        SELECT
            po.POID,
            po.PONo,
            item.ItemID,
            item.ItemCode,
            item.ItemName,
            item.Unit,
            CAST(4 + ((n.n - 1) % 9) AS decimal(18, 3)) AS Quantity,
            CAST(32000 + (n.n * 2750) AS decimal(18, 3)) AS UnitPrice,
            CAST(990001 + ((n.n - 1) % 9) AS numeric(18, 0)) AS MRNo,
            CONCAT(
                'Sample detail line ', n.n,
                ' for Supplier PO Report testing with long note text and realistic purchasing context.'
            ) AS NoteText
        FROM Num n
        INNER JOIN POSeed po
            ON po.Seq = ((n.n - 1) % (SELECT COUNT(*) FROM POSeed)) + 1
        INNER JOIN ItemSeed item
            ON item.Seq = ((n.n - 1) % (SELECT COUNT(*) FROM ItemSeed)) + 1
    )
    INSERT INTO dbo.PC_PODetail
    (
        POID,
        ItemID,
        Quantity,
        UnitPrice,
        Note,
        RecQty,
        POAmount,
        RecAmount,
        IsReceived,
        Renovation,
        General,
        MRNo
    )
    SELECT
        src.POID,
        src.ItemID,
        src.Quantity,
        src.UnitPrice,
        LEFT(src.NoteText, 253),
        src.Quantity * 0.55,
        src.Quantity * src.UnitPrice,
        src.Quantity * src.UnitPrice * 0.55,
        CASE WHEN src.POID % 2 = 0 THEN 1 ELSE 0 END,
        0,
        1,
        src.MRNo
    FROM SourceRows src
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.PC_PODetail d
        WHERE d.POID = src.POID
          AND d.ItemID = src.ItemID
          AND ISNULL(d.MRNo, 0) = src.MRNo
    )
    OPTION (MAXRECURSION 100);

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK;

    IF (SELECT COUNT(*) FROM sys.identity_columns WHERE object_id = OBJECT_ID('dbo.INV_KPGroup')) > 0
    BEGIN
        BEGIN TRY
            SET IDENTITY_INSERT dbo.INV_KPGroup OFF;
        END TRY
        BEGIN CATCH
        END CATCH
    END;

    THROW;
END CATCH;
