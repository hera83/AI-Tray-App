using System;
using System.Threading.Tasks;
using TrayApp.Models;

namespace TrayApp.Infrastructure
{
    public interface IChatRepository
    {
        Task SaveSessionAsync(ChatSession session);
        Task<ChatSession[]?> LoadAllAsync();
        Task<ChatSession?> LoadLatestAsync();
        Task DeleteSessionAsync(Guid id);
        Task DeleteAllAsync();
    }
}
