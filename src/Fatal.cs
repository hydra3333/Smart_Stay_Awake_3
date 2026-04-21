// File: src/Smart_Stay_Awake/Fatal.cs
// Purpose: Centralized "fatal error" helper.
// Shows a modal message box and exits the process with a specific code.
// Pre-UI safe: do not depend on any Form/NotifyIcon being initialized.

using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace Smart_Stay_Awake
{
    internal static class FatalHelper
    {
        /// <summary>
        /// Show a modal error dialog (themed if ApplicationConfiguration.Initialize() was called),
        /// write a final trace line (if tracing), and exit with the given code.
        /// </summary>
        public static void Fatal(string message, int exitCode = 1)
        {
            try
            {
                Trace.WriteLine("Fatal: " + message);
            }
            catch
            {
                // If trace isn't configured, ignore.
            }

            try
            {
                MessageBox.Show(
                    message,
                    AppConfig.APP_DISPLAY_NAME + " - Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
            catch
            {
                // As a last resort if MessageBox fails, we still exit.
            }

            try
            {
                Environment.Exit(exitCode);
                return;
            }
            catch
            {
                // If Environment.Exit throws (extremely rare), we fall through.
            }
        }
    }
}
