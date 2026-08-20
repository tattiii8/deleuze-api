using System;
using System.Threading.Tasks;
using Amazon.S3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeleuzeDrive.Data;
using DeleuzeDrive.Models;
using DeleuzeDrive.Services;

namespace DeleuzeDrive.Controllers
{
    [ApiController]
    [Authorize] // 👈 トークン認証を必須化（未認証は 401）
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

        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("ファイルが空です。");

            // S3 ストレージへアップロードを行い、オブジェクトキーを取得
            var key = await _storageService.UploadAsync(file);

            var metadata = new FileMetadata
            {
                FileName = file.FileName,
                ContentType = file.ContentType,
                ByteSize = file.Length,
                StoragePath = key // S3 の Key を StoragePath として保存
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

            try
            {
                // S3 からオブジェクトのストリームを取得
                var stream = await _storageService.DownloadAsync(fileMetadata.StoragePath);

                var contentType = string.IsNullOrWhiteSpace(fileMetadata.ContentType)
                    ? "application/octet-stream"
                    : fileMetadata.ContentType;

                // File メソッドを使用してストリームをレスポンスとして返却
                return File(stream, contentType, fileDownloadName: fileMetadata.FileName);
            }
            catch (AmazonS3Exception)
            {
                return NotFound(new { error = "S3 上にファイルが存在しません。" });
            }
        }

        [HttpDelete("files/{id:guid}")]
        public async Task<IActionResult> DeleteFile(Guid id)
        {
            var file = await _dbContext.Files.FindAsync(id);
            if (file == null)
                return NotFound();

            // S3 上のオブジェクトを削除
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
}