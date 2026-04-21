// File: src/Smart_Stay_Awake/Cli/CliParser.cs
// Purpose: Parse and validate command-line arguments into CliOptions.
// Design notes:
//   * --help is supported (sets CliOptions.ShowHelp = true).
//   * Two-phase approach to enforce mutual exclusivity:
//       1) Pre-scan loop: detect presence of --for / --until tokens (without parsing values).
//          If both are present, throw immediately with a clear message.
//       2) Main parse loop: parse arguments and validate as before.
//   * Heavy trace breadcrumbs for novice-friendly diagnostics.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Smart_Stay_Awake
{
    /// <summary>
    /// Thrown when command-line parsing or validation fails.
    /// Program.Main will catch this and route to Fatal().
    /// </summary>
    internal sealed class CliParseException : Exception
    {
        public CliParseException(string message) : base(message) { }
    }

    internal static class CliParser
    {
        // Regex to parse duration tokens like "3d4h5s", "1h30m", "90m", "3600s".
        // We allow any order but will sum each unit found.
        private static readonly Regex DurationPattern = new(
            @"(?ix)                           # ignore case, allow comments
              (?:
                 (?<days>\d+)\s*d
               | (?<hours>\d+)\s*h
               | (?<minutes>\d+)\s*m
               | (?<seconds>\d+)\s*s
              )", RegexOptions.Compiled);

        /// <summary>
        /// Main entry: parse args into CliOptions and validate.
        /// Throws CliParseException on error.
        /// </summary>
        public static CliOptions Parse(string[] args)
        {
            Trace.WriteLine("Smart_Stay_Awake: CliParser: Entered CliParser.Parse ...");
            // Raw, unparsed command line (includes exe path)
            string rawCommandLine = Environment.CommandLine ?? string.Empty;
            Trace.WriteLine("Smart_Stay_Awake: CliParser: Raw command line  : " + rawCommandLine);
            Trace.WriteLine("Smart_Stay_Awake: CliParser: Raw arguments only: " + GetRawArgsOnly());
            //
            try
            {
                if (args == null) args = Array.Empty<string>();

                // -------------------------------------------------------------------------------------
                // PRE-SCAN (no parsing): detect presence of mutually exclusive --for and --until tokens
                // -------------------------------------------------------------------------------------
                Trace.WriteLine("Smart_Stay_Awake: CliParser: Start pre-scan for mutually exclusive options");
                bool sawForToken = false;
                bool sawUntilToken = false;
                for (int i = 0; i < args.Length; i++)
                {
                    string token = (args[i] ?? string.Empty).Trim();
                    if (token.Equals("--for", StringComparison.OrdinalIgnoreCase))
                    {
                        sawForToken = true;
                        continue;
                    }
                    if (token.Equals("--until", StringComparison.OrdinalIgnoreCase))
                    {
                        sawUntilToken = true;
                        continue;
                    }
                }
                // Enforce mutual exclusivity BEFORE parsing any values
                if (sawForToken && sawUntilToken)
                {
                    Trace.WriteLine("Smart_Stay_Awake: CliParser: Pre-scan detected both --for and --until.");
                    throw new CliParseException("Smart_Stay_Awake: CliParser: --for and --until are mutually exclusive; specify only one.");
                }
                Trace.WriteLine("Smart_Stay_Awake: CliParser: End pre-scan (OK).");

                // -----------------------------------------------------------------
                // Parse variables
                // -----------------------------------------------------------------
                string? iconPath = null;
                bool verbose = false;
                TimeSpan? forDuration = null;
                DateTime? untilLocal = null;
                double? untilTargetEpoch = null;  // For two-stage ceiling
                bool showHelp = false;

                // -----------------------------------------------------------------
                // MAIN PARSE AND VALIDATE LOOP
                // -----------------------------------------------------------------
                // Basic tokenization/iteration. We accept switches like:
                // --help
                // --icon PATH
                // --for DURATION
                // --until "YYYY-M-D H:m:s"
                // --verbose
                Trace.WriteLine("Smart_Stay_Awake: CliParser: Start of main parse loop");
                for (int i = 0; i < args.Length; i++)
                {
                    string token = (args[i] ?? string.Empty).Trim();

                    if (token.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                        token.Equals("/?"))
                    {
                        showHelp = true;
                        // Continue parsing in case user combined flags; harmless.
                        continue;
                    }
                    if (token.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
                    {
                        verbose = true;
                        continue;
                    }
                    if (token.Equals("--icon", StringComparison.OrdinalIgnoreCase))
                    {
                        // Expect a path after --icon
                        if (++i >= args.Length)
                            throw new CliParseException("Smart_Stay_Awake: CliParser: Missing value for --icon. Expected a file path.");
                        string rawPath = (args[i] ?? string.Empty).Trim().Trim('"');
                        if (string.IsNullOrWhiteSpace(rawPath))
                            throw new CliParseException("Smart_Stay_Awake: CliParser: Empty path provided for --icon.");
                        string fullPath;
                        try
                        {
                            fullPath = Path.GetFullPath(rawPath);
                        }
                        catch (Exception ex)
                        {
                            throw new CliParseException($"Smart_Stay_Awake: CliParser: Invalid --icon path: {rawPath}\n{ex.Message}");
                        }
                        if (!File.Exists(fullPath))
                            throw new CliParseException($"Smart_Stay_Awake: CliParser: --icon file not found: {fullPath}");

                        string ext = Path.GetExtension(fullPath);
                        if (!AppConfig.ALLOWED_ICON_EXTENSIONS.Contains(ext))
                            throw new CliParseException($"Smart_Stay_Awake: CliParser: --icon extension '{ext}' not supported. Allowed: .png .jpg .jpeg .bmp .gif .ico");
                        iconPath = fullPath;
                        Trace.WriteLine($"Smart_Stay_Awake: CliParser: IconPath accepted: {iconPath}");
                        continue;
                    }
                    if (token.Equals("--for", StringComparison.OrdinalIgnoreCase))
                    {
                        if (++i >= args.Length)
                            throw new CliParseException("Smart_Stay_Awake: CliParser: Missing value for --for. Example: --for 1h30m");
                        string raw = (args[i] ?? string.Empty).Trim().ToLowerInvariant();
                        // Bare number (no unit) means minutes
                        if (IsAllDigits(raw))
                        {
                            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int mins))
                                throw new CliParseException($"Smart_Stay_Awake: CliParser: --for value '{raw}' is not a valid integer.");
                            forDuration = TimeSpan.FromMinutes(mins);
                        }
                        else
                        {
                            // Sum up tokens like 3d4h5s, 1h30m, 90m etc.
                            forDuration = ParseDuration(raw);
                        }
                        // Validate bounds
                        ValidateDurationBounds(forDuration.Value);
                        Trace.WriteLine($"Smart_Stay_Awake: CliParser: ForDuration accepted: {forDuration}");
                        continue;
                    }
                    if (token.Equals("--until", StringComparison.OrdinalIgnoreCase))
                    {
                        if (++i >= args.Length)
                            throw new CliParseException("Smart_Stay_Awake: CliParser: Missing value for --until. Example: --until \"2025-1-2 3:4:5\"");
                        string raw = (args[i] ?? string.Empty).Trim().Trim('"', '\''); // relaxed quotes
                        var parsed = ParseUntilLocalRelaxed(raw);

                        // Calculate epoch seconds for two-stage ceiling (Stage 1)
                        DateTime utc = parsed.ToUniversalTime();
                        double targetEpoch = (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

                        // Validate bounds relative to "now" (using epoch for precision)
                        double nowCeil = Math.Ceiling(GetCurrentEpochSeconds());
                        int deltaSeconds = (int)(targetEpoch - nowCeil);

                        if (deltaSeconds < AppConfig.MIN_AUTO_QUIT_SECONDS)
                            throw new CliParseException($"Smart_Stay_Awake: CliParser: --until must be at least {AppConfig.MIN_AUTO_QUIT_SECONDS} seconds in the future (got {deltaSeconds}s).");
                        if (deltaSeconds > AppConfig.MAX_AUTO_QUIT_SECONDS)
                            throw new CliParseException($"Smart_Stay_Awake: CliParser: --until must be within {AppConfig.MAX_AUTO_QUIT_SECONDS} seconds from now (~365 days).");

                        untilLocal = parsed;
                        untilTargetEpoch = targetEpoch;  // Store for two-stage ceiling

                        Trace.WriteLine($"Smart_Stay_Awake: CliParser: UntilLocal accepted: {untilLocal:yyyy-MM-dd HH:mm:ss}");
                        Trace.WriteLine($"Smart_Stay_Awake: CliParser: UntilTargetEpoch (Stage 1): {targetEpoch:F1} (delta={deltaSeconds}s)");
                        continue;
                    }
                    // Unknown token -> friendly error
                    throw new CliParseException($"Smart_Stay_Awake: CliParser: Unknown argument: {token}");
                }
                Trace.WriteLine("Smart_Stay_Awake: CliParser: End of main parse loop");

                // -----------------------------------------------------------------
                // Build result
                // -----------------------------------------------------------------
                var result = new CliOptions
                {
                    IconPath = iconPath,
                    Verbose = verbose,
                    ForDuration = forDuration,
                    UntilLocal = untilLocal,
                    UntilTargetEpoch = untilTargetEpoch,
                    ShowHelp = showHelp
                };
                Trace.WriteLine("Smart_Stay_Awake: CliParser: Exiting CliParser.Parse (success).");
                return result;      // of CliOptions
            }
            catch (CliParseException)
            {
                Trace.WriteLine("Smart_Stay_Awake: CliParser: Caught CliParseException in CliParser.Parse.");
                Trace.WriteLine("Smart_Stay_Awake: CliParser: Exiting CliParser.Parse (error).");
                throw;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Smart_Stay_Awake: CliParser: Caught unexpected exception in CliParser.Parse: " + ex);
                Trace.WriteLine("Smart_Stay_Awake: CliParser: Exiting CliParser.Parse (error).");
                throw new CliParseException("Smart_Stay_Awake: CliParser: Unexpected error while parsing arguments: " + ex.Message);
            }
        }

        // ------------------- Helpers -------------------

        private static bool IsAllDigits(string s)
        {
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return s.Length > 0;
        }
        //
        private static TimeSpan ParseDuration(string raw)
        {
            Trace.WriteLine("Smart_Stay_Awake: CliParser: Start of ParseDuration TRY");
            try
            {
                long days = 0, hours = 0, minutes = 0, seconds = 0;
                foreach (Match m in DurationPattern.Matches(raw))
                {
                    if (m.Groups["days"].Success) days += long.Parse(m.Groups["days"].Value, CultureInfo.InvariantCulture);
                    if (m.Groups["hours"].Success) hours += long.Parse(m.Groups["hours"].Value, CultureInfo.InvariantCulture);
                    if (m.Groups["minutes"].Success) minutes += long.Parse(m.Groups["minutes"].Value, CultureInfo.InvariantCulture);
                    if (m.Groups["seconds"].Success) seconds += long.Parse(m.Groups["seconds"].Value, CultureInfo.InvariantCulture);
                }
                // If regex found nothing, the format is invalid.
                if (days == 0 && hours == 0 && minutes == 0 && seconds == 0)
                    throw new CliParseException($"Smart_Stay_Awake: CliParser: Invalid --for format '{raw}'. Examples: 3d4h5s, 1h30m, 90m, 3600s");
                // Build a TimeSpan safely (check for overflow)
                try
                {
                    var ts = TimeSpan.FromDays(days)
                           + TimeSpan.FromHours(hours)
                           + TimeSpan.FromMinutes(minutes)
                           + TimeSpan.FromSeconds(seconds);
                    Trace.WriteLine("Smart_Stay_Awake: CliParser: End of ParseDuration TRY (success)");
                    return ts;
                }
                catch (OverflowException)
                {
                    throw new CliParseException("Smart_Stay_Awake: CliParser: --for duration is too large.");
                }
            }
            catch (CliParseException)
            {
                Trace.WriteLine("Smart_Stay_Awake: CliParser: Caught   ParseDuration CATCH (CliParseException)");
                Trace.WriteLine("Smart_Stay_Awake: CliParser: End of   ParseDuration TRY");
                throw;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Smart_Stay_Awake: CliParser: Caught   ParseDuration CATCH (Unexpected): " + ex);
                Trace.WriteLine("Smart_Stay_Awake: CliParser: End of   ParseDuration TRY");
                throw new CliParseException("Smart_Stay_Awake: CliParser: Unexpected error parsing --for: " + ex.Message);
            }
        }
        //
        private static void ValidateDurationBounds(TimeSpan value)
        {
            double secs = value.TotalSeconds;
            if (secs < AppConfig.MIN_AUTO_QUIT_SECONDS)
                throw new CliParseException($"Smart_Stay_Awake: CliParser: --for must be at least {AppConfig.MIN_AUTO_QUIT_SECONDS} seconds.");
            if (secs > AppConfig.MAX_AUTO_QUIT_SECONDS)
                throw new CliParseException($"Smart_Stay_Awake: CliParser: --for must be <= {AppConfig.MAX_AUTO_QUIT_SECONDS} seconds (~365 days).");
        }
        //
        private static DateTime ParseUntilLocalRelaxed(string raw)
        {
            // Accept various spacing (e.g., "2025- 1- 2  3: 2: 1").
            // Strategy: compress multiple spaces to single space; then try exact ISO-ish DateTime parsing formats (InvariantCulture),
            // then fall back to general local parse.
            string compressed = Regex.Replace(raw ?? string.Empty, @"\s+", " ").Trim();
            // Try ISO-ish first:
            if (DateTime.TryParseExact(
                    compressed,
                    new[] {
                        "yyyy-M-d H:m:s",
                        "yyyy-MM-dd HH:mm:ss",
                        "yyyy-M-d HH:mm:ss",
                        "yyyy-MM-dd H:m:s"
                    },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces,
                    out DateTime dt))
            {
                return dt;
            }
            // Fallback: last resort general parse (still local)
            if (DateTime.TryParse(compressed, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt))
                return dt;
            throw new CliParseException($"Smart_Stay_Awake: CliParser: --until value '{raw}' could not be parsed as a local 24-hour timestamp (yyyy-M-d H:m:s).");
        }
        private static string GetRawArgsOnly()
        {
            string raw = Environment.CommandLine ?? string.Empty;
            raw = raw.Trim();

            if (string.IsNullOrEmpty(raw)) return string.Empty;

            // If exe path is quoted: "C:\path\app.exe" <args>
            if (raw.StartsWith("\""))
            {
                int close = raw.IndexOf('"', 1);
                if (close >= 0)
                {
                    // Skip closing quote and any following whitespace
                    int start = close + 1;
                    while (start < raw.Length && char.IsWhiteSpace(raw[start])) start++;
                    return raw.Substring(start);
                }
                return string.Empty; // no closing quote found → no args we can safely extract
            }

            // Unquoted exe path case: C:\path\app.exe <args>
            int firstSpace = raw.IndexOf(' ');
            if (firstSpace < 0) return string.Empty; // no args
            int begin = firstSpace + 1;
            while (begin < raw.Length && char.IsWhiteSpace(raw[begin])) begin++;
            return raw.Substring(begin);
        }

        /// <summary>
        /// Get current UTC time as epoch seconds (seconds since Unix epoch: 1970-01-01 00:00:00 UTC).
        /// Used for two-stage ceiling precision in timer arming.
        /// </summary>
        private static double GetCurrentEpochSeconds()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
