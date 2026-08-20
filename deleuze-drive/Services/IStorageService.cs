using System;
using System.IO;
using System.Threading.Tasks;

namespace DeleuzeDrive.Services
{
    public interface IStorageService
    {
        // 署名付きアップロード URL と 生成された S3 Key を返す
        (string UploadUrl, string Key) GeneratePresignedUploadUrl(string fileName, string contentType, double expireMinutes = 15);

        // 署名付きダウンロード URL を返す
        string GeneratePresignedDownloadUrl(string key, double expireMinutes = 15);

        Task DeleteAsync(string key);
    }
}