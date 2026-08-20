using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace DeleuzeDrive.Services
{
    public interface IStorageService
    {
        Task<string> UploadAsync(IFormFile file);
        Task<Stream> DownloadAsync(string key);
        Task DeleteAsync(string key);
    }
}