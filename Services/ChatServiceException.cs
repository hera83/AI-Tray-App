using System;

namespace TrayApp.Services
{
    public enum ChatServiceErrorKind
    {
        Configuration,
        Connection,
        Timeout,
        Server,
        Unknown
    }

    public sealed class ChatServiceException : Exception
    {
        public ChatServiceErrorKind Kind { get; }

        public ChatServiceException(ChatServiceErrorKind kind, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Kind = kind;
        }
    }
}
