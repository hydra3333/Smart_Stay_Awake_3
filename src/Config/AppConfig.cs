// File: src/Smart_Stay_Awake/Config/AppConfig.cs
// Purpose: Single home for app-wide constants and defaults.
// Naming: per your rule, CONSTANTS in UPPER_CASE_WITH_UNDERSCORES.

using System;
using System.Collections.Generic;

namespace Smart_Stay_Awake
{
    /// <summary>
    /// Defines which power management implementation to use.
    /// </summary>
    internal enum PowerManagementStrategy
    {
        /// <summary>
        /// Legacy: SetThreadExecutionState (fire-and-forget, thread-level flags).
        /// Simpler but less robust. No handle cleanup needed.
        /// </summary>
        LegacyThreadExecutionState,

        /// <summary>
        /// Modern: Power Request APIs (handle-based, process-level requests).
        /// More robust, better diagnostic support, explicit lifecycle management.
        /// </summary>
        ModernPowerRequests
    }

    internal static class AppConfig
    {
        // ---- Power management strategy ---------------------------------------
        /// <summary>
        /// Selects which power management implementation to use.
        /// Default: ModernPowerRequests (recommended for Windows 7+).
        /// Switch to LegacyThreadExecutionState for comparison or fallback testing.
        /// </summary>
        public static readonly PowerManagementStrategy POWER_STRATEGY = PowerManagementStrategy.ModernPowerRequests;

        // ---- App identity ----------------------------------------------------
        public const string APP_INTERNAL_NAME = "Smart_Stay_Awake";
        public const string APP_DISPLAY_NAME = "Smart Stay Awake 3";

        // ---- Tracing defaults -------------------------------------------------
        // Developer-only hard override (forces tracing even without --verbose).
        public const bool FORCED_TRACE_DEFAULT = false;

        // Where/how trace logs are written when tracing is ON.
        public const string LOG_FILE_BASENAME_PREFIX = "Smart_Stay_Awake_Trace_";
        public const string LOG_FILE_DATE_FORMAT = "yyyyMMdd"; // zero-padded y-M-d
        public const string LOG_FALLBACK_SUBDIR = "Smart_Stay_Awake\\Logs"; // under %LocalAppData%

        // ---- Auto-quit bounds (seconds) --------------------------------------
        public const int MIN_AUTO_QUIT_SECONDS = 10;                 // ≥ 10s
        public const int MAX_AUTO_QUIT_SECONDS = 365 * 24 * 3600;    // ≤ ~365d

        // ---- Timer cadence configuration (adaptive update frequency) ----
        // Each tuple: (ThresholdSeconds, CadenceMilliseconds)
        // Rule: if remaining > threshold, use this cadence
        public static readonly (int ThresholdSeconds, int CadenceMs)[] COUNTDOWN_CADENCE = new[]
        {
            (3600, 600_000),  // > 60 min:  update every 10 minutes
            (1800, 300_000),  // > 30 min:  update every  5 minutes
            ( 900,  60_000),  // > 15 min:  update every  1 minute
            ( 600,  30_000),  // > 10 min:  update every 30 seconds
            ( 300,  15_000),  // >  5 min:  update every 15 seconds
            ( 120,  10_000),  // >  2 min:  update every 10 seconds
            (  60,   5_000),  // >  1 min:  update every  5 seconds
            (  30,   2_000),  // > 30 sec:  update every  2 seconds
            (  -1,   1_000),  // ≤ 30 sec:  update every  1 second (catch-all)
        };

        // ---- Snap-to-boundary configuration (cadence alignment) ----
        // Snap-to only applies when remaining >= this threshold
        public const int HARD_CADENCE_SNAP_TO_THRESHOLD_SECONDS = 60;

        // Micro-sleep protection: if snap interval < this, add one full cadence
        public const int SNAP_TO_MIN_INTERVAL_MS = 200;

        // ---- Allowed image/icon extensions -----------------------------------
        public static readonly HashSet<string> ALLOWED_ICON_EXTENSIONS =
            new(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico" };

        // ---- Icon imaging knobs ---------------------------------------------------------
        public const int WINDOW_MAX_IMAGE_EDGE_PX = 768; // 512; // 768; // 1024; // Max edge length for loaded image in main window
        // We'll use TRAY_ICON_SIZES in the next sub-iteration when we switch to multi-size ICO:
        public static readonly int[] TRAY_ICON_SIZES = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
    }
}
