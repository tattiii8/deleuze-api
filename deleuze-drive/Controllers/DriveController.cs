using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeleuzeDrive.Data;
using DeleuzeDrive.Models;
using DeleuzeDrive.Services;

namespace DeleuzeDrive.Controllers
{
    [ApiController]
    [Authorize]
    [Route("")]
    public class DriveController : ControllerBase
    {
        private readonly DriveDbContext _dbContext;
        private readonly IStorageService _storageService;

        public DriveController(DriveDbContext dbContext, IStorageService storageService)
        {
            _dbContext = dbContext;
            _storageService = storageService;
        }

        [HttpGet("files")]
        public async Task<IActionResult> GetFiles()
        {
            var files = await _dbContext.Files.ToListAsync();
            return Ok(files);
        }

        /// <summary>
        /// S3 へ直接アップロードするための署名付き URL と メタデータレコードを発行・生成する
        /// </summary>
        [HttpPost("upload-url")]
        public async Task<IActionResult> GetUploadUrl([FromBody] CreateUploadUrlRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FileName))
                return BadRequest("ファイル名が指定されていません。");

            var contentType = string.IsNullOrWhiteSpace(request.ContentType)
                ? "application/octet-stream"
                : request.ContentType;

            // 1. S3 署名付きアップロード URL と Key を生成
            var (uploadUrl, key) = _storageService.GeneratePresignedUploadUrl(request.FileName, contentType);

            // 2. メタデータを DB に保存
            var metadata = new FileMetadata
            {
                FileName = request.FileName,
                ContentType = contentType,
                ByteSize = request.ByteSize,
                StoragePath = key
            };

            _dbContext.Files.Add(metadata);
            await _dbContext.SaveChangesAsync();

            // 3. クライアントに S3 アップロード用 URL と作成されたメタデータを返す
            return Ok(new
            {
                uploadUrl,
                file = metadata
            });
        }

        /// <summary>
        /// S3 から直接ダウンロードするための署名付き URL を取得する
        /// </summary>
        [HttpGet("files/{id:guid}/download-url")]
        public async Task<IActionResult> GetDownloadUrl(Guid id)
        {
            var fileMetadata = await _dbContext.Files.FindAsync(id);
            if (fileMetadata == null)
            {
                return NotFound(new { error = "指定されたファイルのメタデータが見つかりません。" });
            }

            // S3 直接ダウンロード用の署名付き URL を生成
            var downloadUrl = _storageService.GeneratePresignedDownloadUrl(fileMetadata.StoragePath);

            return Ok(new
            {
                downloadUrl,
                fileName = fileMetadata.FileName
            });
        }

        [HttpDelete("files/{id:guid}")]
        public async Task<IActionResult> DeleteFile(Guid id)
        {
            var file = await _dbContext.Files.FindAsync(id);
            if (file == null)
                return NotFound();

            try
            {
                await _storageService.DeleteAsync(file.StoragePath);
            }
            catch (Exception)
            {
                // ログ出力等の例外ハンドリングを適宜行う
            }

            _dbContext.Files.Remove(file);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }
    }

    /// <summary>
    /// アップロード URL 発行リクエスト用 DTO
    /// </summary>
    public class CreateUploadUrlRequest
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long ByteSize { get; set; }
    }
}