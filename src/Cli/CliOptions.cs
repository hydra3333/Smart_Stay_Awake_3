// File: src/Smart_Stay_Awake/Cli/CliOptions.cs
// Purpose: Plain Old C# Object (POCO) that carries parsed command line options.
// Coding style: PascalCase for public properties, camelCase for locals.

using System;

namespace Smart_Stay_Awake
{
    /// <summary>
    /// Holds all command-line options after parsing.
    /// Keep this class intentionally simple and immutable-ish for clarity.
    /// </summary>
    internal sealed class CliOptions
    {
        /// <summary>
        /// If provided, absolute path to an image for window/tray at runtime.
        /// Allowed extensions in v1: .png .jpg .jpeg .bmp .gif .ico
        /// </summary>
        public string? IconPath { get; init; }

        /// <summary>
        /// Enables trace file logging when true (or always if FORCED_TRACE is true).
        /// </summary>
        public bool Verbose { get; init; }

        /// <summary>
        /// If provided, the app keeps-awake for this fixed duration (then quits).
        /// Mutually exclusive with UntilLocal.
        /// </summary>
        public TimeSpan? ForDuration { get; init; }

        /// <summary>
        /// If provided, the app keeps-awake until this local timestamp (then quits).
        /// Mutually exclusive with ForDuration.
        /// </summary>
        public DateTime? UntilLocal { get; init; }

        /// <summary>
        /// If provided, the target epoch seconds for --until (for two-stage ceiling precision).
        /// Used to recalculate delay just before arming timer in OnShown().
        /// </summary>
        public double? UntilTargetEpoch { get; init; }

        /// <summary>
        /// Optional: if you later add --help, set this and exit early after printing help.
        /// </summary>
        public bool ShowHelp { get; init; }

        /// <summary>
        /// Convenience: true if either ForDuration or UntilLocal is provided.
        /// </summary>
        public bool HasDuration => ForDuration.HasValue || UntilLocal.HasValue;

        public override string ToString()
            => $"IconPath={IconPath ?? "<none>"}, Verbose={Verbose}, For={ForDuration?.ToString() ?? "<none>"}, Until={UntilLocal?.ToString("yyyy-MM-dd HH:mm:ss") ?? "<none>"}";
    }
}
