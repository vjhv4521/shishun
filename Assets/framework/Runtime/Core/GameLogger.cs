using System;
using UnityEngine;

namespace Haven.Framework.Core
{
    public enum GameLogLevel
    {
        Info,
        Warning,
        Error
    }

    public readonly struct GameLogEntry
    {
        public GameLogEntry(GameLogLevel level, string module, string message, string code, string correlationId, Exception exception)
        {
            TimestampUtc = DateTime.UtcNow;
            Level = level;
            Module = string.IsNullOrWhiteSpace(module) ? "Unknown" : module;
            Message = message ?? string.Empty;
            Code = code ?? string.Empty;
            CorrelationId = correlationId ?? string.Empty;
            Exception = exception;
        }

        public DateTime TimestampUtc { get; }
        public GameLogLevel Level { get; }
        public string Module { get; }
        public string Message { get; }
        public string Code { get; }
        public string CorrelationId { get; }
        public Exception Exception { get; }

        public override string ToString()
        {
            var codePart = string.IsNullOrEmpty(Code) ? string.Empty : $"/{Code}";
            var correlationPart = string.IsNullOrEmpty(CorrelationId) ? string.Empty : $" correlation={CorrelationId}";
            return $"[{TimestampUtc:O}] [{Module}{codePart}]{correlationPart} {Message}";
        }
    }

    public interface IGameLogSink
    {
        void Write(GameLogEntry entry);
    }

    public sealed class UnityGameLogSink : IGameLogSink
    {
        public void Write(GameLogEntry entry)
        {
            var text = entry.ToString();
            switch (entry.Level)
            {
                case GameLogLevel.Warning:
                    Debug.LogWarning(text);
                    break;
                case GameLogLevel.Error:
                    Debug.LogError(entry.Exception == null ? text : $"{text}\n{entry.Exception}");
                    break;
                default:
                    Debug.Log(text);
                    break;
            }
        }
    }

    public static class GameLog
    {
        private static IGameLogSink _sink = new UnityGameLogSink();

        public static IGameLogSink Sink
        {
            get => _sink;
            set => _sink = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static void Info(string module, string message, string code = null, string correlationId = null)
        {
            Sink.Write(new GameLogEntry(GameLogLevel.Info, module, message, code, correlationId, null));
        }

        public static void Warning(string module, string message, string code = null, string correlationId = null)
        {
            Sink.Write(new GameLogEntry(GameLogLevel.Warning, module, message, code, correlationId, null));
        }

        public static void Error(string module, string message, string code = null, Exception exception = null, string correlationId = null)
        {
            Sink.Write(new GameLogEntry(GameLogLevel.Error, module, message, code, correlationId, exception));
        }
    }
}
