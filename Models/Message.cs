using System;
using TrayApp.ViewModels;

namespace TrayApp.Models
{
    public enum MessageRole
    {
        System,
        User,
        Assistant
    }

    public class Message : ObservableObject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        private MessageRole _role = MessageRole.Assistant;
        public MessageRole Role { get => _role; set => SetProperty(ref _role, value); }

        private string _content = string.Empty;
        public string Content { get => _content; set => SetProperty(ref _content, value); }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        private bool _isStreaming;
        public bool IsStreaming { get => _isStreaming; set => SetProperty(ref _isStreaming, value); }
    }
}
