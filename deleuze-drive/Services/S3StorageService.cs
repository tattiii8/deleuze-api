using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DeleuzeDrive.Services
{
    public class S3StorageService : IStorageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;
        private readonly ILogger<S3StorageService> _logger;

        public S3StorageService(
            IAmazonS3 s3Client, 
            IConfiguration configuration,
            ILogger<S3StorageService> logger)
        {
            _s3Client = s3Client;
            _bucketName = configuration["AWS:BucketName"] ?? "my-default-bucket";
            _logger = logger;
        }

        /// <summary>
        /// テナントIDごとのパス（{tenantId}/{Guid}_{fileName}）に保存するための署名付き URL と S3 Key を生成します。
        /// </summary>
        public (string UploadUrl, string Key) GeneratePresignedUploadUrl(string tenantId, string fileName, string contentType, double expireMinutes = 15)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                _logger.LogWarning("[S3] Upload URL generation failed. TenantId is null or empty.");
                throw new ArgumentNullException(nameof(tenantId));
            }

            // パス・トラバーサル防止のためファイル名部分の純粋な名前のみを抽出
            var safeFileName = Path.GetFileName(fileName);

            // S3 の Key プレフィックス構造を構築: {tenantId}/{Guid}_{safeFileName}
            var key = $"{tenantId.Trim('/')}/{Guid.NewGuid()}_{safeFileName}";

            _logger.LogInformation("[S3] Generating UPLOAD URL. Bucket: {Bucket}, TenantId: {TenantId}, Key: {Key}, ContentType: {ContentType}, ExpiresInMinutes: {ExpireMinutes}", 
                _bucketName, tenantId, key, contentType, expireMinutes);

            try
            {
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = _bucketName,
                    Key = key,
                    Verb = HttpVerb.PUT,
                    Expires = DateTime.UtcNow.AddMinutes(expireMinutes),
                    ContentType = contentType
                };

                var url = _s3Client.GetPreSignedURL(request);
                _logger.LogInformation("[S3] Successfully generated UPLOAD URL for Key: {Key}", key);
                return (url, key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S3] Error generating UPLOAD URL for Key: {Key}", key);
                throw;
            }
        }

        /// <summary>
        /// ダウンロード用の署名付き URL を生成します。
        /// </summary>
        public string GeneratePresignedDownloadUrl(string key, double expireMinutes = 15)
        {
            _logger.LogInformation("[S3] Generating DOWNLOAD URL. Bucket: {Bucket}, Key: {Key}, ExpiresInMinutes: {ExpireMinutes}", 
                _bucketName, key, expireMinutes);

            try
            {
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = _bucketName,
                    Key = key,
                    Verb = HttpVerb.GET,
                    Expires = DateTime.UtcNow.AddMinutes(expireMinutes)
                };

                var url = _s3Client.GetPreSignedURL(request);
                _logger.LogInformation("[S3] Successfully generated DOWNLOAD URL for Key: {Key}", key);
                return url;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S3] Error generating DOWNLOAD URL for Key: {Key}", key);
                throw;
            }
        }

        /// <summary>
        /// 指定された単一オブジェクトを削除します。
        /// </summary>
        public async Task DeleteAsync(string key)
        {
            _logger.LogInformation("[S3] Deleting object. Bucket: {Bucket}, Key: {Key}", _bucketName, key);

            try
            {
                await _s3Client.DeleteObjectAsync(_bucketName, key);
                _logger.LogInformation("[S3] Successfully deleted object. Key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S3] Error deleting object for Key: {Key}", key);
                throw;
            }
        }

        /// <summary>
        /// 指定されたプレフィックス（例: テナントID）配下のすべてのオブジェクトを一括削除します。
        /// </summary>
        public async Task DeletePrefixAsync(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                _logger.LogWarning("[S3] DeletePrefix skipped. Prefix is null or empty.");
                return;
            }

            var formattedPrefix = prefix.EndsWith("/") ? prefix : $"{prefix}/";
            _logger.LogWarning("[S3] STARTING BATCH DELETION under prefix: {Prefix}, Bucket: {Bucket}", formattedPrefix, _bucketName);

            var listRequest = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = formattedPrefix
            };

            ListObjectsV2Response listResponse;
            int totalDeleted = 0;

            try
            {
                do
                {
                    listResponse = await _s3Client.ListObjectsV2Async(listRequest);

                    if (listResponse.S3Objects.Count > 0)
                    {
                        var deleteRequest = new DeleteObjectsRequest
                        {
                            BucketName = _bucketName,
                            Objects = listResponse.S3Objects
                                .Select(obj => new KeyVersion { Key = obj.Key })
                                .ToList()
                        };

                        await _s3Client.DeleteObjectsAsync(deleteRequest);
                        totalDeleted += listResponse.S3Objects.Count;

                        _logger.LogInformation("[S3] Deleted batch of {Count} objects under prefix: {Prefix}", 
                            listResponse.S3Objects.Count, formattedPrefix);
                    }

                    listRequest.ContinuationToken = listResponse.NextContinuationToken;

                } while (listResponse.IsTruncated);

                _logger.LogInformation("[S3] COMPLETED BATCH DELETION under prefix: {Prefix}. Total objects deleted: {TotalCount}", 
                    formattedPrefix, totalDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[S3] Error during batch deletion under prefix: {Prefix}", formattedPrefix);
                throw;
            }
        }
    }
}