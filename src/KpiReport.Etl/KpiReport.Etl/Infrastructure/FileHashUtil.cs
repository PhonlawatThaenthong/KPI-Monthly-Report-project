using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KpiReport.Etl.Infrastructure
{
    /// <summary>
    /// คำนวณ hash ของไฟล์ ใช้เทียบกับ stg.FileLoadHistory
    /// เพื่อกันโหลดไฟล์เดิมซ้ำเวลารัน ETL ทับหลายรอบ
    /// </summary>
    public static class FileHashUtil
    {
        public static string ComputeSha256(string filePath)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hashBytes = sha256.ComputeHash(stream);
                var sb = new StringBuilder(hashBytes.Length * 2);
                foreach (byte b in hashBytes)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
