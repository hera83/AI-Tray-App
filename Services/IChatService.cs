using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TrayApp.Services
{
    public interface IChatService
    {
        IAsyncEnumerable<string> StreamResponseAsync(string prompt, CancellationToken cancellationToken, string? modelOverride = null);
        Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken cancellationToken);
    }
}
