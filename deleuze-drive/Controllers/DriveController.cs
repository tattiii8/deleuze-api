using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeleuzeDrive.Data;
using DeleuzeDrive.Models;
using DeleuzeDrive.Services;

namespace DeleuzeDrive.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DriveController : ControllerBase
    {
        private readonly DriveDbContext _context;
        private readonly ITenantProvider _tenantProvider;
        private readonly string _baseStoragePath = "/app/uploads";

        public DriveController(DriveDbContext context, ITenantProvider tenantProvider)
        {
            _context = context;
            _tenantProvider = tenantProvider;
        }

        [HttpPost("upload")]
        [RequestSizeLimit(500 * 1024 * 1024)] // 500MB (動画・音声用)
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("ファイルが選択されていません。");

            var tenantId = _tenantProvider.GetTenantId();
            var tenantDirectory = Path.Combine(_baseStoragePath, tenantId);
            Directory.CreateDirectory(tenantDirectory);

            var fileId = Guid.NewGuid();
            var extension = Path.GetExtension(file.FileName);
            var savedFileName = $"{fileId}{extension}";
            var filePath = Path.Combine(tenantDirectory, savedFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var metadata = new FileMetadata
            {
                Id = fileId,
                FileName = file.FileName,
                ContentType = file.ContentType,
                Size = file.Length,
                StoragePath = savedFileName,
                TenantId = tenantId
            };

            _context.Files.Add(metadata);
            await _context.SaveChangesAsync();

            return Ok(metadata);
        }

        [HttpGet("files")]
        public async Task<IActionResult> ListFiles()
        {
            var tenantId = _tenantProvider.GetTenantId();
            var files = await _context.Files
                .Where(f => f.TenantId == tenantId)
                .ToListAsync();

            return Ok(files);
        }

        [HttpGet("files/{id}")]
        public async Task<IActionResult> GetFile(Guid id)
        {
            var tenantId = _tenantProvider.GetTenantId();
            var metadata = await _context.Files
                .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId);

            if (metadata == null) return NotFound();

            var filePath = Path.Combine(_baseStoragePath, tenantId, metadata.StoragePath);
            if (!System.IO.File.Exists(filePath)) return NotFound();

            // Range processing を有効にして動画/音声のストリーミング再生をサポート
            return PhysicalFile(filePath, metadata.ContentType, enableRangeProcessing: true);
        }
    }
}