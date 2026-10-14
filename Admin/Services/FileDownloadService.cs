using Admin.Interfaces;
using Microsoft.JSInterop;

namespace Admin.Services
{
    /// <summary>Saves a file the API returned (Excel exports) in the user's browser.</summary>
    public sealed class FileDownloadService
    {
        private readonly IJSRuntime _js;
        public FileDownloadService(IJSRuntime js) => _js = js;

        public async Task SaveAsync(DownloadedFile file)
        {
            using var stream = new MemoryStream(file.Bytes);
            using var streamRef = new DotNetStreamReference(stream);
            await _js.InvokeVoidAsync("pogov.downloadFile", file.FileName, file.ContentType, streamRef);
        }
    }
}
