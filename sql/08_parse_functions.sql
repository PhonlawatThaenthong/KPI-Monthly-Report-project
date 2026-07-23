/* =============================================================
   08_parse_functions.sql
   Purpose : ฟังก์ชันแปลงข้อมูลสกปรกเพิ่มเติม (วันที่, กะ)
   Idempotent : YES
   ============================================================= */

USE KpiMonthlyReport;
GO

/* -------------------------------------------------------------
   core.fn_ParseDate
   แปลงวันที่แบบข้อความเป็น DATE โดยลองหลายรูปแบบตามลำดับ

   รองรับ:  2026-01-15 | 2026/01/15 | 15/01/2026 | 01/15/2026 | 15-Jan-26

   *** ข้อจำกัดที่ต้องรู้ ***
   '07/06/2025' ตีความได้ทั้ง 6 ก.ค. และ 7 มิ.ย. -> กำกวมโดยธรรมชาติ
   ฟังก์ชันนี้เลือก dd/MM (style 103) เป็นค่าเริ่มต้นตามธรรมเนียมไทย
   ในงานจริงต้องไปยืนยันกับเจ้าของระบบต้นทางว่าใช้รูปแบบไหน
   ห้ามเดาแล้วปล่อยผ่านเงียบ ๆ เพราะตัวเลขจะเพี้ยนข้ามเดือน
   ------------------------------------------------------------- */
CREATE OR ALTER FUNCTION core.fn_ParseDate (@Input NVARCHAR(50))
RETURNS DATE
AS
BEGIN
    DECLARE @s NVARCHAR(50) = LTRIM(RTRIM(ISNULL(@Input, N'')));
    IF @s = N'' RETURN NULL;

    DECLARE @d DATE;

    -- 1) ISO: yyyy-MM-dd (ไม่กำกวม ลองก่อนเสมอ)
    SET @d = TRY_CONVERT(DATE, @s, 23);
    IF @d IS NOT NULL RETURN @d;

    -- 2) yyyy/MM/dd -> แปลงเป็น ISO ก่อน
    IF @s LIKE '[0-9][0-9][0-9][0-9]/[0-9][0-9]/[0-9][0-9]'
    BEGIN
        SET @d = TRY_CONVERT(DATE, REPLACE(@s, '/', '-'), 23);
        IF @d IS NOT NULL RETURN @d;
    END

    -- 3) dd-MMM-yy  เช่น 15-Jan-26
    IF @s LIKE '%[A-Za-z][A-Za-z][A-Za-z]%'
    BEGIN
        SET @d = TRY_CONVERT(DATE, REPLACE(@s, '-', ' '), 6);
        IF @d IS NOT NULL RETURN @d;
        SET @d = TRY_CONVERT(DATE, REPLACE(@s, '-', ' '), 106);
        IF @d IS NOT NULL RETURN @d;
    END

    -- 4) dd/MM/yyyy  <-- ค่าเริ่มต้นสำหรับกรณีกำกวม
    SET @d = TRY_CONVERT(DATE, @s, 103);
    IF @d IS NOT NULL RETURN @d;

    -- 5) MM/dd/yyyy  (เหลือเฉพาะกรณีที่ 103 แปลงไม่ได้ เช่น 01/15/2026)
    SET @d = TRY_CONVERT(DATE, @s, 101);
    IF @d IS NOT NULL RETURN @d;

    RETURN NULL;   -- แปลงไม่ได้ -> ให้ Transform ส่งเข้า DataRejectLog
END
GO

/* -------------------------------------------------------------
   core.fn_ParseShift
   '1' / '2' / 'Shift 1' / 'S2'  ->  1 / 2
   คืน NULL ถ้าแปลงไม่ได้ (Transform จะ default เป็น 1)
   ------------------------------------------------------------- */
CREATE OR ALTER FUNCTION core.fn_ParseShift (@Input NVARCHAR(20))
RETURNS TINYINT
AS
BEGIN
    DECLARE @s NVARCHAR(20) = LTRIM(RTRIM(ISNULL(@Input, N'')));
    IF @s = N'' RETURN NULL;

    -- เอาตัวเลขตัวสุดท้าย รองรับทั้ง '1', 'Shift 1', 'S1'
    DECLARE @last NCHAR(1) = RIGHT(@s, 1);
    DECLARE @n TINYINT = TRY_CONVERT(TINYINT, @last);

    IF @n IN (1, 2, 3) RETURN @n;
    RETURN NULL;
END
GO

/* -------------------------------------------------------------
   ทดสอบฟังก์ชัน  (ควรได้ค่าตามคอมเมนต์ท้ายบรรทัด)
   ------------------------------------------------------------- */
SELECT
    core.fn_ParseDate(N'2026-01-15')  AS Iso,        -- 2026-01-15
    core.fn_ParseDate(N'2026/01/15')  AS IsoSlash,   -- 2026-01-15
    core.fn_ParseDate(N'15/01/2026')  AS DdMm,       -- 2026-01-15
    core.fn_ParseDate(N'01/15/2026')  AS MmDd,       -- 2026-01-15
    core.fn_ParseDate(N'15-Jan-26')   AS DdMon,      -- 2026-01-15
    core.fn_ParseDate(N'ขยะ')          AS Junk;       -- NULL
GO

SELECT
    core.fn_ParseShift(N'1')       AS S1,   -- 1
    core.fn_ParseShift(N'Shift 2') AS S2,   -- 2
    core.fn_ParseShift(N'S2')      AS S3,   -- 2
    core.fn_ParseShift(N'')        AS S4;   -- NULL
GO
