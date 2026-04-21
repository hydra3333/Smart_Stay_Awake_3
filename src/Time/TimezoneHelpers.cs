// File: src/Smart_Stay_Awake/Time/TimezoneHelpers.cs
// Purpose: Timezone and DST-aware timestamp parsing utilities.
// Handles edge cases: invalid times (spring-forward gaps), ambiguous times (fall-back overlaps).

using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Smart_Stay_Awake.Time
{
    /// <summary>
    /// Static utility class for timezone-aware timestamp parsing and epoch conversion.
    /// Provides robust DST validation to prevent errors during spring-forward and fall-back transitions.
    /// </summary>
    internal static class TimezoneHelpers
    {
        /// <summary>
        /// Parse a local timestamp string with full DST validation.
        /// Rejects nonexistent times (spring-forward gap) and ambiguous times (fall-back overlap).
        /// </summary>
        /// <param name="timestamp">Local timestamp string in format "YYYY-MM-DD HH:MM:SS" (relaxed spacing allowed)</param>
        /// <param name="epochSeconds">Output: seconds since Unix epoch (1970-01-01 00:00:00 UTC)</param>
        /// <returns>Parsed local DateTime (for display purposes)</returns>
        /// <exception cref="ArgumentException">Thrown on invalid format, calendar errors, or DST issues</exception>
        public static DateTime ParseUntilTimestamp(string timestamp, out double epochSeconds)
        {
            Trace.WriteLine($"Smart_Stay_Awake: TimezoneHelpers: Entered ParseUntilTimestamp with: '{timestamp}'");

            // Step 1: Parse with regex (relaxed spacing, 1-2 digit components)
            var match = Regex.Match(timestamp, @"^\s*(\d{4})\s*-\s*(\d{1,2})\s*-\s*(\d{1,2})\s+(\d{1,2})\s*:\s*(\d{1,2})\s*:\s*(\d{1,2})\s*$");

            if (!match.Success)
            {
                Trace.WriteLine("Smart_Stay_Awake: TimezoneHelpers: Regex match FAILED - invalid format");
                throw new ArgumentException("Invalid --until format. Use: YYYY-MM-DD HH:MM:SS");
            }

            int year = int.Parse(match.Groups[1].Value);
            int month = int.Parse(match.Groups[2].Value);
            int day = int.Parse(match.Groups[3].Value);
            int hour = int.Parse(match.Groups[4].Value);
            int minute = int.Parse(match.Groups[5].Value);
            int second = int.Parse(match.Groups[6].Value);

            Trace.WriteLine($"Smart_Stay_Awake: TimezoneHelpers: Parsed components: Y={year} M={month} D={day} H={hour} M={minute} S={second}");

            // Step 2: Calendar validation (DateTime constructor will throw on invalid dates)
            DateTime localDt;
            try
            {
                localDt = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
                Trace.WriteLine($"Smart_Stay_Awake: TimezoneHelpers: Calendar validation PASSED: {localDt:yyyy-MM-dd HH:mm:ss}");
            }
            catch (ArgumentOutOfRangeException ex)
            {
                Trace.WriteLine($"Smart_Stay_Awake: TimezoneHelpers: Calendar validation FAILED: {ex.Message}");
                throw new ArgumentException($"Invalid calendar date/time in --until: {ex.Message}", ex);
            }

            // Step 3: DST validation via TimeZoneInfo
            TimeZoneInfo localZone = TimeZoneInfo.Local;
            Trace.WriteLine($"Smart_Stay_Awake: TimezoneHelpers: Local timezone: {localZone.Id} (StandardName={localZone.StandardName})");

            // Check if this local time is invalid (spring-forward gap)
            if (localZone.IsInvalidTime(localDt))
            {
                Trace.WriteLine("Smart_Stay_Awake: TimezoneHelpers: DST validation FAILED - nonexistent time (spring-forward gap)");
                throw new ArgumentException(
                    "--until is not a valid local time (nonexistent due to DST spring-forward gap). " +
                    "Please choose a different time.");
            }

            // Check if this local time is ambiguous (fall-back overlap)
            if (localZone.IsAmbiguousTime(localDt))
            {
                Trace.WriteLine("Smart_Stay_Awake: TimezoneHelpers: DST validation FAILED - ambiguous time (fall-back overlap)");
                throw new ArgumentException(
                    "--until is ambiguous (falls in the repeated DST fall-back hour). " +
                    "Please choose a different time.");
            }

            Trace.WriteLine("Smart_Stay_Awake: TimezoneHelpers: DST validation PASSED (no spring-forward gap, no fall-back ambiguity)");

            // Step 4: Convert to UTC (guaranteed safe now)
            DateTime utcDt = TimeZoneInfo.ConvertTimeToUtc(localDt, localZone);
            Trace.WriteLine($"Smart_Stay_Awake: TimezoneHelpers: Converted to UTC: {utcDt:yyyy-MM-dd HH:mm:ss} UTC");

            // Step 5: Convert to epoch seconds
            epochSeconds = (utcDt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            Trace.WriteLine($"Smart_Stay_Awake: TimezoneHelpers: Epoch seconds: {epochSeconds:F1}");

            Trace.WriteLine("Smart_Stay_Awake: TimezoneHelpers: Exiting ParseUntilTimestamp (success)");
            return localDt;  // Return original local time for display
        }

        /// <summary>
        /// Get current UTC time as epoch seconds (seconds since Unix epoch: 1970-01-01 00:00:00 UTC).
        /// Used for countdown calculations and two-stage ceiling.
        /// </summary>
        /// <returns>Current time as epoch seconds (double precision)</returns>
        public static double GetCurrentEpochSeconds()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}