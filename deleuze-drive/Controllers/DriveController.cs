using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DeleuzeDrive.Data;
using DeleuzeDrive.Models;
using DeleuzeDrive.Services;
using Deleuze.Shared.Constants; // 共通定数を参照

namespace DeleuzeDrive.Controllers
{
    [ApiController]
    [Authorize]
    [Route(ApiRoutes.Drive.Base)] // -> "api/drive"
    public class DriveController : ControllerBase
    {
        private readonly DriveDbContext _dbContext;
        private readonly IStorageService _storageService;
        private readonly ILogger<DriveController> _logger;

        public DriveController(
            DriveDbContext dbContext,
            IStorageService storageService,
            ILogger<DriveController> logger)
        {
            _dbContext = dbContext;
            _storageService = storageService;
            _logger = logger;
        }

        [HttpPost("upload-url")]
        public async Task<IActionResult> GetUploadUrl([FromBody] UploadUrlRequest request)
        {
            var authenticatedTenantId = GetAuthenticatedTenantId();

            if (string.IsNullOrEmpty(authenticatedTenantId))
            {
                _logger.LogWarning("[DriveController] Unauthorized: TenantId missing from authentication token or ApiKey.");
                return Unauthorized(new { error = "認証情報からテナント情報を特定できませんでした。" });
            }

            _logger.LogInformation("[DriveController] Generating upload URL for Tenant: {TenantId}, File: {FileName}", 
                authenticatedTenantId, request.FileName);

            var (uploadUrl, key) = _storageService.GeneratePresignedUploadUrl(
                authenticatedTenantId, 
                request.FileName, 
                request.ContentType
            );

            var fileRecord = new FileMetadata
            {
                Id = Guid.NewGuid(),
                FileName = request.FileName,
                ContentType = request.ContentType,
                ByteSize = request.ByteSize,
                StoragePath = key,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                _dbContext.Files.Add(fileRecord);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("[DriveController] Created file metadata record. FileId: {FileId}, Path: {StoragePath}", 
                    fileRecord.Id, fileRecord.StoragePath);

                return Ok(new
                {
                    uploadUrl,
                    fileId = fileRecord.Id,
                    key
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DriveController] Failed to save file metadata to DB for Tenant: {TenantId}", authenticatedTenantId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "ファイルメタデータの保存に失敗しました。" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetFiles()
        {
            var authenticatedTenantId = GetAuthenticatedTenantId();

            if (string.IsNullOrEmpty(authenticatedTenantId))
            {
                _logger.LogWarning("[DriveController] Unauthorized: TenantId missing from context.");
                return Unauthorized(new { error = "認証情報からテナント情報を特定できませんでした。" });
            }

            _logger.LogInformation("[DriveController] Fetching files for Tenant: {TenantId}", authenticatedTenantId);

            try
            {
                var files = await _dbContext.Files.ToListAsync();
                return Ok(files);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DriveController] Error retrieving files for Tenant: {TenantId}", authenticatedTenantId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "ファイル一覧の取得に失敗しました。" });
            }
        }

        [HttpGet("{fileId:guid}/download-url")]
        public async Task<IActionResult> GetDownloadUrl(Guid fileId)
        {
            var authenticatedTenantId = GetAuthenticatedTenantId();

            if (string.IsNullOrEmpty(authenticatedTenantId))
            {
                return Unauthorized(new { error = "認証情報からテナント情報を特定できませんでした。" });
            }

            var fileRecord = await _dbContext.Files.FindAsync(fileId);

            if (fileRecord == null)
            {
                _logger.LogWarning("[DriveController] File not found. FileId: {FileId}", fileId);
                return NotFound(new { error = "ファイルが見つかりません。" });
            }

            _logger.LogInformation("[DriveController] Generating download URL for FileId: {FileId}, Path: {StoragePath}", 
                fileId, fileRecord.StoragePath);

            var downloadUrl = _storageService.GeneratePresignedDownloadUrl(fileRecord.StoragePath);

            return Ok(new
            {
                downloadUrl,
                fileName = fileRecord.FileName,
                contentType = fileRecord.ContentType
            });
        }

        [HttpDelete("{fileId:guid}")]
        public async Task<IActionResult> DeleteFile(Guid fileId)
        {
            var authenticatedTenantId = GetAuthenticatedTenantId();

            if (string.IsNullOrEmpty(authenticatedTenantId))
            {
                return Unauthorized(new { error = "認証情報からテナント情報を特定できませんでした。" });
            }

            var fileRecord = await _dbContext.Files.FindAsync(fileId);

            if (fileRecord == null)
            {
                _logger.LogWarning("[DriveController] Delete failed. File not found. FileId: {FileId}", fileId);
                return NotFound(new { error = "ファイルが見つかりません。" });
            }

            _logger.LogInformation("[DriveController] Deleting file. FileId: {FileId}, Path: {StoragePath}", fileId, fileRecord.StoragePath);

            try
            {
                await _storageService.DeleteAsync(fileRecord.StoragePath);
                _dbContext.Files.Remove(fileRecord);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("[DriveController] Successfully deleted file. FileId: {FileId}", fileId);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DriveController] Error deleting file. FileId: {FileId}", fileId);
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "ファイルの削除に失敗しました。" });
            }
        }

        private string? GetAuthenticatedTenantId()
        {
            return User.FindFirst("tenant")?.Value 
                ?? User.FindFirst("tenant_id")?.Value 
                ?? User.FindFirst("tenantId")?.Value 
                ?? User.FindFirst("TenantId")?.Value 
                ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }

    public class UploadUrlRequest
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long ByteSize { get; set; }
    }
}