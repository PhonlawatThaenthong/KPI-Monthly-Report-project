using System.IO;
using System.Text;

namespace KpiReport.Etl.Infrastructure
{
    /// <summary>
    /// ระบบต้นทางแต่ละไฟล์อาจ export ด้วย encoding ไม่เหมือนกัน
    /// (บางไฟล์เป็น UTF-8 with BOM ปกติ บางไฟล์เป็น Thai Windows-874)
    /// ห้าม assume UTF-8 เสมอ ไม่งั้นอ่านภาษาไทยจะเพี้ยนแบบเงียบ ๆ
    /// (ได้ตัวอักษรผิด ไม่ throw exception ให้เห็น)
    /// </summary>
    public static class CsvEncodingDetector
    {
        public static Encoding Detect(string filePath)
        {
            byte[] head = new byte[3];
            using (var fs = File.OpenRead(filePath))
            {
                int read = fs.Read(head, 0, (int)System.Math.Min(3, fs.Length));
                if (read < 3)
                    return new UTF8Encoding(false);
            }

            // UTF-8 BOM: EF BB BF
            if (head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
                return new UTF8Encoding(true);

            byte[] allBytes = File.ReadAllBytes(filePath);

            try
            {
                // ลอง decode แบบเข้มงวด ถ้าไม่ใช่ UTF-8 จริงจะ throw
                var strictUtf8 = Encoding.GetEncoding(
                    "utf-8",
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback);
                strictUtf8.GetString(allBytes);
                return new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                // ไม่ใช่ UTF-8 ที่ถูกต้อง -> เดาว่าเป็น Thai Windows code page (874)
                // ถ้าเจอ encoding อื่นเพิ่มเติมในอนาคต ให้มาต่อ else-if ตรงนี้
                return Encoding.GetEncoding(874);
            }
        }
    }
}
