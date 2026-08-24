using System;
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
using Deleuze.Shared.Constants;

namespace DeleuzeDrive.Controllers
{
    [ApiController]
    [Authorize]
    [Route(ApiRoutes.Drive.Base)]
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

        [HttpPost("objects")]
        public async Task<IActionResult> CreateObject(
            [FromBody] UploadUrlRequest request)
        {
            var authenticatedTenantId = GetAuthenticatedTenantId();

            if (string.IsNullOrEmpty(authenticatedTenantId))
            {
                _logger.LogWarning(
                    "[DriveController] Unauthorized: TenantId missing from authentication token or ApiKey.");

                return Unauthorized(new
                {
                    error = "認証情報からテナント情報を特定できませんでした。"
                });
            }

            _logger.LogInformation(
                "[DriveController] Creating object for Tenant: {TenantId}, File: {FileName}",
                authenticatedTenantId,
                request.FileName);

            var (uploadUrl, key) =
                _storageService.GeneratePresignedUploadUrl(
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

                _logger.LogInformation(
                    "[DriveController] Created object metadata record. ObjectId: {ObjectId}, Path: {StoragePath}",
                    fileRecord.Id,
                    fileRecord.StoragePath);

                return Ok(new
                {
                    objectId = fileRecord.Id,
                    uploadUrl,
                    key
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[DriveController] Failed to save object metadata to DB for Tenant: {TenantId}",
                    authenticatedTenantId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        error = "ファイルメタデータの保存に失敗しました。"
                    });
            }
        }

        [HttpGet("objects")]
        public async Task<IActionResult> GetObjects()
        {
            var authenticatedTenantId = GetAuthenticatedTenantId();

            if (string.IsNullOrEmpty(authenticatedTenantId))
            {
                _logger.LogWarning(
                    "[DriveController] Unauthorized: TenantId missing from context.");

                return Unauthorized(new
                {
                    error = "認証情報からテナント情報を特定できませんでした。"
                });
            }

            _logger.LogInformation(
                "[DriveController] Fetching objects for Tenant: {TenantId}",
                authenticatedTenantId);

            try
            {
                var objects = await _dbContext.Files.ToListAsync();

                return Ok(objects);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[DriveController] Error retrieving objects for Tenant: {TenantId}",
                    authenticatedTenantId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        error = "ファイル一覧の取得に失敗しました。"
                    });
            }
        }

        [HttpGet("objects/{objectId:guid}")]
        public async Task<IActionResult> GetObject(Guid objectId)
        {
            var authenticatedTenantId = GetAuthenticatedTenantId();

            if (string.IsNullOrEmpty(authenticatedTenantId))
            {
                return Unauthorized(new
                {
                    error = "認証情報からテナント情報を特定できませんでした。"
                });
            }

            var objectRecord =
                await _dbContext.Files.FindAsync(objectId);

            if (objectRecord == null)
            {
                _logger.LogWarning(
                    "[DriveController] Object not found. ObjectId: {ObjectId}",
                    objectId);

                return NotFound(new
                {
                    error = "ファイルが見つかりません。"
                });
            }

            _logger.LogInformation(
                "[DriveController] Generating download URL for ObjectId: {ObjectId}, Path: {StoragePath}",
                objectId,
                objectRecord.StoragePath);

            var downloadUrl =
                _storageService.GeneratePresignedDownloadUrl(
                    objectRecord.StoragePath);

            return Ok(new
            {
                objectId = objectRecord.Id,
                fileName = objectRecord.FileName,
                contentType = objectRecord.ContentType,
                byteSize = objectRecord.ByteSize,
                downloadUrl
            });
        }

        [HttpDelete("objects/{objectId:guid}")]
        public async Task<IActionResult> DeleteObject(Guid objectId)
        {
            var authenticatedTenantId = GetAuthenticatedTenantId();

            if (string.IsNullOrEmpty(authenticatedTenantId))
            {
                return Unauthorized(new
                {
                    error = "認証情報からテナント情報を特定できませんでした。"
                });
            }

            var objectRecord =
                await _dbContext.Files.FindAsync(objectId);

            if (objectRecord == null)
            {
                _logger.LogWarning(
                    "[DriveController] Delete failed. Object not found. ObjectId: {ObjectId}",
                    objectId);

                return NotFound(new
                {
                    error = "ファイルが見つかりません。"
                });
            }

            _logger.LogInformation(
                "[DriveController] Deleting object. ObjectId: {ObjectId}, Path: {StoragePath}",
                objectId,
                objectRecord.StoragePath);

            try
            {
                await _storageService.DeleteAsync(
                    objectRecord.StoragePath);

                _dbContext.Files.Remove(objectRecord);

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "[DriveController] Successfully deleted object. ObjectId: {ObjectId}",
                    objectId);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[DriveController] Error deleting object. ObjectId: {ObjectId}",
                    objectId);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        error = "ファイルの削除に失敗しました。"
                    });
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