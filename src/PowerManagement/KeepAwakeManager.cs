// File: src/Smart_Stay_Awake/PowerManagement/KeepAwakeManager.cs
// Purpose: High-level keep-awake manager with state tracking and strategy switching.
//          Provides clean interface for arming/disarming keep-awake from anywhere in the app.
//          Wraps low-level ExecutionState implementations with business logic and idempotency.
//          Switches between Legacy (thread-based) and Modern (Power Request) strategies.

using System;
using System.Diagnostics;

namespace Smart_Stay_Awake.PowerManagement
{
    /// <summary>
    /// High-level manager for keep-awake functionality.
    /// Tracks armed/disarmed state and provides idempotent Arm/Disarm methods.
    /// Thread-safe (uses lock), though threading is unnecessary for this tiny app.
    /// Singleton pattern: static class with global state.
    /// Strategy-based: switches between Legacy and Modern implementations via AppConfig.
    /// </summary>
    internal static class KeepAwakeManager
    {
        // State tracking (common to both strategies)
        private static bool _isArmed = false;
        private static readonly object _lock = new object();

        // Modern strategy: instance-based power request manager
        private static ExecutionStateModernPowerRequests? _modernInstance = null;

        // Strategy selected at startup (logged once for diagnostics)
        private static bool _strategyLogged = false;

        /// <summary>
        /// Gets whether keep-awake is currently armed.
        /// Thread-safe property (lock not needed for bool read, but included for consistency).
        /// </summary>
        public static bool IsArmed
        {
            get
            {
                lock (_lock)
                {
                    return _isArmed;
                }
            }
        }

        /// <summary>
        /// Arms keep-awake: prevents system sleep and hibernation.
        /// Idempotent: safe to call multiple times (no-op if already armed).
        /// Logs state changes verbosely.
        /// Strategy: uses AppConfig.POWER_STRATEGY to select Legacy or Modern implementation.
        /// </summary>
        /// <returns>True if armed successfully (or already armed), false if Win32 call failed.</returns>
        public static bool Arm(AppState appState)
        {
            Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Entered Arm ...");

            lock (_lock)
            {
                // Log strategy once on first use
                if (!_strategyLogged)
                {
                    Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Power management strategy: {AppConfig.POWER_STRATEGY}");
                    _strategyLogged = true;
                }

                // Idempotency check: already armed?
                if (_isArmed)
                {
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Arm: Already armed (no-op)");
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Exiting Arm (already armed, returning true)");
                    return true;
                }

                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Arm: Not armed yet, dispatching to strategy implementation ...");

                bool success = false;

                // Strategy dispatch
                if (AppConfig.POWER_STRATEGY == PowerManagementStrategy.LegacyThreadExecutionState)
                {
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Arm: Using LEGACY strategy (SetThreadExecutionState)");
                    success = ExecutionStateLegacyThread.ArmKeepAwake();
                }
                else if (AppConfig.POWER_STRATEGY == PowerManagementStrategy.ModernPowerRequests)
                {
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Arm: Using MODERN strategy (Power Request APIs)");

                    // Create new instance if needed (first call or after dispose)
                    if (_modernInstance == null)
                    {
                        Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Arm: Creating new ExecutionStateModernPowerRequests instance ...");
                        _modernInstance = new ExecutionStateModernPowerRequests();
                    }

                    success = _modernInstance.ArmKeepAwake(appState);
                }
                else
                {
                    Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Arm: ERROR - Unknown strategy: {AppConfig.POWER_STRATEGY}");
                    success = false;
                }

                if (success)
                {
                    _isArmed = true;
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Arm: SUCCESS - Keep-awake is now ARMED");
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Arm: State changed: _isArmed=false -> true");
                }
                else
                {
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Arm: FAILED - Strategy implementation returned false");
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Arm: State unchanged: _isArmed=false");
                }

                Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Exiting Arm (success={success})");
                return success;
            }
        }

        /// <summary>
        /// Disarms keep-awake: allows system sleep and hibernation again.
        /// Idempotent: safe to call multiple times (no-op if already disarmed).
        /// Logs state changes verbosely.
        /// Always call this on app quit to restore normal power management.
        /// Strategy: uses AppConfig.POWER_STRATEGY to select Legacy or Modern implementation.
        /// </summary>
        /// <returns>True if disarmed successfully (or already disarmed), false if Win32 call failed.</returns>
        public static bool Disarm()
        {
            Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Entered Disarm ...");

            lock (_lock)
            {
                // Idempotency check: already disarmed?
                if (!_isArmed)
                {
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Disarm: Already disarmed (no-op)");
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Exiting Disarm (already disarmed, returning true)");
                    return true;
                }

                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Disarm: Currently armed, dispatching to strategy implementation ...");

                bool success = false;

                // Strategy dispatch
                if (AppConfig.POWER_STRATEGY == PowerManagementStrategy.LegacyThreadExecutionState)
                {
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Disarm: Using LEGACY strategy (SetThreadExecutionState)");
                    success = ExecutionStateLegacyThread.DisarmKeepAwake();
                }
                else if (AppConfig.POWER_STRATEGY == PowerManagementStrategy.ModernPowerRequests)
                {
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Disarm: Using MODERN strategy (Power Request APIs)");

                    if (_modernInstance != null)
                    {
                        success = _modernInstance.DisarmKeepAwake();

                        // Dispose and nullify instance to release resources
                        Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Disarm: Disposing ExecutionStateModernPowerRequests instance ...");
                        _modernInstance.Dispose();
                        _modernInstance = null;
                    }
                    else
                    {
                        Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Disarm: No modern instance exists (unexpected state, treating as success)");
                        success = true;
                    }
                }
                else
                {
                    Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Disarm: ERROR - Unknown strategy: {AppConfig.POWER_STRATEGY}");
                    success = false;
                }

                if (success)
                {
                    _isArmed = false;
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Disarm: SUCCESS - Keep-awake is now DISARMED");
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Disarm: State changed: _isArmed=true -> false");
                }
                else
                {
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Disarm: FAILED - Strategy implementation returned false");
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Disarm: State unchanged: _isArmed=true (CRITICAL: keep-awake still active!)");
                }

                Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.KeepAwakeManager: Exiting Disarm (success={success})");
                return success;
            }
        }

        /// <summary>
        /// Gets a human-readable status string for display/logging.
        /// Example: "Armed (Modern Power Requests)" or "Disarmed"
        /// </summary>
        /// <returns>Status string describing current state and active strategy.</returns>
        public static string GetStatusString()
        {
            lock (_lock)
            {
                string strategyName = AppConfig.POWER_STRATEGY switch
                {
                    PowerManagementStrategy.LegacyThreadExecutionState => "Legacy Thread",
                    PowerManagementStrategy.ModernPowerRequests => "Modern Power Requests",
                    _ => "Unknown Strategy"
                };

                return _isArmed
                    ? $"Armed ({strategyName})"
                    : "Disarmed";
            }
        }
    }
}