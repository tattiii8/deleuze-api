using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeleuzeDrive.Data;
using DeleuzeDrive.Models;

namespace DeleuzeDrive.Controllers
{
    [ApiController]
    [Authorize] // 👈 トークン認証を必須化（未認証は 401）
    [Route("")]
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

            // 保存先ディレクトリが存在しない場合は作成
            var uploadDir = "/tmp/uploads";
            if (!Directory.Exists(uploadDir))
            {
                Directory.CreateDirectory(uploadDir);
            }

            var storagePath = Path.Combine(uploadDir, Guid.NewGuid() + "_" + file.FileName);

            // 物理ファイルをディスクへ保存
            await using (var stream = new FileStream(storagePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

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

        /// <summary>
        /// ファイルをダウンロードする
        /// </summary>
        [HttpGet("files/{id:guid}/download")]
        public async Task<IActionResult> DownloadFile(Guid id)
        {
            var fileMetadata = await _dbContext.Files.FindAsync(id);
            if (fileMetadata == null)
            {
                return NotFound(new { error = "指定されたファイルのメタデータが見つかりません。" });
            }

            // 物理ストレージ上にファイルが存在するかチェック
            if (!System.IO.File.Exists(fileMetadata.StoragePath))
            {
                return NotFound(new { error = "物理ストレージ上にファイルが存在しません。" });
            }

            var contentType = string.IsNullOrWhiteSpace(fileMetadata.ContentType)
                ? "application/octet-stream"
                : fileMetadata.ContentType;

            // PhysicalFile を使用してファイルをストリーミングレスポンスとして返す
            // 第3引数に fileDownloadName を渡すことで、ブラウザ側で Content-Disposition ヘッダーが自動設定されます
            return PhysicalFile(
                fileMetadata.StoragePath,
                contentType,
                fileDownloadName: fileMetadata.FileName
            );
        }

        [HttpDelete("files/{id:guid}")]
        public async Task<IActionResult> DeleteFile(Guid id)
        {
            var file = await _dbContext.Files.FindAsync(id);
            if (file == null)
                return NotFound();

            // 物理ファイルが存在する場合は削除
            if (System.IO.File.Exists(file.StoragePath))
            {
                try
                {
                    System.IO.File.Delete(file.StoragePath);
                }
                catch (Exception)
                {
                    // ログ出力等の例外ハンドリングを適宜行う
                }
            }

            _dbContext.Files.Remove(file);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }
    }
}