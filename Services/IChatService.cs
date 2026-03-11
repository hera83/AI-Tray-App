using System.Collections.Generic;
using System.Threading;

namespace TrayApp.Services
{
    public interface IChatService
    {
        IAsyncEnumerable<string> StreamResponseAsync(string prompt, CancellationToken cancellationToken);
    }
}
