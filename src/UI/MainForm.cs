using Smart_Stay_Awake.Imaging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.LinkLabel;

namespace Smart_Stay_Awake.UI
{
    // Accessibility matches internal AppState to avoid CS0051.
    internal partial class MainForm : Form
    {
        private readonly AppState _state;

        // System tray owner (already a stub with Initialize/SetIcon/Show/Hide).
        private TrayManager? _tray;

        // Simple image host – fills the client area. We assign a cloned Bitmap to it.
        private PictureBox? _picture;

        // Unified quit guard: prevents recursive calls during shutdown
        private bool _isClosing = false;

        // UI containers and controls (added below the image in Module 4)
        // COMMENTED OUT (iteration 2 - removed from UI to match Python layout):
        // private Label? _lblPrimary;              // "Smart Stay Awake 3"
        // private Label? _lblSecondary;            // "Ready • No timers armed"
        private Panel? _separator;                  // Thin horizontal line (still used)
        private FlowLayoutPanel? _buttonsRow;       // Buttons: Minimize/Quit
        private TableLayoutPanel? _fieldsTable;     // Dummy fields grid

        // FOR DEBUGGING
        // Container under the image (grey area) so we can trace its size
        private FlowLayoutPanel? _belowPanel;

        // Individual dummy field value labels (active fields for countdown display)
        private Label? _fldUntil;           // Auto-quit at timestamp
        private Label? _fldRemaining;       // Time remaining countdown
        private Label? _fldCadence;         // Timer update frequency

        // =====================================================================
        // Timer infrastructure (Module C - Auto-quit and countdown display)
        // =====================================================================

        // Auto-quit timer (one-shot background timer, fires once at deadline)
        private System.Threading.Timer? _autoQuitTimer;

        // Countdown display timer (UI thread timer, updates fields at adaptive intervals)
        private System.Windows.Forms.Timer? _countdownTimer;

        // Monotonic deadline tracking (immune to clock changes, DST, NTP adjustments)
        private long _autoQuitDeadlineTicks;

        // Wall-clock ETA for display (human-readable target time)
        private DateTime? _autoQuitWallClockEta;

        // Cadence tracking (low-churn field updates - only update when value changes)
        private int _lastCadenceSeconds = -1;

        // Constructor is lightweight: build controls, wire events, set fixed window policy.
        internal MainForm(AppState state)
        {
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Entered MainForm ctor ...");
            _state = state ?? throw new ArgumentNullException(nameof(state));

            InitializeComponent(); // designer baseline: AutoScaleMode=Dpi, etc.

            // Window policy (fixed dialog-like frame; no maximize)
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.CenterScreen;

            // Title uses AppState
            // this.Text = $"{_state.AppDisplayName} — v{_state.AppVersion}";
            this.Text = _state.AppDisplayName; // Just Title from AppState", no version

            // Cleanup hook (tray disposal, etc.)
            this.FormClosed += MainForm_FormClosed;

            // Build tray and a basic context menu (Show/Minimize/Quit will be wired later)
            _tray = new TrayManager(_state, this);
            _tray.Initialize();

            // Wire tray events to unified handlers
            _tray.ShowRequested += (s, e) =>
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Tray.ShowRequested => RestoreFromTray");
                RestoreFromTray();
            };
            _tray.QuitRequested += (s, e) =>
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Tray.QuitRequested => QuitApplication");
                QuitApplication("Tray.Quit");
            };

            // Add an image host (fills the client area)
            _picture = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            this.Controls.Add(_picture);

