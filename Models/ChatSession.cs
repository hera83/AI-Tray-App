using System;
using System.Collections.Generic;

namespace TrayApp.Models
{
    public class ChatSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = "New chat";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public List<Message> Messages { get; set; } = new List<Message>();
    }
}
