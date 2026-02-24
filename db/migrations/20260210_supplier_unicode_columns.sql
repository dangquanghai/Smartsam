/*
  Normalize Supplier text columns to Unicode (NVARCHAR)
  Date: 2026-02-10
*/

-- Current suppliers
IF COL_LENGTH('dbo.PC_Suppliers','Contact') IS NOT NULL
    ALTER TABLE dbo.PC_Suppliers ALTER COLUMN Contact NVARCHAR(40) NULL;
IF COL_LENGTH('dbo.PC_Suppliers','Position') IS NOT NULL
    ALTER TABLE dbo.PC_Suppliers ALTER COLUMN Position NVARCHAR(40) NULL;
IF COL_LENGTH('dbo.PC_Suppliers','Comment') IS NOT NULL
    ALTER TABLE dbo.PC_Suppliers ALTER COLUMN Comment NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.PC_Suppliers','Certificate') IS NOT NULL
    ALTER TABLE dbo.PC_Suppliers ALTER COLUMN Certificate NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.PC_Suppliers','Appcept') IS NOT NULL
    ALTER TABLE dbo.PC_Suppliers ALTER COLUMN Appcept NVARCHAR(254) NULL;
IF COL_LENGTH('dbo.PC_Suppliers','Business') IS NOT NULL
    ALTER TABLE dbo.PC_Suppliers ALTER COLUMN Business NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.PC_Suppliers','Service') IS NOT NULL
    ALTER TABLE dbo.PC_Suppliers ALTER COLUMN Service NVARCHAR(1000) NULL;

-- Annual snapshot suppliers
IF COL_LENGTH('dbo.PC_SupplierAnualy','Contact') IS NOT NULL
    ALTER TABLE dbo.PC_SupplierAnualy ALTER COLUMN Contact NVARCHAR(40) NULL;
IF COL_LENGTH('dbo.PC_SupplierAnualy','Position') IS NOT NULL
    ALTER TABLE dbo.PC_SupplierAnualy ALTER COLUMN Position NVARCHAR(40) NULL;
IF COL_LENGTH('dbo.PC_SupplierAnualy','Comment') IS NOT NULL
    ALTER TABLE dbo.PC_SupplierAnualy ALTER COLUMN Comment NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.PC_SupplierAnualy','Certificate') IS NOT NULL
    ALTER TABLE dbo.PC_SupplierAnualy ALTER COLUMN Certificate NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.PC_SupplierAnualy','Appcept') IS NOT NULL
    ALTER TABLE dbo.PC_SupplierAnualy ALTER COLUMN Appcept NVARCHAR(254) NULL;
IF COL_LENGTH('dbo.PC_SupplierAnualy','Business') IS NOT NULL
    ALTER TABLE dbo.PC_SupplierAnualy ALTER COLUMN Business NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.PC_SupplierAnualy','Service') IS NOT NULL
    ALTER TABLE dbo.PC_SupplierAnualy ALTER COLUMN Service NVARCHAR(1000) NULL;
