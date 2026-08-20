using System;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace DeleuzeDrive.Services
{
    public class S3StorageService : IStorageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;

        public S3StorageService(IAmazonS3 s3Client, IConfiguration configuration)
        {
            _s3Client = s3Client;
            _bucketName = configuration["AWS:BucketName"] ?? "my-default-bucket";
        }

        /// <summary>
        /// アップロード用の署名付き URL と S3 Key を生成します。
        /// クライアントはこの URL に対して HTTP PUT リクエストでファイルを送信します。
        /// </summary>
        public (string UploadUrl, string Key) GeneratePresignedUploadUrl(string fileName, string contentType, double expireMinutes = 15)
        {
            var key = $"{Guid.NewGuid()}_{fileName}";

            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = key,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.AddMinutes(expireMinutes),
                ContentType = contentType
            };

            var url = _s3Client.GetPreSignedURL(request);
            return (url, key);
        }

        /// <summary>
        /// ダウンロード用の署名付き URL を生成します。
        /// クライアントはこの URL にアクセスしてファイルを直接取得します。
        /// </summary>
        public string GeneratePresignedDownloadUrl(string key, double expireMinutes = 15)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddMinutes(expireMinutes)
            };

            return _s3Client.GetPreSignedURL(request);
        }

        public async Task DeleteAsync(string key)
        {
            await _s3Client.DeleteObjectAsync(_bucketName, key);
        }
    }
}