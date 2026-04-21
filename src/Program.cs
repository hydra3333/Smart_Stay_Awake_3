// File: src/Smart_Stay_Awake/Program.cs
// Purpose: Entry point. Initialize WinForms defaults, parse CLI, help, configure tracing, build AppState, run MainForm.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Smart_Stay_Awake
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>

        // Developer TRACING override:
        //  (Preferably) Globally:
        //      Change FORCED_TRACE_DEFAULT in AppConfig.cs to true, to force tracing even without --verbose.
        //  (2nd choice) Here:
        //      Change FORCED_TRACE below to true, to force tracing even without --verbose.
        //
        private const bool FORCED_TRACE = AppConfig.FORCED_TRACE_DEFAULT;

        [STAThread]
        static void Main(string[] args)
        {
#if DEBUG
            // Mirror Tracing to VS Output window while debugging
            // Ensure this is present at the top of main() even if tracing is re-configured later.
            //Trace.Listeners.Add(new DefaultTraceListener());
            //Trace.AutoFlush = true;
#endif

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            // Modern WinForms defaults (high DPI plumbing, default fonts, etc.)
            // Apply modern WinForms defaults (visual styles, text rendering, DPI via ApplicationHighDpiMode).
            ApplicationConfiguration.Initialize();

            try
            {
                Trace.WriteLine("Smart_Stay_Awake: Main: Entered Program.Main ...");

                // 1) Parse CLI -> may throw CliParseException with a friendly message.
                Trace.WriteLine("Smart_Stay_Awake: Main: Start of CLI parse TRY");
                CliOptions opts = CliParser.Parse(args);
                Trace.WriteLine("Smart_Stay_Awake: Main: End of   CLI parse TRY (success)");

                // 1a) If --help was requested, print usage and exit(0) BEFORE any other work.
                if (opts.ShowHelp)
                {
                    Trace.WriteLine("Smart_Stay_Awake: Main: Help requested via CLI. Showing usage and exiting.");
                    ShowHelpAndExit();
                    return; // (unreached; Environment.Exit inside)
                }

                // 2) Configure tracing
                bool enableTrace = FORCED_TRACE || opts.Verbose;
                string? logFullPath = null;

                // When Tracing enabled, setup Listeners
                Trace.WriteLine("Smart_Stay_Awake: Main: Start of tracing config TRY");
                try
                {
                    Trace.Listeners.Clear(); // always clear first; avoid OutputDebugString noise
                    if (enableTrace)
                    {
                        // Build log path: prefer beside EXE; fall back to LocalAppData.
                        string exeDir = AppContext.BaseDirectory;
                        string today = DateTime.Now.ToString(AppConfig.LOG_FILE_DATE_FORMAT, CultureInfo.InvariantCulture);
                        string fileName = $"{AppConfig.LOG_FILE_BASENAME_PREFIX}{today}.log";
                        // Try next to the EXE first:
                        string candidate = Path.Combine(exeDir, fileName);
                        // Fallback dir:
                        string fallbackDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            AppConfig.LOG_FALLBACK_SUBDIR);
                        // CanOpenForWrite() creates the file to test writability
                        if (CanOpenForWrite(candidate))
                        {
                            logFullPath = candidate;
                        }
                        else
                        {
                            Directory.CreateDirectory(fallbackDir);
                            logFullPath = Path.Combine(fallbackDir, fileName);
                        }
                        // Overwrite each run to keep logs tight.
                        // But CanOpenForWrite() creates the file to test writability anyway, so no need to open/truncate.
                        // TextWriterTraceListener will create the file if missing and append if present;
                        // since CanOpenForWrite has already created a fresh file, you'll get a clean file anyway.
                        // using (File.Create(logFullPath)) { /* truncate/close */ }
                        // Add listener and write a small session header.
                        Trace.Listeners.Add(new TextWriterTraceListener(logFullPath));
#if DEBUG
                        // Mirror to VS Output window while debugging
                        Trace.Listeners.Add(new DefaultTraceListener());
#endif
                        Trace.AutoFlush = true;
                        Trace.WriteLine("==================================================");
                        Trace.WriteLine($"[Init] {AppConfig.APP_INTERNAL_NAME} start {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
                        // Grab version like AppState.Create(...) would
                        var asm = System.Reflection.Assembly.GetEntryAssembly();
                        var ver = asm?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
                                  ?? asm?.GetName()?.Version?.ToString() ?? "0.0.0";
                        Trace.WriteLine($"[Init] Version: {ver}");
                        Trace.WriteLine("[Init] Args: " + string.Join(" ", args));
                        Trace.WriteLine("[Init] Tracing enabled.");
                        Trace.WriteLine("[Init] Log file: " + logFullPath);
#if DEBUG
                        // Uncomment this line to open the log folder in Explorer automatically on startup (debug convenience).
                        try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + logFullPath + "\""); } catch { }
#endif

                    }
                }
                catch (Exception ex)
                {
                    // If tracing setup fails, continue without tracing.
                    // The app should still run; we just note it via a best-effort MessageBox.
                    MessageBox.Show(
                        "Warning: Failed to initialize trace logging.\n" + ex.Message,
                        AppConfig.APP_DISPLAY_NAME + " - Warning",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                Trace.WriteLine("Smart_Stay_Awake: Main: End of   tracing config TRY");

                // 3) Create AppState (collects version, options, trace flags) (currently unused)
                AppState mainApplicationState = AppState.Create(opts, enableTrace, logFullPath);

                // 4) Run the MainForm (inject application state)
                Trace.WriteLine("Smart_Stay_Awake: Main: Starting UI.MainForm ...");
                Application.Run(new UI.MainForm(mainApplicationState));
                Trace.WriteLine("Smart_Stay_Awake: Main: UI.MainForm exited normally.");
                Trace.WriteLine("Smart_Stay_Awake: Main: Exiting Program.Main (success).");
            }
            catch (CliParseException ex)
            {
                // Known, friendly CLI error (bad args, etc.)
                Trace.WriteLine("Smart_Stay_Awake: Main: Caught CliParseException in Program.Main.");
                FatalHelper.Fatal("Smart_Stay_Awake: Main: Command-line error:\n" + ex.Message, exitCode: 2);
            }
            catch (Exception ex)
            {
                // Unexpected, catch-all path.
                Trace.WriteLine("Smart_Stay_Awake: Main: Caught unexpected exception in Program.Main: " + ex);
                FatalHelper.Fatal("Smart_Stay_Awake: Main: Unexpected error: " + ex.Message, exitCode: 99);
            }
        }

        /// <summary>
        /// Prints concise usage/help to Console (if visible) AND shows a MessageBox (for double-clicked runs).
        /// Exits the process with code 0.
        /// Now uses HelpTextBuilder so help content is shared with the UI Help button.
        /// </summary>
        private static void ShowHelpAndExit()
        {
            Trace.WriteLine("Smart_Stay_Awake: Entered ShowHelpAndExit ...");
            Trace.WriteLine("Smart_Stay_Awake: ShowHelpAndExit: Help requested via CLI. Showing usage and exiting.");

            string help = HelpTextBuilder.BuildHelpText();

            // Write to Trace
            try { Trace.WriteLine(help); } catch { /* ignore */ }

            // Show a modal dialog (if launched by double-click)
            Trace.WriteLine("Smart_Stay_Awake: ShowHelpAndExit: about to show a MessageBox with help");
            try
            {
                MessageBox.Show(help, AppConfig.APP_DISPLAY_NAME + " - Help", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { /* ignore */ }


            Trace.WriteLine("Smart_Stay_Awake: Exiting ShowHelpAndExit ...");
            try { Environment.Exit(0); } catch { /* ignore */ }
        }

        private static bool CanOpenForWrite(string fullPath)
        {
            try
            {
                // If directory doesn't exist, create it.
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                // Try open exclusive write (side effect = overwrite), then close.
                using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
    /// <summary>
    /// Builds the help text shown by CLI --help and the Help button.
    /// Centralizes help content to avoid drift between CLI and UI.
    /// </summary>
    internal static class HelpTextBuilder
    {
        /// <summary>
        /// Builds and returns the complete help text as a string.
        /// Identical content to what CLI --help displays.
        /// </summary>
        /// <returns>Multi-line help text with usage, options, and examples.</returns>
        public static string BuildHelpText()
        {
            Trace.WriteLine("Smart_Stay_Awake: Entered BuildHelpText ...");
            var sb = new StringBuilder();
            sb.AppendLine("Usage:");
            sb.AppendLine("  Smart_Stay_Awake.exe [--help] [--verbose] [--icon PATH] (--for DURATION | --until \"YYYY-M-D H:m:s\")");
            sb.AppendLine();
            sb.AppendLine("Options:");
            sb.AppendLine("  --help");
            sb.AppendLine("      Show this help.");
            sb.AppendLine();
            sb.AppendLine("  --verbose");
            sb.AppendLine("      Enable detailed trace logging to a file.");
            sb.AppendLine("      Log file created/overwritten alongside \"Smart_Stay_Awake.exe\" if that directory is writable,");
            sb.AppendLine("               otherwise in: \"%LocalAppData%\\Smart_Stay_Awake\\Logs\"");
            sb.AppendLine();
            sb.AppendLine("  --icon PATH");
            sb.AppendLine("      Use a specific image file for the window/tray icon.");
            sb.AppendLine("      Supports: PNG, JPG/JPEG, BMP, GIF, ICO.");
            sb.AppendLine();
            sb.AppendLine("  --for DURATION");
            sb.AppendLine("      Mutually exclusive with --until.");
            sb.AppendLine("      Keep awake for a fixed time, then quit gracefully.");
            sb.AppendLine("      DURATION accepts days/hours/minutes/seconds in a single string:");
            sb.AppendLine("        3d4h5s, 2h, 90m, 3600s, 1h30m");
            sb.AppendLine("      A bare number means minutes. Use 0 to disable the timer (max duration).");
            sb.AppendLine($"      Bounds: at least {AppConfig.MIN_AUTO_QUIT_SECONDS}s; at most {AppConfig.MAX_AUTO_QUIT_SECONDS}s (~365 days).");
            sb.AppendLine();
            sb.AppendLine("  --until \"YYYY-M-D H:m:s\"");
            sb.AppendLine("      Mutually exclusive with --for.");
            sb.AppendLine("      Keep awake until the given local 24-hour timestamp, then quit gracefully.");
            sb.AppendLine("      Examples (relaxed spacing & 1-2 digit parts accepted):");
            sb.AppendLine("        \"2025-01-02 23:22:21\"");
            sb.AppendLine("        \"2025- 1- 2  3: 2: 1\"");
            sb.AppendLine("        \"2025-1-2 3:2:1\"");
            sb.AppendLine($"      Bounds: at least {AppConfig.MIN_AUTO_QUIT_SECONDS}s ahead, at most {AppConfig.MAX_AUTO_QUIT_SECONDS}s from now.");
            sb.AppendLine();
            Trace.WriteLine("Smart_Stay_Awake: Exiting BuildHelpText");
            return sb.ToString();
        }
    }
}
