// File: src/Smart_Stay_Awake/Time/CountdownPlanner.cs
// Purpose: Countdown timer planning and formatting utilities.
// Provides adaptive cadence selection, snap-to-boundary logic, and time formatting.

using System;
using System.Diagnostics;

namespace Smart_Stay_Awake.Time
{
    /// <summary>
    /// Static utility class for countdown timer cadence planning and time formatting.
    /// Implements adaptive update frequency and snap-to-boundary alignment.
    /// </summary>
    internal static class CountdownPlanner
    {
        /// <summary>
        /// Get the base cadence (update interval) in milliseconds based on remaining time.
        /// Uses adaptive bands: updates more frequently as deadline approaches.
        /// </summary>
        /// <param name="remainingSeconds">Seconds remaining until deadline</param>
        /// <returns>Cadence in milliseconds (how often to update countdown display)</returns>
        public static int GetBaseCadenceMs(int remainingSeconds)
        {
            // Iterate through bands, return first match
            foreach (var (threshold, cadenceMs) in AppConfig.COUNTDOWN_CADENCE)
            {
                if (remainingSeconds > threshold)
                {
                    return cadenceMs;
                }
            }

            // Should never reach here (catch-all threshold = -1), but safety fallback
            Trace.WriteLine("Smart_Stay_Awake: CountdownPlanner: WARNING - No cadence band matched, using 1s fallback");
            return 1_000;
        }

        /// <summary>
        /// Calculate the next timer interval with optional snap-to-boundary alignment.
        /// Snap-to aligns first update to a "round" cadence boundary for cleaner countdown display.
        /// </summary>
        /// <param name="autoQuitDeadlineTicks">Monotonic deadline (Stopwatch ticks)</param>
        /// <returns>Next timer interval in milliseconds</returns>
        public static int CalculateNextInterval(long autoQuitDeadlineTicks)
        {
            // Get remaining time (monotonic clock, immune to system time changes)
            long nowTicks = Stopwatch.GetTimestamp();
            long remainingTicks = Math.Max(0, autoQuitDeadlineTicks - nowTicks);
            int remainingSeconds = (int)(remainingTicks / Stopwatch.Frequency);

            // Get base cadence for current remaining time
            int baseCadenceMs = GetBaseCadenceMs(remainingSeconds);
            int nextIntervalMs = baseCadenceMs;

            // Apply snap-to-boundary (only when remaining >= threshold)
            if (remainingSeconds >= AppConfig.HARD_CADENCE_SNAP_TO_THRESHOLD_SECONDS)
            {
                int cadenceSeconds = Math.Max(1, baseCadenceMs / 1000);
                int phaseSeconds = remainingSeconds % cadenceSeconds;

                if (phaseSeconds != 0)
                {
                    int snapMs = phaseSeconds * 1000;

                    // Micro-sleep protection: avoid firing too soon
                    if (snapMs < AppConfig.SNAP_TO_MIN_INTERVAL_MS)
                    {
                        snapMs += cadenceSeconds * 1000;
                    }

                    // Only snap if sooner than regular cadence
                    if (snapMs < baseCadenceMs)
                    {
                        nextIntervalMs = snapMs;
                        Trace.WriteLine("Smart_Stay_Awake: CountdownPlanner: Snap-to applied: remaining=" + remainingSeconds + "s, phase=" + phaseSeconds + "s, snap=" + snapMs + "ms");
                    }
                }
            }

            return nextIntervalMs;
        }

        /// <summary>
        /// Format seconds as "DDDd HH:mm:ss" or "HH:mm:ss" (omit days if 0).
        /// Used for "Time remaining" field display.
        /// </summary>
        /// <param name="totalSeconds">Total seconds to format</param>
        /// <returns>Formatted time string</returns>
        public static string FormatDHMS(int totalSeconds)
        {
            int days = totalSeconds / 86400;
            int remainder = totalSeconds % 86400;
            int hours = remainder / 3600;
            remainder %= 3600;
            int minutes = remainder / 60;
            int seconds = remainder % 60;

            return (days > 0)
                ? days + "d " + hours.ToString("D2") + ":" + minutes.ToString("D2") + ":" + seconds.ToString("D2")
                : hours.ToString("D2") + ":" + minutes.ToString("D2") + ":" + seconds.ToString("D2");
        }

        /// <summary>
        /// Format seconds as "HH:mm:ss".
        /// Used for "Timer update frequency" field display.
        /// </summary>
        /// <param name="totalSeconds">Total seconds to format</param>
        /// <returns>Formatted time string</returns>
        public static string FormatHMS(int totalSeconds)
        {
            int hours = totalSeconds / 3600;
            int remainder = totalSeconds % 3600;
            int minutes = remainder / 60;
            int seconds = remainder % 60;

            return hours.ToString("D2") + ":" + minutes.ToString("D2") + ":" + seconds.ToString("D2");
        }
    }
}