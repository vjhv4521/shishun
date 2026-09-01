using System;

namespace Haven.Framework.Core
{
    public sealed class FrameworkError
    {
        public FrameworkError(string code, string message, string module, bool retryable = false, Exception exception = null)
        {
            Code = string.IsNullOrWhiteSpace(code) ? "UNKNOWN" : code;
            Message = message ?? string.Empty;
            Module = string.IsNullOrWhiteSpace(module) ? "Unknown" : module;
            Retryable = retryable;
            Exception = exception;
        }

        public string Code { get; }
        public string Message { get; }
        public string Module { get; }
        public bool Retryable { get; }
        public Exception Exception { get; }

        public override string ToString()
        {
            return $"[{Module}/{Code}] {Message}";
        }
    }

    public readonly struct FrameworkResult
    {
        private FrameworkResult(bool succeeded, FrameworkError error)
        {
            Succeeded = succeeded;
            Error = error;
        }

        public bool Succeeded { get; }
        public FrameworkError Error { get; }

        public static FrameworkResult Success()
        {
            return new FrameworkResult(true, null);
        }

        public static FrameworkResult Failure(FrameworkError error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            return new FrameworkResult(false, error);
        }
    }

    public readonly struct FrameworkResult<T>
    {
        private FrameworkResult(bool succeeded, T value, FrameworkError error)
        {
            Succeeded = succeeded;
            Value = value;
            Error = error;
        }

        public bool Succeeded { get; }
        public T Value { get; }
        public FrameworkError Error { get; }

        public static FrameworkResult<T> Success(T value)
        {
            return new FrameworkResult<T>(true, value, null);
        }

        public static FrameworkResult<T> Failure(FrameworkError error)
        {
            if (error == null)
                throw new ArgumentNullException(nameof(error));
            return new FrameworkResult<T>(false, default, error);
        }
    }
}
