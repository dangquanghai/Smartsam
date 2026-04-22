SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRAN;

    IF NOT EXISTS (SELECT 1 FROM dbo.PC_POStatus WHERE POStatusID = 1)
        INSERT INTO dbo.PC_POStatus (POStatusID, POStatusName) VALUES (1, 'Draft');
    IF NOT EXISTS (SELECT 1 FROM dbo.PC_POStatus WHERE POStatusID = 2)
        INSERT INTO dbo.PC_POStatus (POStatusID, POStatusName) VALUES (2, 'Pending');
    IF NOT EXISTS (SELECT 1 FROM dbo.PC_POStatus WHERE POStatusID = 3)
        INSERT INTO dbo.PC_POStatus (POStatusID, POStatusName) VALUES (3, 'Approved');
    IF NOT EXISTS (SELECT 1 FROM dbo.PC_POStatus WHERE POStatusID = 4)
        INSERT INTO dbo.PC_POStatus (POStatusID, POStatusName) VALUES (4, 'Closed');

    IF NOT EXISTS (SELECT 1 FROM dbo.PC_AssessLevel WHERE AssessLevelID = 1)
        INSERT INTO dbo.PC_AssessLevel (AssessLevelID, AssessLevelName, Point) VALUES (1, 'Good', 8.50);
    IF NOT EXISTS (SELECT 1 FROM dbo.PC_AssessLevel WHERE AssessLevelID = 2)
        INSERT INTO dbo.PC_AssessLevel (AssessLevelID, AssessLevelName, Point) VALUES (2, 'Very Good', 9.20);
    IF NOT EXISTS (SELECT 1 FROM dbo.PC_AssessLevel WHERE AssessLevelID = 3)
        INSERT INTO dbo.PC_AssessLevel (AssessLevelID, AssessLevelName, Point) VALUES (3, 'Not Good', 5.10);
    IF NOT EXISTS (SELECT 1 FROM dbo.PC_AssessLevel WHERE AssessLevelID = 4)
        INSERT INTO dbo.PC_AssessLevel (AssessLevelID, AssessLevelName, Point) VALUES (4, 'Conditional', 6.80);

    DECLARE @SampleSuppliers TABLE
    (
        SupplierCode varchar(10) NOT NULL,
        SupplierName nvarchar(254) NOT NULL,
        Address nvarchar(254) NULL,
        Phone varchar(20) NULL,
        Contact nvarchar(40) NULL,
        Business nvarchar(500) NULL,
        Service nvarchar(500) NULL,
        Comment nvarchar(500) NULL
    );

    INSERT INTO @SampleSuppliers (SupplierCode, SupplierName, Address, Phone, Contact, Business, Service, Comment)
    VALUES
        ('ASL0001', N'Alpha Building Maintenance and Technical Supply Solutions for Smart Property Operations',
            N'127 Nguyen Huu Canh Street, Binh Thanh District, Ho Chi Minh City',
            '02839112233', N'Hoang Minh',
            N'Supplies MEP materials, replacement parts, routine maintenance support and urgent technical response services.',
            N'Handles split delivery, site coordination and document support for large purchasing batches.',
            N'Sample supplier with a long name for testing ellipsis, tooltip and responsive table layout.'),
        ('ASL0002', N'Asia Clean Energy Equipment and Industrial Automation Control Joint Stock Company',
            N'85 Vo Nguyen Giap Street, Thu Duc City, Ho Chi Minh City',
            '02835557799', N'Thanh Le',
            N'Provides electrical devices, controllers, sensors and related operational support items.',
            N'Offers staged handover, quality dossier support and technical clarification during approval.',
            N'Sample supplier focused on long labels for list, report and export verification.'),
        ('ASL0003', N'International Hospitality Operations Laundry Amenities and Room Supply Services Company',
            N'12 Tran Phu Street, Hai Chau District, Da Nang City',
            '02363886655', N'Gia Bao Pham',
            N'Covers hotel consumables, laundry support, room amenities, linen coordination and daily operations.',
            N'Uses weekly delivery slots and same-day complaint feedback to simulate realistic remarks.',
            N'Sample supplier used to test long remark and comment display across breakpoints.');

    INSERT INTO dbo.PC_Suppliers
    (
        SupplierCode,
        SupplierName,
        Address,
        Phone,
        Contact,
        Business,
        Service,
        Comment,
        ApprovedDate,
        IsApproved,
        IsNew,
        Status,
        IsDeleted,
        Email
    )
    SELECT
        s.SupplierCode,
        s.SupplierName,
        s.Address,
        s.Phone,
        s.Contact,
        s.Business,
        s.Service,
        s.Comment,
        '2026-04-01',
        1,
        1,
        4,
        0,
        CONCAT(LOWER(s.SupplierCode), '@sample.local')
    FROM @SampleSuppliers s
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.PC_Suppliers x
        WHERE x.SupplierCode = s.SupplierCode
    );

    UPDATE target
    SET
        target.SupplierName = src.SupplierName,
        target.Address = src.Address,
        target.Phone = src.Phone,
        target.Contact = src.Contact,
        target.Business = src.Business,
        target.Service = src.Service,
        target.Comment = src.Comment,
        target.ApprovedDate = '2026-04-01',
        target.IsApproved = 1,
        target.IsNew = 1,
        target.Status = 4,
        target.IsDeleted = 0,
        target.Email = CONCAT(LOWER(src.SupplierCode), '@sample.local')
    FROM dbo.PC_Suppliers target
    INNER JOIN @SampleSuppliers src
        ON src.SupplierCode = target.SupplierCode;

    DECLARE @SamplePrs TABLE
    (
        RequestNo varchar(10) NOT NULL,
        RequestDate smalldatetime NOT NULL,
        Description varchar(500) NULL,
        Currency tinyint NOT NULL,
        Status tinyint NOT NULL,
        MRNo varchar(100) NULL
    );

    INSERT INTO @SamplePrs (RequestNo, RequestDate, Description, Currency, Status, MRNo)
    VALUES
        ('ASPR26001', '2026-04-01', 'Sample PR 01 for supplier analysis UI validation and long-list rendering.', 1, 1, 'MR-AS-001'),
        ('ASPR26002', '2026-04-02', 'Sample PR 02 for supplier analysis UI validation and long-list rendering.', 1, 2, 'MR-AS-002'),
        ('ASPR26003', '2026-04-03', 'Sample PR 03 for supplier analysis UI validation and long-list rendering.', 1, 3, 'MR-AS-003'),
        ('ASPR26004', '2026-04-04', 'Sample PR 04 for supplier analysis UI validation and long-list rendering.', 1, 4, 'MR-AS-004'),
        ('ASPR26005', '2026-04-05', 'Sample PR 05 for supplier analysis UI validation and long-list rendering.', 1, 1, 'MR-AS-005'),
        ('ASPR26006', '2026-04-06', 'Sample PR 06 for supplier analysis UI validation and long-list rendering.', 1, 2, 'MR-AS-006'),
        ('ASPR26007', '2026-04-07', 'Sample PR 07 for supplier analysis UI validation and long-list rendering.', 1, 3, 'MR-AS-007'),
        ('ASPR26008', '2026-04-08', 'Sample PR 08 for supplier analysis UI validation and long-list rendering.', 1, 4, 'MR-AS-008'),
        ('ASPR26009', '2026-04-09', 'Sample PR 09 for supplier analysis UI validation and long-list rendering.', 1, 1, 'MR-AS-009'),
        ('ASPR26010', '2026-04-10', 'Sample PR 10 for supplier analysis UI validation and long-list rendering.', 1, 2, 'MR-AS-010'),
        ('ASPR26011', '2026-04-11', 'Sample PR 11 for supplier analysis UI validation and long-list rendering.', 1, 3, 'MR-AS-011'),
        ('ASPR26012', '2026-04-12', 'Sample PR 12 for supplier analysis UI validation and long-list rendering.', 1, 4, 'MR-AS-012');

    INSERT INTO dbo.PC_PR
    (
        RequestNo,
        RequestDate,
        Description,
        Currency,
        Status,
        IsAuto,
        MRNo,
        PostPO
    )
    SELECT
        p.RequestNo,
        p.RequestDate,
        p.Description,
        p.Currency,
        p.Status,
        0,
        p.MRNo,
        0
    FROM @SamplePrs p
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.PC_PR x
        WHERE x.RequestNo = p.RequestNo
    );

    DECLARE @SupplierLookup TABLE
    (
        Seq int NOT NULL,
        SupplierID int NOT NULL
    );

    INSERT INTO @SupplierLookup (Seq, SupplierID)
    SELECT
        ROW_NUMBER() OVER (ORDER BY SupplierCode),
        SupplierID
    FROM dbo.PC_Suppliers
    WHERE SupplierCode IN ('ASL0001', 'ASL0002', 'ASL0003');

    DECLARE @PrLookup TABLE
    (
        Seq int NOT NULL,
        PRID int NOT NULL
    );

    INSERT INTO @PrLookup (Seq, PRID)
    SELECT
        ROW_NUMBER() OVER (ORDER BY RequestNo),
        PRID
    FROM dbo.PC_PR
    WHERE RequestNo LIKE 'ASPR260%';

    ;WITH N AS
    (
        SELECT 1 AS n
        UNION ALL
        SELECT n + 1
        FROM N
        WHERE n < 42
    )
    INSERT INTO dbo.PC_PO
    (
        PRID,
        PONo,
        PODate,
        Remark,
        SupplierID,
        POTerms,
        StatusID,
        AssessLevel,
        Comment,
        Currency,
        ExRate,
        BeforeVAT,
        PerVAT,
        VAT,
        AfterVAT,
        IsOld,
        IsHifi,
        KeepStatus
    )
    SELECT
        pr.PRID,
        CONCAT('AS26', RIGHT(CONCAT('000', n.n), 3)),
        DATEADD(DAY, n.n - 1, CAST('2026-04-01' AS date)),
        LEFT(
            CONCAT(
                'Lot ', n.n,
                ': split delivery, verify labels, delivery papers and displayed specifications for UI review.'
            ),
            100
        ),
        sp.SupplierID,
        CONCAT(
            'Sample terms for UI testing. Delivery schedule is intentionally verbose to emulate real purchasing notes for row ',
            n.n,
            '.'
        ),
        ((n.n - 1) % 4) + 1,
        CASE
            WHEN n.n % 9 = 0 THEN 3
            WHEN n.n % 5 = 0 THEN 4
            WHEN n.n % 2 = 0 THEN 2
            ELSE 1
        END,
        LEFT(
            CONCAT(
                'Evaluation batch ', n.n,
                ': packaging, lead time, document consistency and after-sales response were recorded for responsive-table testing.'
            ),
            100
        ),
        1,
        1.00,
        CAST(1200000 + (n.n * 35000) AS decimal(18, 2)),
        8,
        CAST((1200000 + (n.n * 35000)) * 0.08 AS decimal(18, 2)),
        CAST((1200000 + (n.n * 35000)) * 1.08 AS decimal(18, 2)),
        0,
        CASE WHEN n.n % 3 = 0 THEN 1 ELSE 0 END,
        ((n.n - 1) % 4) + 1
    FROM N
    INNER JOIN @SupplierLookup sp
        ON sp.Seq = ((n.n - 1) % 3) + 1
    INNER JOIN @PrLookup pr
        ON pr.Seq = ((n.n - 1) % 12) + 1
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.PC_PO po
        WHERE po.PONo = CONCAT('AS26', RIGHT(CONCAT('000', n.n), 3))
    )
    OPTION (MAXRECURSION 100);

    ;WITH N AS
    (
        SELECT 1 AS n
        UNION ALL
        SELECT n + 1
        FROM N
        WHERE n < 42
    )
    UPDATE po
    SET
        po.Remark = LEFT(
            CONCAT(
                'Lot ', n.n,
                ': split delivery, verify labels, delivery papers and displayed specifications for UI review.'
            ),
            100
        ),
        po.Comment = LEFT(
            CONCAT(
                'Evaluation batch ', n.n,
                ': packaging, lead time, document consistency and after-sales response were recorded for UI testing.'
            ),
            100
        )
    FROM dbo.PC_PO po
    INNER JOIN N
        ON po.PONo = CONCAT('AS26', RIGHT(CONCAT('000', n.n), 3))
    OPTION (MAXRECURSION 100);

    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK;

    THROW;
END CATCH;
