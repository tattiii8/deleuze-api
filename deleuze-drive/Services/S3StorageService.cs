using System;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
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

        public async Task<string> UploadAsync(IFormFile file)
        {
            var key = $"{Guid.NewGuid()}_{file.FileName}";
            await using var stream = file.OpenReadStream();
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = stream,
                ContentType = file.ContentType
            };
            await _s3Client.PutObjectAsync(request);
            return key; // DBには StoragePath として S3 の Key を保存
        }

        public async Task<Stream> DownloadAsync(string key)
        {
            var response = await _s3Client.GetObjectAsync(_bucketName, key);
            return response.ResponseStream;
        }

        public async Task DeleteAsync(string key)
        {
            await _s3Client.DeleteObjectAsync(_bucketName, key);
        }
    }
}