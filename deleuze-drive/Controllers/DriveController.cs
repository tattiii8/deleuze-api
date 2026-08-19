using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeleuzeDrive.Data;
using DeleuzeDrive.Models;

namespace DeleuzeDrive.Controllers
{
    [ApiController]
    [Route("")] // 👈 ここだけ修正
    public class DriveController : ControllerBase
    {
        private readonly DriveDbContext _dbContext;

        public DriveController(DriveDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet("files")]
        public async Task<IActionResult> GetFiles()
        {
            var files = await _dbContext.Files.ToListAsync();
            return Ok(files);
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("ファイルが空です。");

            var storagePath = Path.Combine("/tmp/uploads", Guid.NewGuid() + "_" + file.FileName);

            var metadata = new FileMetadata
            {
                FileName = file.FileName,
                ContentType = file.ContentType,
                ByteSize = file.Length,
                StoragePath = storagePath
            };

            _dbContext.Files.Add(metadata);
            await _dbContext.SaveChangesAsync();

            return Ok(metadata);
        }

        [HttpDelete("files/{id:guid}")]
        public async Task<IActionResult> DeleteFile(Guid id)
        {
            var file = await _dbContext.Files.FindAsync(id);
            if (file == null)
                return NotFound();

            _dbContext.Files.Remove(file);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }
    }
}