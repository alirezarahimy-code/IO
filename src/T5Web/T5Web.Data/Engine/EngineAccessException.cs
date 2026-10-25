using System;

namespace T5Web.Data.Engine
{
    /// <summary>
    /// Raised when the engine's data-access layer cannot answer a read.
    ///
    /// This is deliberately a distinct exception type: a schema/data-layer problem is
    /// an *operational* condition (someone upgraded the engine, or the database is
    /// down), not a programming error. The web layer catches it and renders one Persian
    /// page; it must never surface as a stack trace or as a silent empty grid.
    /// </summary>
    public sealed class EngineAccessException : Exception
    {
        public EngineAccessException(string operation, Exception inner)
            : base("دسترسی به موتور T5 ناموفق بود (" + operation + "): " +
                   (inner == null ? "خطای نامشخص" : inner.Message), inner)
        {
            Operation = operation ?? string.Empty;
        }

        /// <summary>Which operation failed, for the diagnostics page and the log.</summary>
        public string Operation { get; private set; }
    }
}