            Trace.WriteLine($"UI.MainForm: Using TraceEnabled={_state.TraceEnabled}");
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Exiting MainForm ctor.");
        }

        /// <summary>
        /// Prefer doing the image pipeline here (once) instead of the ctor.
        /// At this point the form handle exists; scaling/DPIs are fully resolved.
        /// After image loads, build the controls panel and resize the form.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Entered.");

            // Load and prepare image (sets PictureBox.Image and resizes form to image only)
            TryLoadPrepareAndApplyImageAndIcon();

            // Now convert layout: image at top, controls below
            BuildBelowImageLayout();

            // =====================================================================
            // Arm keep-awake: Prevent system sleep/hibernation (CRITICAL)
            // =====================================================================
            // Now that form is fully initialized (image loaded, layout built), arm keep-awake.
            // This blocks system sleep/hibernation while app is running.
            // Display monitor sleep is still allowed (ES_AWAYMODE_REQUIRED permits this).
            // CRITICAL: If arming fails, the app's entire purpose is defeated - this is FATAL.
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Arming keep-awake ...");
            bool keepAwakeSuccess = PowerManagement.KeepAwakeManager.Arm(_state);
            if (keepAwakeSuccess)
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Keep-awake armed successfully");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: OnShown: System sleep/hibernation now BLOCKED (IsArmed={PowerManagement.KeepAwakeManager.IsArmed})");
            }
            else
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: FATAL - FAILED to arm keep-awake (SetThreadExecutionState failed)");
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: FATAL - System sleep/hibernation NOT blocked (keep-awake did not activate)");
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: FATAL - App purpose is defeated, cannot continue");

                // Fatal error: app cannot fulfill its purpose
                FatalHelper.Fatal(
                    "FATAL ERROR: Failed to prevent system sleep.\n\n" +
                    "The System Win32 SetThreadExecutionState API call failed.\n\n" +
                    "Possible causes:\n" +
                    "- System policy prevents sleep blocking\n" +
                    "- Windows API compatibility issue\n\n" +
                    "Check trace log for details.\n\n" +
                    "The application cannot continue without keep-awake functionality.",
                    exitCode: 10);
                // Unreachable: FatalHelper.Fatal exits process
            }

            // =====================================================================
            // Arm timers: Auto-quit and countdown display (Module C)
            // =====================================================================
            // Only arm timers if we're in a timed mode (ForDuration or UntilTimestamp)
            if (_state.Mode != PlannedMode.Indefinite)
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Timed mode detected, arming timers...");
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Mode=" + _state.Mode);

                // Calculate target epoch (two-stage ceiling - Stage 2)
                double targetEpoch = 0;
                bool hasValidTarget = false;

                if (_state.Mode == PlannedMode.ForDuration && _state.PlannedTotal.HasValue)
                {
                    // --for: Calculate target from duration
                    // double nowCeil = Math.Ceiling(Time.TimezoneHelpers.GetCurrentEpochSeconds());
                    // double durationSeconds = _state.PlannedTotal.Value.TotalSeconds;
                    // targetEpoch = nowCeil + durationSeconds;
                    // Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: --for mode: nowCeil=" + nowCeil.ToString("F1") + ", duration=" + durationSeconds.ToString("F1") + "s, targetEpoch=" + targetEpoch.ToString("F1"));
                    // CORRECTED CODE:
                    double now2 = Time.TimezoneHelpers.GetCurrentEpochSeconds();
                    double durationSeconds = _state.PlannedTotal.Value.TotalSeconds;
                    targetEpoch = now2 + durationSeconds;
                    hasValidTarget = true;
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: --for mode: now=" + now2.ToString("F1") + ", duration=" + durationSeconds.ToString("F1") + "s, targetEpoch=" + targetEpoch.ToString("F1"));
                }
                else if (_state.Mode == PlannedMode.UntilTimestamp && _state.Options.UntilTargetEpoch.HasValue)
                {
                    // --until: Use pre-calculated epoch from CLI parser (Stage 1)
                    targetEpoch = _state.Options.UntilTargetEpoch.Value;
                    hasValidTarget = true;
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: --until mode: targetEpoch=" + targetEpoch.ToString("F1") + " (from CLI Stage 1)");
                }
                else
                {
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: ERROR - Timed mode but no duration/epoch available, skipping timers");
                }

                // Only proceed if we have a valid target
                if (hasValidTarget)
                {
                    // Re-calculate seconds from NOW to target (accounts for startup overhead - Stage 2)
                    // double nowCeil2 = Math.Ceiling(Time.TimezoneHelpers.GetCurrentEpochSeconds());
                    // int finalSeconds = (int)Math.Ceiling(targetEpoch - nowCeil2);
                    // Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Two-stage ceiling Stage 2: nowCeil=" + nowCeil2.ToString("F1") + ", finalSeconds=" + finalSeconds + "s");
                    // CORRECTED CODE:
                    double now2 = Time.TimezoneHelpers.GetCurrentEpochSeconds();
                    int finalSeconds = (int)Math.Ceiling(targetEpoch - now2);
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Two-stage ceiling Stage 2: now=" + now2.ToString("F1") + ", finalSeconds=" + finalSeconds + "s");

                    if (finalSeconds <= 0)
                    {
                        // Target already passed, quit immediately
                        Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: WARNING - Target time already passed (finalSeconds=" + finalSeconds + "), quitting immediately");
                        QuitApplication("Timer.AlreadyExpired");
                        return;
                    }

                    // Set monotonic deadline for countdown calculations (immune to clock changes)
                    long monotonicNow = Stopwatch.GetTimestamp();
                    _autoQuitDeadlineTicks = monotonicNow + ((long)finalSeconds * Stopwatch.Frequency);
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Monotonic deadline set: nowTicks=" + monotonicNow + ", deadlineTicks=" + _autoQuitDeadlineTicks);

                    // Set wall-clock ETA for display (human-readable)
                    _autoQuitWallClockEta = DateTime.Now.AddSeconds(finalSeconds);
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Wall-clock ETA: " + _autoQuitWallClockEta.Value.ToString("yyyy-MM-dd HH:mm:ss"));

                    // Update "Auto-quit at" field (static, set once)
                    if (_fldUntil != null)
                    {
                        _fldUntil.Text = _autoQuitWallClockEta.Value.ToString("yyyy-MM-dd HH:mm:ss");
                        Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: _fldUntil updated: " + _fldUntil.Text);
                    }

                    // Arm auto-quit timer (one-shot, fires once at deadline)
                    int finalMilliseconds = finalSeconds * 1000;
                    _autoQuitTimer = new System.Threading.Timer(OnAutoQuitCallback, null, finalMilliseconds, System.Threading.Timeout.Infinite);
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Auto-quit timer armed: will fire in " + finalSeconds + "s (" + finalMilliseconds + "ms)");

                    // Arm countdown display timer (adaptive cadence, reschedules itself)
                    _countdownTimer = new System.Windows.Forms.Timer();
                    _countdownTimer.Tick += OnCountdownTick;

                    // Set initial interval and start
                    int initialIntervalMs = Time.CountdownPlanner.CalculateNextInterval(_autoQuitDeadlineTicks);
                    _countdownTimer.Interval = initialIntervalMs;
                    _countdownTimer.Start();

                    // Update fields immediately (don't wait for first tick)
                    UpdateCountdownFields();
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Countdown fields initialized immediately");

                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Countdown timer armed: initial interval=" + initialIntervalMs + "ms");
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Timers armed successfully");
                }
            }
            else
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Indefinite mode, no timers needed");
            }

            //------------------------------------
            //------------------------------------
            // FOR DEBUGGING: Trace the panel below the image, containing text and fields.
            // Make sure any suspended parents are resumed, then trace a full snapshot
            // DebugLayout.EnsureResumed(this, _belowPanel, _fieldsTable);
            // DebugLayout.TraceLayoutSnapshot(this, _picture, _belowPanel, _fieldsTable);
            // one-time z-order flip test if you suspect the image is covering controls
            // DebugLayout.FlipZOrderForTest(this, _picture!);
            //------------------------------------
            //------------------------------------

            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnShown: Exiting.");
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm_Load: Entered MainForm_Load ...");
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm_Load: Exiting MainForm_Load ...");
        }

        // Dispose tray in FormClosed (no duplicate Dispose override).
        private void MainForm_FormClosed(object? sender, FormClosedEventArgs e)
        {
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Entered MainForm_FormClosed ...");
            try { _tray?.Dispose(); _tray = null; }
            catch (Exception ex) { Trace.WriteLine("UI.MainForm: Tray dispose error: " + ex); }
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Exiting MainForm_FormClosed ...");
        }

        // =====================================================================
        // Unified Handlers: All restore/minimize/quit paths funnel through here
        // =====================================================================

        /// <summary>
        /// Restore main window from system tray.
        /// Called by: tray left-click, tray menu "Show Window", Show Window button (future).
        /// </summary>
        private void RestoreFromTray()
        {
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Entered RestoreFromTray ...");
            try
            {
                if (_isClosing)
                {
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: RestoreFromTray: Already closing; ignoring.");
                    return;
                }

                // Show and activate the window
                this.Show();
                this.WindowState = FormWindowState.Normal;
                this.Activate();
                this.BringToFront();

                // Hide tray icon (window is now visible)
                if (_tray != null)
                {
                    _tray.Hide();
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: RestoreFromTray: Tray icon hidden");
                }

                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: RestoreFromTray: Window restored and activated");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: RestoreFromTray FAILED: {ex.GetType().Name}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: RestoreFromTray error message: {ex.Message}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: RestoreFromTray stack trace: {ex.StackTrace}");
            }
            finally
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Exiting RestoreFromTray");
            }
        }

        /// <summary>
        /// Minimize main window to system tray (hide window, show tray icon).
        /// Called by: title bar minimize button, Minimize button (future).
        /// </summary>
        private void MinimizeToTray()
        {
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Entered MinimizeToTray ...");
            try
            {
                if (_isClosing)
                {
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: MinimizeToTray: Already closing; ignoring.");
                    return;
                }

                // Show tray icon first (so user knows where the window went)
                if (_tray != null)
                {
                    _tray.Show();
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: MinimizeToTray: Tray icon shown");
                }

                // Hide the window completely (cleaner than WindowState.Minimized)
                this.Hide();
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: MinimizeToTray: Window hidden");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: MinimizeToTray FAILED: {ex.GetType().Name}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: MinimizeToTray error message: {ex.Message}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: MinimizeToTray stack trace: {ex.StackTrace}");
            }
            finally
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Exiting MinimizeToTray");
            }
        }

        /// <summary>
        /// Unified quit/shutdown handler. Cleans up tray, stops timers (future), exits application.
        /// Called by: title bar close X, Alt+F4, tray Quit, Quit button (future), session end.
        /// </summary>
        /// <param name="source">Trace-friendly description of quit trigger source.</param>
        private void QuitApplication(string source)
        {
            Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: Entered QuitApplication (source={source}) ...");
            try
            {
                // Guard against recursive calls (e.g., Close() triggering FormClosing which calls this again)
                if (_isClosing)
                {
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: Already closing; ignoring recursive call.");
                    return;
                }

                _isClosing = true;
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: Guard flag set (_isClosing = true)");

                // =====================================================================
                // Dispose timers: Stop and cleanup timer resources (Module C)
                // =====================================================================
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: Disposing timers...");

                // Stop and dispose auto-quit timer
                if (_autoQuitTimer != null)
                {
                    try
                    {
                        _autoQuitTimer.Dispose();
                        Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: Auto-quit timer disposed");
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: Error disposing auto-quit timer: " + ex.Message);
                    }
                    finally
                    {
                        _autoQuitTimer = null;
                    }
                }
                // Stop and dispose countdown timer
                if (_countdownTimer != null)
                {
                    try
                    {
                        _countdownTimer.Stop();
                        _countdownTimer.Tick -= OnCountdownTick;
                        _countdownTimer.Dispose();
                        Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: Countdown timer disposed");
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: Error disposing countdown timer: " + ex.Message);
                    }
                    finally
                    {
                        _countdownTimer = null;
                    }
                }

                // =====================================================================
                // Disarm keep-awake: Restore normal power management
                // =====================================================================
                // Critical cleanup: Allow system sleep/hibernation again before app exits.
                // If this fails, the system will remain in keep-awake state even after app closes!
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: Disarming keep-awake ...");
                bool disarmSuccess = PowerManagement.KeepAwakeManager.Disarm();
                if (disarmSuccess)
                {
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: Keep-awake disarmed successfully");
                    Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: QuitApplication: System sleep/hibernation now ALLOWED (IsArmed={PowerManagement.KeepAwakeManager.IsArmed})");
                }
                else
                {
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: CRITICAL - FAILED to disarm keep-awake!");
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: CRITICAL - System may remain in keep-awake state after app closes!");
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: CRITICAL - User may need to manually check 'powercfg -requests' and reboot if necessary");
                    // Don't block quit on disarm failure - app must close
                    // But log it very loudly so user can investigate
                }

                // TODO (future iterations): Stop timers, etc.

                // Hide and dispose tray icon
                if (_tray != null)
                {
                    try
                    {
                        _tray.Hide();
                        _tray.Dispose();
                        _tray = null;
                        Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: Tray disposed");
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: QuitApplication: Tray disposal error: {ex.Message}");
                    }
                }

                // Close the form (will trigger FormClosing/FormClosed, but _isClosing guard prevents recursion)
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: Calling this.Close()");
                this.Close();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: QuitApplication FAILED: {ex.GetType().Name}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: QuitApplication error message: {ex.Message}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: QuitApplication stack trace: {ex.StackTrace}");

                // Last resort: force exit
                try
                {
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: QuitApplication: Forcing Application.Exit() as last resort");
                    Application.Exit();
                }
                catch { }
            }
            finally
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Exiting QuitApplication");
            }
        }

        /// <summary>
        /// Public wrapper for ShowHelpModal() so TrayManager can trigger help display.
        /// Called by: tray menu "Help" item.
        /// </summary>
        internal void ShowHelp()
        {
            // Invoke on UI thread if called from tray background thread
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(ShowHelpModal));
            }
            else
            {
                ShowHelpModal();
            }
        }

        // =====================================================================
        /// <summary>
        /// Intercepts window resize/minimize events.
        /// When user clicks title bar minimize button "_", redirect to MinimizeToTray() instead.
        /// </summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            // Only trace and act if actually minimizing (not just resizing)
            if (this.WindowState == FormWindowState.Minimized && !_isClosing)
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnResize: Minimize detected => MinimizeToTray");
                MinimizeToTray();
            }
        }

        /// <summary>
        /// Intercepts form closing events (title bar X, Alt+F4, programmatic Close()).
        /// Routes all close attempts through unified QuitApplication() handler.
        /// Guard flag (_isClosing) prevents infinite recursion.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: OnFormClosing: Entered (CloseReason={e.CloseReason})");

            // If we're already in the quit flow, allow default close behavior
            if (_isClosing)
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnFormClosing: Already closing; allowing default close");
                base.OnFormClosing(e);
                return;
            }

            // For user-initiated closes (X button, Alt+F4), route through unified handler
            if (e.CloseReason == CloseReason.UserClosing ||
                e.CloseReason == CloseReason.WindowsShutDown ||
                e.CloseReason == CloseReason.TaskManagerClosing ||
                e.CloseReason == CloseReason.FormOwnerClosing)
            {
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: OnFormClosing: User/system close => QuitApplication (CloseReason={e.CloseReason})");
                e.Cancel = true;  // Cancel the default close
                QuitApplication($"FormClosing:{e.CloseReason}");
                return;
            }

            // Other close reasons (application-initiated, etc.) - allow default
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnFormClosing: Non-user close; allowing default");
            base.OnFormClosing(e);
        }

        /// <summary>
        /// Handles Windows session ending events (shutdown, logoff, restart).
        /// Ensures clean shutdown via unified QuitApplication() handler.
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            const int WM_QUERYENDSESSION = 0x0011;
            const int WM_ENDSESSION = 0x0016;

            if (m.Msg == WM_QUERYENDSESSION || m.Msg == WM_ENDSESSION)
            {
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: WndProc: Session ending (Msg=0x{m.Msg:X4}) => QuitApplication");

                if (!_isClosing)
                {
                    QuitApplication($"SessionEnd:0x{m.Msg:X4}");
                }
                // Allow Windows to proceed with shutdown
                m.Result = (IntPtr)1;
                return;
            }

            base.WndProc(ref m);
        }
        // =====================================================================

        // Image/Icon Loading and Preparation Pipeline
        /// <summary>
        /// Full imaging pipeline with very verbose tracing:
        /// 1) Source selection (priority): CLI --icon -> embedded base64 -> EXE neighbor -> checkerboard fallback
        /// 2) Square by edge replication (no solid bars), then ensure even pixels (replicate right/bottom if odd)
        /// 3) Window bitmap: ≤512 stays as-is; >512 downscales to 512 (high quality)
        /// 4) Multi-size PNG ICO (16..256): apply to Form and Tray
        /// 5) Resize window client area to match final display bitmap (leaving future room optional)
        /// </summary>
        private void TryLoadPrepareAndApplyImageAndIcon()
        {
            Trace.WriteLine("UI.MainForm: Entered TryLoadPrepareAndApplyImageAndIcon ...");

            Bitmap? src = null;
            Bitmap? squared = null;
            Bitmap? display = null;
            Icon? multiIcon = null;
            MemoryStream? icoStream = null;

            try
            {
                // ------------------------------------------------------------
                // SOURCE PRIORITY (Spec v11):
                //   1) CLI --icon PATH
                //   2) Embedded base64 (if non-empty)
                //   3) File 'Smart_Stay_Awake_icon.*' next to EXE (supported: png/jpg/jpeg/bmp/gif/ico)
                //   4) Self-generated checkerboard “eye” fallback
                // ------------------------------------------------------------
                // Note: we validate extension for disk files against AppConfig.ALLOWED_ICON_EXTENSIONS
                // ------------------------------------------------------------

                // 1) CLI --icon PATH
                if (!string.IsNullOrWhiteSpace(_state.Options?.IconPath))
                {
                    string path = _state.Options.IconPath!;
                    string ext = Path.GetExtension(path) ?? string.Empty;
                    Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: Candidate source (1/CLI): {path} (ext='{ext}')");

                    if (!AppConfig.ALLOWED_ICON_EXTENSIONS.Contains(ext))
                        throw new InvalidOperationException(
                            $"Unsupported --icon extension '{ext}'. Allowed: {string.Join(" ", AppConfig.ALLOWED_ICON_EXTENSIONS)}");

                    src = ImageLoader.LoadBitmapFromPath(path);
                    Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: Using CLI image. Size={src.Width}x{src.Height}");
                }
                // 2) Embedded base64
                else if (Base64ImageLoader.HasEmbeddedImage())
                {
                    Trace.WriteLine("UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: Using embedded base64 image (2/embedded).");
                    src = Base64ImageLoader.LoadEmbeddedBitmap();
                    Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: Retrieved embedded image. Size={src.Width}x{src.Height}");
                }
                // 3) Next-to-EXE file (Assets optional)
                else
                {
                    Trace.WriteLine("UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: Checking EXE-neighbor image (3/next-to-EXE).");
                    string exeDir = AppContext.BaseDirectory;

                    // Prefer next-to-EXE (root) first, then ./Assets as a courtesy
                    var probeList = new List<string>();
                    foreach (var ext in AppConfig.ALLOWED_ICON_EXTENSIONS)
                        probeList.Add(Path.Combine(exeDir, $"Smart_Stay_Awake_icon{ext}"));
                    foreach (var ext in AppConfig.ALLOWED_ICON_EXTENSIONS)
                        probeList.Add(Path.Combine(exeDir, "Assets", $"Smart_Stay_Awake_icon{ext}"));

                    string? found = probeList.FirstOrDefault(File.Exists);

                    // ??????????????????????????????????????????????????????????????????????
                    // for testing only: disable EXE-neighbour to force FALLBACK
                    //
                    //found = null; // TEMP: disable EXE-neighbor for now
                    //
                    // ??????????????????????????????????????????????????????????????????????

                    if (found != null)
                    {
                        string ext = Path.GetExtension(found) ?? string.Empty;
                        Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: Found EXE-neighbor: {found} (ext='{ext}')");
                        if (!AppConfig.ALLOWED_ICON_EXTENSIONS.Contains(ext))
                            throw new InvalidOperationException(
                                $"Neighbor image extension '{ext}' not allowed. Allowed: {string.Join(" ", AppConfig.ALLOWED_ICON_EXTENSIONS)}");
                        src = ImageLoader.LoadBitmapFromPath(found);
                        Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: Using EXE-neighbor image. Size={src.Width}x{src.Height}");
                    }
                    else
                    {
                        // 4) Self-generated checkerboard “eye” fallback
                        Trace.WriteLine("UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: No disk/embedded image found; using final fallback synthetic image (4/checkerboard).");
                        src = FallbackImageFactory.CreateEyeOfHorusBitmap(AppConfig.WINDOW_MAX_IMAGE_EDGE_PX);
                        Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: Retrieved final fallback synthetic image. Size={src.Width}x{src.Height}");
                    }
                }

                // =========================================================================
                // PIPELINE SPLIT: Window display vs. Tray icon (different requirements)
                // =========================================================================
                // Window: Show original aspect ratio (no squaring), resize if needed
                // Tray:   Square + even dimensions for proper icon rendering
                // Separation of concerns: /Imaging classes handle pixel manipulation

                // --------- Window display pipeline (preserve aspect ratio) ---------------
                Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: [WINDOW] Source image: {src.Width}x{src.Height}");

                int targetEdge = AppConfig.WINDOW_MAX_IMAGE_EDGE_PX;
                int srcMaxEdge = Math.Max(src.Width, src.Height);

                // Resize only if source exceeds target; otherwise use as-is
                if (srcMaxEdge > targetEdge)
                {
                    // Calculate scale to fit within max edge while preserving aspect ratio
                    float scale = (float)targetEdge / srcMaxEdge;
                    int newWidth = Math.Max(1, (int)Math.Round(src.Width * scale));
                    int newHeight = Math.Max(1, (int)Math.Round(src.Height * scale));

                    Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: [WINDOW] Resizing from {src.Width}x{src.Height} to {newWidth}x{newHeight} (scale={scale:F3})");

                    display = new Bitmap(newWidth, newHeight);
                    using (var g = Graphics.FromImage(display))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.DrawImage(src, 0, 0, newWidth, newHeight);
                    }
                }
                else
                {
                    // Use original size (already within limits)
                    Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: [WINDOW] Using original size (within {targetEdge}px limit)");
                    display = new Bitmap(src);
                }

                Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: [WINDOW] Final display size: {display.Width}x{display.Height}");

                // Apply to PictureBox
                if (_picture != null)
                {
                    var old = _picture.Image;
                    _picture.Image = new Bitmap(display);  // Clone for PictureBox
                    old?.Dispose();
                    Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: [WINDOW] PictureBox.Image set");
                }

                // Adjust window client size to fit the display image
                this.ClientSize = new Size(display.Width, display.Height);
                Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: [WINDOW] ClientSize set to {this.ClientSize.Width}x{this.ClientSize.Height}");

                // --------- Tray icon pipeline (square + even dimensions) ----------------
                // Note: ImageSquareReplicator.MakeSquareByEdgeReplication() guarantees even-dimensioned output,
                // so no manual evenization is needed here (proper separation of concerns)
                Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: [TRAY] Starting icon pipeline from original source");

                // Square by edge replication (no distortion of subject)
                Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: [TRAY] Source BEFORE SQUARE: {src.Width}x{src.Height}");
                squared = ImageSquareReplicator.MakeSquareByEdgeReplication(src);
                Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: [TRAY] Result AFTER SQUARE (guaranteed even): {squared.Width}x{squared.Height}");

                // --------- Build multi-size ICO from squared image ----------------------
                Trace.WriteLine($"UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: [TRAY] Building multi-size ICO from {squared.Width}x{squared.Height} square");
                var (icon, stream) = IcoBuilder.BuildMultiSizePngIco(squared, AppConfig.TRAY_ICON_SIZES);
                multiIcon = icon;
                icoStream = stream;

                // Apply to Form (title bar / taskbar)
                this.Icon = multiIcon;

                // Apply to Tray
                _tray?.SetIcon(multiIcon, icoStream);

                Trace.WriteLine("UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: [TRAY] Multi-size ICO applied to Form and Tray");

                // --------- Cleanup: Dispose intermediates (keep only what's in use) -----
                // 'src' is no longer needed (we made copies for display and icon)
                try { src?.Dispose(); src = null; } catch { }
                Trace.WriteLine("UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: Disposed source bitmap");

                // 'squared' is no longer needed (icon was built from it)
                try { squared?.Dispose(); squared = null; } catch { }
                Trace.WriteLine("UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: Disposed squared bitmap");

                // 'display' is no longer needed (we cloned it into PictureBox)
                try { display?.Dispose(); display = null; } catch { }
                Trace.WriteLine("UI.MainForm: TryLoadPrepareAndApplyImageAndIcon: Disposed display bitmap");


                // multiIcon and icoStream are held by TrayManager; will be disposed on app exit
                Trace.WriteLine("UI.MainForm: Exiting TryLoadPrepareAndApplyImageAndIcon (success).");
                // NOTE: Do NOT dispose multiIcon or icoStream here; TrayManager holds refs.
                // The Form will also use this.Icon. Dispose them on FormClosed via TrayManager.
            }
            catch (Exception ex)
            {
                Trace.WriteLine("UI.MainForm: TryLoadPrepareAndApplyImageAndIcon ERROR: " + ex);
                MessageBox.Show("Failed to load/prepare image/icon.\n" + ex.Message,
                    _state.AppDisplayName + " — Image Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                // If tray/icon not held yet, clean locals
                try { if (multiIcon != null && (_tray == null)) multiIcon.Dispose(); } catch { }
                try { if (icoStream != null && (_tray == null)) icoStream.Dispose(); } catch { }
            }
            finally
            {
                // We copied 'display' into PictureBox, safe to dispose local
                try { display?.Dispose(); } catch { }
                // 'squared' and 'src' are no longer needed
                try { squared?.Dispose(); } catch { }
                try { src?.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Builds the UI controls panel below the image and adjusts form size.
        /// Called once after image is loaded and displayed.
        /// Creates: primary text, separator, secondary text, buttons, dummy fields.
        /// 
        /// CRITICAL: Z-order matters for dock processing. WinForms docks controls in REVERSE z-order
        /// (back-to-front, highest index first). For Dock=Top followed by Dock=Fill to work correctly:
        ///   - Top-docked control must be at HIGHER index (back)
        ///   - Fill-docked control must be at index 0 (front)
        /// Otherwise Fill claims entire client area first, then Top paints over it (overlap).
        /// </summary>
        private void BuildBelowImageLayout()
        {
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Entered BuildBelowImageLayout ...");
            try
            {
                if (_picture == null)
                {
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout: PictureBox is null; cannot proceed");
                    return;
                }

                // =====================================================================
                // STEP 1: Calculate dimensions (no hardcoded magic numbers)
                // =====================================================================
                // Well-named inline constants for padding (single-use, don't pollute AppConfig)
                const int PANEL_PADDING_LEFT = 12;
                const int PANEL_PADDING_TOP = 10;
                const int PANEL_PADDING_RIGHT = 12;
                const int PANEL_PADDING_BOTTOM = 12;
                const int FORM_HEIGHT_SAFETY_MARGIN = 20;  // Extra pixels to prevent clipping at various DPI

                // Calculate content width: form's client area minus panel's horizontal padding
                // This ensures controls scale properly with form size and DPI scaling
                int formClientWidth = this.ClientSize.Width;
                int contentWidth = formClientWidth - PANEL_PADDING_LEFT - PANEL_PADDING_RIGHT;
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout: Width calculation: formClient={formClientWidth}, padding={PANEL_PADDING_LEFT}+{PANEL_PADDING_RIGHT}, contentWidth={contentWidth}");

                // =====================================================================
                // STEP 2: Convert image from Dock=Fill to Dock=Top with fixed height
                // =====================================================================
                int imageHeight = _picture.Height;
                _picture.Dock = DockStyle.Top;
                _picture.Height = imageHeight;
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout: Image converted to Dock=Top, Height={imageHeight}");

                // =====================================================================
                // STEP 3: Create controls container (FlowLayoutPanel with Dock=Fill)
                // =====================================================================
                var mainStack = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    AutoScroll = true,
                    Padding = new Padding(PANEL_PADDING_LEFT, PANEL_PADDING_TOP, PANEL_PADDING_RIGHT, PANEL_PADDING_BOTTOM),
                    BackColor = SystemColors.Control
                };
                this.Controls.Add(mainStack);
                _belowPanel = mainStack;  // Keep ref for debugging

                // =====================================================================
                // STEP 4: Fix z-order immediately (CRITICAL for correct dock processing)
                // =====================================================================
                // WinForms docks controls in REVERSE z-order (back-to-front, highest index first).
                // Current state after adding mainStack:
                //   [0] PictureBox (added in ctor) - FRONT
                //   [1] mainStack (just added) - BACK
                // This causes Fill to dock first (claims entire area), then Top paints over it.
                //
                // Correct order for Top-then-Fill layout:
                //   [0] mainStack (Dock=Fill) - FRONT - docked SECOND (gets remainder after Top)
                //   [1] PictureBox (Dock=Top) - BACK - docked FIRST (claims top portion)
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout: Z-order BEFORE fix: Picture={this.Controls.GetChildIndex(_picture)}, Panel={this.Controls.GetChildIndex(mainStack)}");

                this.Controls.SetChildIndex(_picture, 1);      // Move picture to back (higher index)
                this.Controls.SetChildIndex(mainStack, 0);     // Ensure panel is front (index 0)

                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout: Z-order AFTER fix: Picture={this.Controls.GetChildIndex(_picture)}, Panel={this.Controls.GetChildIndex(mainStack)}");
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout: Z-order corrected for proper dock processing (Top before Fill)");

                // =====================================================================
                // STEP 5: Build child controls (using calculated dimensions)
                // =====================================================================

                // =====================================================================
                // Text blurb section (matches Python APP_BLURB structure)
                // =====================================================================
                // Line 1: Title (centered, plain text - Python doesn't bold this)
                var lblTitle = new Label
                {
                    Text = "WEDJAT  :  THE EYE OF HORUS",
                    TextAlign = ContentAlignment.MiddleCenter,
                    AutoSize = false,
                    Width = contentWidth,
                    Height = 25,
                    Margin = new Padding(0, 6, 0, 0),
                    Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9.0f, FontStyle.Bold)
                };
                mainStack.Controls.Add(lblTitle);

                // Line 2: Empty spacing line (matches Python's "\n")
                var lblSpacer = new Label
                {
                    Text = "",
                    AutoSize = false,
                    Width = contentWidth,
                    Height = 10,
                    Margin = new Padding(0, 0, 0, 0)
                };
                mainStack.Controls.Add(lblSpacer);

                // Line 3: First description line (BOLD to match Python, taller to prevent clipping)
                var lblDesc1 = new Label
                {
                    Text = "Prevents system sleep and hibernation while active.",
                    TextAlign = ContentAlignment.MiddleCenter,
                    AutoSize = false,
                    Width = contentWidth,
                    Height = 24,  // Increased from 20 to 24 to accommodate descenders
                    Margin = new Padding(0, 0, 0, 2),  // Small gap between lines
                    Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9.0f, FontStyle.Bold)
                };
                mainStack.Controls.Add(lblDesc1);

                // Line 4: Second description line (BOLD to match Python, taller to prevent clipping)
                var lblDesc2 = new Label
                {
                    Text = "Display Monitor sleep is allowed.",
                    TextAlign = ContentAlignment.MiddleCenter,
                    AutoSize = false,
                    Width = contentWidth,
                    Height = 24,  // Increased from 20 to 24 to accommodate descenders
                    Margin = new Padding(0, 0, 0, 2),  // Small gap between lines
                    Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9.0f, FontStyle.Bold)
                };
                mainStack.Controls.Add(lblDesc2);

                // Line 5: Third description line (BOLD to match Python, taller height + larger bottom margin)
                var lblDesc3 = new Label
                {
                    Text = "Closing this app re-allows sleep && hibernation.",
                    TextAlign = ContentAlignment.MiddleCenter,
                    AutoSize = false,
                    Width = contentWidth,
                    Height = 24,  // Increased from 20 to 24 to accommodate descenders (prevents "p" clipping)
                    Margin = new Padding(0, 0, 0, 16),  // Extra space before separator
                    Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9.0f, FontStyle.Bold)
                };
                mainStack.Controls.Add(lblDesc3);

                // Separator line (matches Python's ttk.Separator, with spacing above and below)
                _separator = new Panel
                {
                    Width = contentWidth,
                    Height = 1,
                    Margin = new Padding(0, 12, 0, 12),  // ~1 line of space above and below
                    BackColor = SystemColors.ControlDark
                };
                mainStack.Controls.Add(_separator);

                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout: Text blurb (5 lines) and separator added");

                var lblStatusHint = new Label
                {
                    Text = "Click Help or Right-click System Tray icon for options.",
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = SystemColors.GrayText,
                    AutoSize = false,
                    Width = contentWidth,
                    Height = 24,  // Increased to prevent clipping
                    Margin = new Padding(0, 0, 0, 12),  // Space below before fields
                    Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9.0f, FontStyle.Regular)
                };
                mainStack.Controls.Add(lblStatusHint);
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout: Status hint label added");

                // Fields table (centered block, positioned after status hint)
                BuildFieldsTable();
                if (_fieldsTable != null)
                {
                    // Don't set width - let it auto-size, then center it
                    // FlowLayoutPanel doesn't center children by default, so wrap in a container
                    var fieldsContainer = new FlowLayoutPanel
                    {
                        Width = contentWidth,
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        FlowDirection = FlowDirection.LeftToRight,
                        WrapContents = false,
                        Margin = new Padding(0),
                        // Center the table horizontally
                        Padding = new Padding((contentWidth - _fieldsTable.PreferredSize.Width) / 2, 0, 0, 0)
                    };

                    // Force layout calculation
                    _fieldsTable.PerformLayout();

                    // Calculate centering padding
                    int centerPadding = Math.Max(0, (contentWidth - _fieldsTable.PreferredSize.Width) / 2);
                    fieldsContainer.Padding = new Padding(centerPadding, 0, 0, 0);

                    fieldsContainer.Controls.Add(_fieldsTable);
                    mainStack.Controls.Add(fieldsContainer);
                }

                // Buttons (positioned at bottom: left edge and right edge)
                BuildButtonsRow();
                if (_buttonsRow != null)
                {
                    // Find the inner Panel and set its width to match content width
                    if (_buttonsRow.Controls.Count > 0 && _buttonsRow.Controls[0] is Panel buttonPanel)
                    {
                        buttonPanel.Width = contentWidth;
                    }
                    mainStack.Controls.Add(_buttonsRow);
                }

                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout: Controls added to main stack");

                // =====================================================================
                // STEP 6: Calculate final form height based on actual control measurements
                // =====================================================================
                // Force layout engine to calculate PreferredSize based on actual children
                mainStack.PerformLayout();

                // Measure how much vertical space the bottom panel actually needs
                int bottomPanelHeight = mainStack.PreferredSize.Height;

                // Calculate total client height: image + bottom panel + safety margin
                // Safety margin prevents clipping at different DPI scales or with slight measurement variations
                int newClientHeight = imageHeight + bottomPanelHeight + FORM_HEIGHT_SAFETY_MARGIN;

                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout: Height calculation: image={imageHeight}, panel={bottomPanelHeight}, margin={FORM_HEIGHT_SAFETY_MARGIN}, total={newClientHeight}");

                this.ClientSize = new Size(this.ClientSize.Width, newClientHeight);
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout: Form resized to {this.ClientSize.Width}x{this.ClientSize.Height}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout FAILED: {ex.GetType().Name}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout error message: {ex.Message}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildBelowImageLayout stack trace: {ex.StackTrace}");
            }
            finally
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Exiting BuildBelowImageLayout");
            }
        }

        /// <summary>
        /// Builds the button row: Minimize to system tray (left), Quit (right edge).
        /// Matches Python program's button layout with bold text and proper positioning.
        /// </summary>
        private void BuildButtonsRow()
        {
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Entered BuildButtonsRow ...");
            try
            {
                // Left button: Minimize to system tray
                var btnMin = new Button
                {
                    Text = "Minimize to System Tray",
                    AutoSize = true,
                    Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9.0f, FontStyle.Bold),
                    Anchor = AnchorStyles.Left,  // Anchor to left
                    Margin = new Padding(0, 2, 4, 2)
                };
                btnMin.Click += (s, e) =>
                {
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Button 'Minimize to System Tray' clicked => MinimizeToTray");
                    MinimizeToTray();
                };

                // --- New Help Button ---
                var btnHelp = new Button
                {
                    Text = "Help",
                    AutoSize = true,
                    Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9.0f, FontStyle.Bold),
                    //Width = 100, // Matches standard button width in your app
                    //Height = 30,
                    Anchor = AnchorStyles.Right, // Right-justified like the Quit button
                    Margin = new Padding(4, 2, 4, 2)
                };
                // Wire it to use the existing modal help logic
                btnHelp.Click += (s, e) =>
                {
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Button 'Help' clicked => ShowHelpModal");
                    ShowHelpModal();
                };

                // Right button: Quit
                var btnQuit = new Button
                {
                    Text = "Quit",
                    AutoSize = true,
                    Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9.0f, FontStyle.Bold),
                    Anchor = AnchorStyles.Right,  // Anchor to right
                    Margin = new Padding(4, 2, 0, 2)
                };
                btnQuit.Click += (s, e) =>
                {
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Button 'Quit' clicked => QuitApplication");
                    QuitApplication("Button.Quit");
                };

                // Use a Panel with manual positioning (will be set in BuildBelowImageLayout)
                var buttonPanel = new Panel
                {
                    AutoSize = false,  // Fixed size (set by caller)
                    Height = 35,
                    Margin = new Padding(0, 12, 0, 0)
                };
                buttonPanel.Controls.Add(btnMin);
                buttonPanel.Controls.Add(btnHelp);  // Ensure Help is added BEFORE Quit to appear to its left in a right-anchored layout
                buttonPanel.Controls.Add(btnQuit);

                // Position buttons on resize/layout
                buttonPanel.Layout += (s, e) =>
                {
                    if (buttonPanel.Width > 0)
                    {
                        // 1. Position Minimize on the far left
                        btnMin.Location = new Point(0, 0);
                        // 2. Position Quit on the far right
                        int quitX = buttonPanel.Width - btnQuit.Width;
                        btnQuit.Location = new Point(quitX, 0);
                        // 3. Position Help to the left of Quit
                        // (8 pixel gap = btnHelp's right margin of 4 + btnQuit's left margin of 4)
                        int helpX = quitX - btnHelp.Width - 8;
                        btnHelp.Location = new Point(helpX, 0);
                    }
                };

                // Wrap for compatibility
                _buttonsRow = new FlowLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    FlowDirection = FlowDirection.TopDown,
                    Margin = new Padding(0)
                };
                _buttonsRow.Controls.Add(buttonPanel);

                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: BuildButtonsRow: 3 buttons added (manual positioning)");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildButtonsRow FAILED: {ex.GetType().Name}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildButtonsRow error message: {ex.Message}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildButtonsRow stack trace: {ex.StackTrace}");
            }
            finally
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Exiting BuildButtonsRow");
            }
        }

        /// <summary>
        /// Builds the fields table showing countdown/timer information (when active).
        /// Matches Python program's 2-column layout with dummy placeholder values.
        /// Fields: Auto-quit at, Time remaining, Timer update frequency.
        /// 
        /// Layout: Labels right-aligned (left column), Values left-aligned (right column),
        /// with spacing between columns to create "invisible center line" effect.
        /// 
        /// COMMENTED OUT (for future iterations):
        /// - Mode, Status, Icon Source, Version, DPI/Scale
        /// </summary>
        private void BuildFieldsTable()
        {
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Entered BuildFieldsTable ...");
            try
            {
                _fieldsTable = new TableLayoutPanel
                {
                    Dock = DockStyle.None,  // Changed from Fill to None for centering
                    Anchor = AnchorStyles.Top,  // Anchor to top, allow centering horizontally
                    ColumnCount = 2,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Margin = new Padding(0, 0, 0, 12)  // Space below before next element
                };
                _fieldsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));  // Left column: auto-size for labels
                _fieldsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));  // Right column: auto-size for values

                // Helper: create label pair (name + value) with proper alignment
                Label CreateNameLabel(string text) => new Label
                {
                    Text = text + ":",
                    AutoSize = true,
                    Anchor = AnchorStyles.Right,  // Use Anchor instead of TextAlign for auto-size labels
                    Margin = new Padding(0, 2, 12, 2),  // 12px gap between label and value
                    Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9.0f, FontStyle.Bold)
                };
                Label CreateValueLabel(string text) => new Label
                {
                    Text = text,
                    AutoSize = true,
                    Margin = new Padding(0, 2, 0, 2),
                    TextAlign = ContentAlignment.MiddleLeft,  // LEFT-aligned (unchanged)
                    Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 9.0f, FontStyle.Bold)
                };

                // Populate field values with dummy data matching Python's format
                // _fldUntil = CreateValueLabel("2025-12-31 23:59:59");
                // _fldRemaining = CreateValueLabel("0d 01:30:45");
                // _fldCadence = CreateValueLabel("00:00:10");
                _fldUntil = CreateValueLabel("0000-00-00 00:00:00");
                _fldRemaining = CreateValueLabel("0d 00:00:00");
                _fldCadence = CreateValueLabel("00:00:00");

                // Add rows to table (only active fields)
                int row = 0;
                void AddRow(string name, Label valueLabel)
                {
                    _fieldsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    _fieldsTable.Controls.Add(CreateNameLabel(name), 0, row);
                    _fieldsTable.Controls.Add(valueLabel, 1, row);
                    row++;
                }

                // Python field order and exact label text
                AddRow("Auto-quit at", _fldUntil);
                AddRow("Time remaining", _fldRemaining);
                AddRow("Time remaining update frequency", _fldCadence);
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: BuildFieldsTable: 3 field rows added (right/left aligned)");

                // =====================================================================
                // Conditional visibility: Hide countdown fields when no timer is active
                // =====================================================================
                // Per spec: When no --for and no --until, hide countdown fields (they're irrelevant).
                // Mode == Indefinite means keep-awake runs forever with no auto-quit timer.
                if (_state.Mode == PlannedMode.Indefinite)
                {
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: BuildFieldsTable: Mode is Indefinite (no timer)");
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: BuildFieldsTable: Hiding entire fields table (countdown info not applicable)");

                    // Hide the entire table (cleaner than hiding individual rows)
                    _fieldsTable.Visible = false;
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: BuildFieldsTable: Fields table hidden (_fieldsTable.Visible=false)");
                }
                else
                {
                    Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildFieldsTable: Mode is {_state.Mode} (timer active)");
                    Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: BuildFieldsTable: Fields table remains visible (countdown info relevant)");
                    Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildFieldsTable: Timer details: Until={_state.PlannedUntilLocal?.ToString("yyyy-MM-dd HH:mm:ss") ?? "<none>"}, Duration={_state.PlannedTotal?.ToString() ?? "<none>"}");
                    // Fields stay visible with placeholder values (will be updated by timer in future iteration)
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildFieldsTable FAILED: {ex.GetType().Name}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildFieldsTable error message: {ex.Message}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: BuildFieldsTable stack trace: {ex.StackTrace}");
            }
            finally
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Exiting BuildFieldsTable");
            }
        }
        /// <summary>
        /// Determines friendly text for Icon Source field based on what was actually used.
        /// </summary>
        private string DetermineIconSourceText()
        {
            if (!string.IsNullOrWhiteSpace(_state.Options?.IconPath))
                return $"CLI (--icon {Path.GetFileName(_state.Options.IconPath)})";

            if (Base64ImageLoader.HasEmbeddedImage())
                return "Embedded (base64)";

            // Check if neighbor file was used
            string exeDir = AppContext.BaseDirectory;
            foreach (var ext in AppConfig.ALLOWED_ICON_EXTENSIONS)
            {
                string path = Path.Combine(exeDir, $"Smart_Stay_Awake_icon{ext}");
                if (File.Exists(path))
                    return $"Neighbor ({Path.GetFileName(path)})";
            }

            return "Fallback (synthetic)";
        }

        /// <summary>
        /// Shows help content in a modal dialog.
        /// Reuses the same help text as CLI --help (from HelpTextBuilder).
        /// Unlike CLI --help, this does NOT exit the application.
        /// </summary>
        private void ShowHelpModal()
        {
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Entered ShowHelpModal ...");
            try
            {
                string helpText = HelpTextBuilder.BuildHelpText();

                int help_size_x = 1200;
                int help_size_y = 750;
                int help_size_x_min = (int) (help_size_x * 0.9);
                int help_size_y_min = (int) (help_size_y * 0.9);
                using var dlg = new Form
                {
                    Text = _state.AppDisplayName + " — Help",
                    StartPosition = FormStartPosition.CenterScreen,  // Changed from CenterParent (parent may be hidden)
                    Size = new Size(help_size_x, help_size_y),
                    MinimumSize = new Size(help_size_x_min, help_size_y_min),  // Add minimum size constraint
                    MinimizeBox = false,
                    MaximizeBox = true,  // Changed to true - allow user to maximize if needed
                    FormBorderStyle = FormBorderStyle.Sizable,
                    ShowInTaskbar = true  // Show in taskbar since parent may be hidden
                };

                var tb = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Both,
                    Dock = DockStyle.Fill,
                    WordWrap = false,
                    Font = new Font(FontFamily.GenericMonospace, 9.0f),
                    Text = helpText
                };

                dlg.Controls.Add(tb);
                // Deselect text after form is shown (prevents "highlighted" appearance)
                dlg.Shown += (s, e) =>
                {
                    tb.SelectionStart = 0;
                    tb.SelectionLength = 0;
                };

                // Ensure size is set after controls are added
                dlg.ClientSize = new Size(help_size_x, help_size_y);

                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: ShowHelpModal: Displaying modal help dialog");
                dlg.ShowDialog(this);
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: ShowHelpModal: Help dialog closed");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: ShowHelpModal FAILED: {ex.GetType().Name}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: ShowHelpModal error message: {ex.Message}");
                Trace.WriteLine($"Smart_Stay_Awake: UI.MainForm: ShowHelpModal stack trace: {ex.StackTrace}");

                MessageBox.Show(this, "Help is unavailable.\n" + ex.Message,
                    _state.AppDisplayName + " — Help Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: Exiting ShowHelpModal");
            }
        }

        /// <summary>
        /// Ensures the square image has even width/height by replicating the
        /// rightmost column and bottommost row by 1px if needed.
        /// </summary>
        private static Bitmap AddRightAndBottomReplication(Bitmap srcSquare)
        {
            Trace.WriteLine("UI.MainForm: Entered AddRightAndBottomReplication ...");
            if (srcSquare == null) throw new ArgumentNullException(nameof(srcSquare));
            if (srcSquare.Width != srcSquare.Height)
                throw new ArgumentException("UI.MainForm: AddRightAndBottomReplication expects a square image.");

            int w = srcSquare.Width;
            int h = srcSquare.Height;
            int newW = (w % 2 == 0) ? w : (w + 1);
            int newH = (h % 2 == 0) ? h : (h + 1);

            if (newW == w && newH == h)
            {
                Trace.WriteLine("UI.MainForm: AddRightAndBottomReplication: Even size already; returning clone.");
                return new Bitmap(srcSquare);
            }

            var dst = new Bitmap(newW, newH);
            using (var g = Graphics.FromImage(dst))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(srcSquare, 0, 0, w, h);
            }

            // If width grew by 1, copy the last original column into the new rightmost column.
            if (newW > w)
            {
                int srcX = w - 1;
                int dstX = newW - 1;
                for (int y = 0; y < h; y++)
                {
                    Color c = dst.GetPixel(srcX, y);
                    dst.SetPixel(dstX, y, c);
                }
            }

            // If height grew by 1, copy the last original row into the new bottom row.
            if (newH > h)
            {
                int srcY = h - 1;
                int dstY = newH - 1;
                for (int x = 0; x < newW; x++)
                {
                    // If we also added a column, make sure to read from the last
                    // valid column (w-1) for x >= w.
                    int sx = Math.Min(x, w - 1);
                    Color c = dst.GetPixel(sx, srcY);
                    dst.SetPixel(x, dstY, c);
                }
            }

            Trace.WriteLine($"UI.MainForm: AddRightAndBottomReplication -> Widht x Height {dst.Width}x{dst.Height}");
            return dst;
        }

        /// <summary>
        /// Resize the form’s client area to match the bitmap exactly (for now).
        /// You can add extra reserved height here later for labels/buttons.
        /// </summary>
        private void ResizeClientToBitmap(Bitmap bmp)
        {
            if (bmp == null) return;
            Trace.WriteLine("UI.MainForm: Entered  ResizeClientToBitmap ...");
            Trace.WriteLine($"UI.MainForm: ResizeClientToBitmap: target client={bmp.Width}x{bmp.Height}");

            // Current non-client borders
            int ncW = this.Width - this.ClientSize.Width;
            int ncH = this.Height - this.ClientSize.Height;

            // Requested client area: image width/height
            int targetW = bmp.Width;
            int targetH = bmp.Height;

            // Apply
            this.Size = new Size(targetW + ncW, targetH + ncH);
            Trace.WriteLine($"UI.MainForm: ResizeClientToBitmap: New Window Size={this.Width}x{this.Height} (Client={this.ClientSize.Width}x{this.ClientSize.Height})");
            Trace.WriteLine("UI.MainForm: Exiting ResizeClientToBitmap");
        }

        private void MainForm_Load_1(object sender, EventArgs e)
        {
        }

        // =====================================================================
        // Timer Callbacks (Module C - Auto-quit and countdown display)
        // =====================================================================

        /// <summary>
        /// Auto-quit timer callback - fires once when timer expires (runs on ThreadPool thread).
        /// Marshals to UI thread to call QuitApplication() safely.
        /// </summary>
        /// <param name="state">Timer state (unused)</param>
        private void OnAutoQuitCallback(object? state)
        {
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnAutoQuitCallback: Auto-quit timer expired, quitting application...");

            // Marshal to UI thread (callback runs on ThreadPool thread)
            if (this.InvokeRequired)
            {
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnAutoQuitCallback: Marshaling to UI thread via Invoke()");
                this.Invoke(new Action(() => QuitApplication("Timer.AutoQuit")));
            }
            else
            {
                // Already on UI thread (shouldn't happen, but defensive)
                Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: OnAutoQuitCallback: Already on UI thread (unexpected)");
                QuitApplication("Timer.AutoQuit");
            }
        }

        /// <summary>
        /// Countdown display timer tick - fires at adaptive intervals (runs on UI thread).
        /// Updates countdown fields if window visible, recalculates next interval, reschedules timer.
        /// </summary>
        /// <param name="sender">Timer object</param>
        /// <param name="e">Event args</param>
        private void OnCountdownTick(object? sender, EventArgs e)
        {
            // 1. Stop current timer
            _countdownTimer?.Stop();

#if DEBUG
            // Performance metrics (debug builds only)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long memBefore = GC.GetTotalMemory(false);
#endif

            // 2. Do work (update fields if visible)
            if (this.Visible)
            {
                UpdateCountdownFields();
            }

            // 3. Calculate next interval
            int nextIntervalMs = Time.CountdownPlanner.CalculateNextInterval(_autoQuitDeadlineTicks);

            // 4. Restart with new interval
            if (_countdownTimer != null)
            {
                _countdownTimer.Interval = nextIntervalMs;
                _countdownTimer.Start();
            }

#if DEBUG
            // Log performance metrics
            sw.Stop();
            long memAfter = GC.GetTotalMemory(false);
            int threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            string localTime = DateTime.Now.ToString("HH:mm:ss.fff");
            Trace.WriteLine("Smart_Stay_Awake: UI.MainForm: [PERF] Tick: " + sw.ElapsedTicks + " ticks (" + sw.Elapsed.TotalMilliseconds.ToString("F3") + "ms), " +
                            "Thread: " + threadId + ", Mem: " + (memAfter - memBefore) + " bytes, Visible: " + this.Visible + ", Time: " + localTime);
#endif
        }

        /// <summary>
        /// Update countdown display fields with current remaining time and cadence.
        /// Updates _fldRemaining every tick, _fldCadence only when value changes (low-churn).
        /// </summary>
        private void UpdateCountdownFields()
        {
            // Calculate remaining time (monotonic clock)
            long nowTicks = Stopwatch.GetTimestamp();
            long remainingTicks = Math.Max(0, _autoQuitDeadlineTicks - nowTicks);
            int remainingSeconds = (int)(remainingTicks / Stopwatch.Frequency);

            // Update "Time remaining" (every tick)
            if (_fldRemaining != null)
            {
                _fldRemaining.Text = Time.CountdownPlanner.FormatDHMS(remainingSeconds);
            }

            // Update "Timer update frequency" (only when changed)
            int baseCadenceMs = Time.CountdownPlanner.GetBaseCadenceMs(remainingSeconds);
            int cadenceSeconds = Math.Max(1, baseCadenceMs / 1000);

            if (cadenceSeconds != _lastCadenceSeconds)
            {
                if (_fldCadence != null)
                {
                    _fldCadence.Text = Time.CountdownPlanner.FormatHMS(cadenceSeconds);
                }
                _lastCadenceSeconds = cadenceSeconds;
            }
        }
    }
}
