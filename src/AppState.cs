// File: src/Smart_Stay_Awake/AppState.cs
// Purpose: Immutable snapshot of runtime configuration and environment, created once in Program.Main.

using System;
using System.Diagnostics;
using System.Reflection;

namespace Smart_Stay_Awake
{
    internal enum IconSourceKind { None, CliPath, EmbeddedFallback, NeighborFile }
    internal enum PlannedMode { Indefinite, ForDuration, UntilTimestamp }

    internal sealed class AppState
    {
        // Identity / environment
        public string AppDisplayName { get; }
        public string AppVersion { get; }
        public string ExeDir { get; }
        public DateTime ProcessStartUtc { get; }
        public string LocalTimeZoneId { get; }

        // Tracing & CLI
        public bool TraceEnabled { get; }
        public string? LogFullPath { get; }
        public CliOptions Options { get; }

        // Icon/Image decision (not the pixels yet)
        public IconSourceKind IconSource { get; }
        public string? IconPath { get; }

        // Timer planning
        public PlannedMode Mode { get; }
        public DateTime? PlannedUntilLocal { get; }
        public TimeSpan? PlannedTotal { get; }

        // Platform info
        public string WindowsBuild { get; }
        public string DotnetRuntimeVersion { get; }

        private AppState(
            string appDisplayName,
            string appVersion,
            string exeDir,
            DateTime processStartUtc,
            string localTimeZoneId,
            bool traceEnabled,
            string? logFullPath,
            CliOptions options,
            IconSourceKind iconSource,
            string? iconPath,
            PlannedMode mode,
            DateTime? plannedUntilLocal,
            TimeSpan? plannedTotal,
            string windowsBuild,
            string dotnetRuntimeVersion)
        {
            AppDisplayName = appDisplayName;
            AppVersion = appVersion;
            ExeDir = exeDir;
            ProcessStartUtc = processStartUtc;
            LocalTimeZoneId = localTimeZoneId;

            TraceEnabled = traceEnabled;
            LogFullPath = logFullPath;
            Options = options;

            IconSource = iconSource;
            IconPath = iconPath;

            Mode = mode;
            PlannedUntilLocal = plannedUntilLocal;
            PlannedTotal = plannedTotal;

            WindowsBuild = windowsBuild;
            DotnetRuntimeVersion = dotnetRuntimeVersion;
        }

        public static AppState Create(CliOptions opts, bool traceEnabled, string? logPath)
        {
            Trace.WriteLine("Entered AppState.Create ...");

            // Identity
            var asm = Assembly.GetEntryAssembly();
            string version =
                asm?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? asm?.GetName()?.Version?.ToString()
                ?? "0.0.0";
            string exeDir = AppContext.BaseDirectory;

            // Basic env
            DateTime processStartUtc = DateTime.UtcNow;
            string tzId = TimeZoneInfo.Local.Id;

            // Icon decision policy: (1) CLI path if present; else (2) embedded fallback later; else (3) neighbor file if we add that rule; else None.
            IconSourceKind iconSource;
            string? iconPath = null;
            if (!string.IsNullOrWhiteSpace(opts.IconPath))
            {
                iconSource = IconSourceKind.CliPath;
                iconPath = opts.IconPath;
            }
            else
            {
                iconSource = IconSourceKind.None; // we’ll swap to EmbeddedFallback later when we wire assets
            }

            // Timer planning
            PlannedMode mode;
            DateTime? until = null;
            TimeSpan? total = null;

            if (opts.ForDuration.HasValue)
            {
                mode = PlannedMode.ForDuration;
                total = opts.ForDuration.Value;
            }
            else if (opts.UntilLocal.HasValue)
            {
                mode = PlannedMode.UntilTimestamp;
                until = opts.UntilLocal.Value;
            }
            else
            {
                mode = PlannedMode.Indefinite;
            }

            // Platform diagnostics (best-effort)
            string windowsBuild = Environment.OSVersion.VersionString; // e.g., "Microsoft Windows NT 10.0.26100.0"
            string dotnetRuntimeVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription; // ".NET 10.0.x"

            var state = new AppState(
                appDisplayName: AppConfig.APP_DISPLAY_NAME,
                appVersion: version,
                exeDir: exeDir,
                processStartUtc: processStartUtc,
                localTimeZoneId: tzId,
                traceEnabled: traceEnabled,
                logFullPath: logPath,
                options: opts,
                iconSource: iconSource,
                iconPath: iconPath,
                mode: mode,
                plannedUntilLocal: until,
                plannedTotal: total,
                windowsBuild: windowsBuild,
                dotnetRuntimeVersion: dotnetRuntimeVersion);

            Trace.WriteLine($"AppState.Create: Version={version}, TraceEnabled={traceEnabled}, LogPath={(logPath ?? "<none>")}");
            Trace.WriteLine($"AppState.Create: Mode={state.Mode}, Until={state.PlannedUntilLocal?.ToString("yyyy-MM-dd HH:mm:ss") ?? "<none>"}, Total={state.PlannedTotal?.ToString() ?? "<none>"}");
            Trace.WriteLine($"AppState.Create: IconSource={state.IconSource}, IconPath={state.IconPath ?? "<none>"}");
            Trace.WriteLine("Exiting AppState.Create.");
            return state;
        }
    }
}
