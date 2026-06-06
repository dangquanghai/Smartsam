SET NOCOUNT ON;

UPDATE dbo.AM_Apmt
SET ExistFrom = ISNULL(ExistFrom, CONVERT(date, '19000101')),
    ExistTo = ISNULL(ExistTo, CONVERT(date, '20991231'))
WHERE ExistFrom IS NULL
   OR ExistTo IS NULL;

SELECT ApmtID,
       ApartmentNo,
       ExistFrom,
       ExistTo
FROM dbo.AM_Apmt
WHERE ExistFrom <= GETDATE()
  AND ExistTo >= GETDATE()
ORDER BY FloorNo, BlockNo, ApartmentNo;
