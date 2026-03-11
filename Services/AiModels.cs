using System.Collections.Generic;

namespace TrayApp.Services
{
    // Models for request/response to external AI gateway
    public class AiChatRequest
    {
        public string?      Model       { get; set; }
        public List<AiMessage>? Messages { get; set; }
        public double       Temperature { get; set; } = 0.7;
        public bool         Stream      { get; set; } = false;
    }

    public class AiMessage
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = string.Empty;
    }

    public class AiChatResponse
    {
        public string? Id { get; set; }
        public AiChoice[]? Choices { get; set; }
    }

    public class AiChoice
    {
        public AiMessage? Message { get; set; }
    }

    public class OllamaChatRequest
    {
        public string? Model { get; set; }
        public List<AiMessage>? Messages { get; set; }
        public bool? Stream { get; set; }
    }

    public class OllamaChatResponse
    {
        public string? Model { get; set; }
        public AiMessage? Message { get; set; }
        public bool Done { get; set; }
    }
}
