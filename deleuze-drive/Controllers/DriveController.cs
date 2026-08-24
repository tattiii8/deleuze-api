using System;
using System.Security.Claims;
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

        [HttpPost("upload-url")]
        public async Task<IActionResult> GetUploadUrl([FromBody] CreateUploadUrlRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FileName))
                return BadRequest(new { error = "ファイル名が指定されていません。" });

            var tenantId = User.FindFirst("tenant_id")?.Value 
                        ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                        ?? request.TenantId;

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return BadRequest(new { error = "テナントIDが取得できませんでした。" });
            }

            var contentType = string.IsNullOrWhiteSpace(request.ContentType)
                ? "application/octet-stream"
                : request.ContentType;

            var (uploadUrl, key) = _storageService.GeneratePresignedUploadUrl(tenantId, request.FileName, contentType);

            var metadata = new FileMetadata
            {
                FileName = request.FileName,
                ContentType = contentType,
                ByteSize = request.ByteSize,
                StoragePath = key
            };

            _dbContext.Files.Add(metadata);
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                uploadUrl,
                file = metadata
            });
        }

        [HttpGet("files/{id:guid}/download-url")]
        public async Task<IActionResult> GetDownloadUrl(Guid id)
        {
            var fileMetadata = await _dbContext.Files.FindAsync(id);
            if (fileMetadata == null)
            {
                return NotFound(new { error = "指定されたファイルのメタデータが見つかりません。" });
            }

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
                // ログ出力等の例外ハンドリング
            }

            _dbContext.Files.Remove(file);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }
    }

    public class CreateUploadUrlRequest
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long ByteSize { get; set; }
        public string? TenantId { get; set; }
    }
}