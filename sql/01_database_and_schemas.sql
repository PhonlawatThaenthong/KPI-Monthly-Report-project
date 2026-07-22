/* =============================================================
   01_database_and_schemas.sql
   Project : KPI Monthly Report
   Purpose : สร้าง Database และ Schema ทั้งหมด
   Run as  : sysadmin / dbcreator
   Idempotent : YES (รันซ้ำได้)
   ============================================================= */

USE master;
GO

IF DB_ID('KpiMonthlyReport') IS NULL
BEGIN
    CREATE DATABASE KpiMonthlyReport;
    PRINT '>> Created database KpiMonthlyReport';
END
ELSE
    PRINT '>> Database KpiMonthlyReport already exists';
GO

ALTER DATABASE KpiMonthlyReport SET RECOVERY SIMPLE;   -- dev เท่านั้น, prod ใช้ FULL
GO

USE KpiMonthlyReport;
GO

/* -------------------------------------------------------------
   Schema layers
   stg  = Staging  : ข้อมูลดิบจากต้นทาง ยังไม่ clean (ทุก column เป็น NVARCHAR)
   core = Core DW  : ข้อมูลที่ clean แล้ว มี Dim / Fact
   rpt  = Report   : View สำหรับ Dashboard และ Report (Web อ่านจากชั้นนี้เท่านั้น)
   meta = Metadata : KPI definition, ETL log, Audit log, config
   ------------------------------------------------------------- */

IF SCHEMA_ID('stg')  IS NULL EXEC('CREATE SCHEMA stg  AUTHORIZATION dbo;');
IF SCHEMA_ID('core') IS NULL EXEC('CREATE SCHEMA core AUTHORIZATION dbo;');
IF SCHEMA_ID('rpt')  IS NULL EXEC('CREATE SCHEMA rpt  AUTHORIZATION dbo;');
IF SCHEMA_ID('meta') IS NULL EXEC('CREATE SCHEMA meta AUTHORIZATION dbo;');
GO

PRINT '>> Schemas ready: stg, core, rpt, meta';
GO

/* -------------------------------------------------------------
   Database Roles  (แยกสิทธิ์ ETL กับ Web ออกจากกัน)
   ------------------------------------------------------------- */
IF DATABASE_PRINCIPAL_ID('db_kpi_etl') IS NULL
    CREATE ROLE db_kpi_etl AUTHORIZATION dbo;
IF DATABASE_PRINCIPAL_ID('db_kpi_web') IS NULL
    CREATE ROLE db_kpi_web AUTHORIZATION dbo;
GO

-- ETL: เขียนได้ทุกชั้น
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::stg  TO db_kpi_etl;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::core TO db_kpi_etl;
GRANT SELECT, INSERT, UPDATE          ON SCHEMA::meta TO db_kpi_etl;
GRANT EXECUTE ON SCHEMA::meta TO db_kpi_etl;
GRANT EXECUTE ON SCHEMA::core TO db_kpi_etl;

-- Web: อ่านได้เฉพาะ rpt + เขียน log ได้ (ห้ามแตะ core/stg โดยตรง)
GRANT SELECT ON SCHEMA::rpt TO db_kpi_web;
GRANT SELECT, INSERT ON SCHEMA::meta TO db_kpi_web;
GRANT EXECUTE ON SCHEMA::rpt  TO db_kpi_web;
GRANT EXECUTE ON SCHEMA::meta TO db_kpi_web;
GO

PRINT '>> Roles ready: db_kpi_etl, db_kpi_web';
GO
