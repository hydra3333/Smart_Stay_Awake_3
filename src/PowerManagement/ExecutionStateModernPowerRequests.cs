// File: src/Smart_Stay_Awake/PowerManagement/ExecutionStateModernPowerRequests.cs
// Purpose: Low-level Win32 interop wrapper for Power Request APIs (MODERN implementation).
//          Uses CsWin32-generated bindings from Windows SDK metadata.
//          Instance-based: maintains a SafeFileHandle to a power request object.
//          Process-level request: more robust than thread-level flags.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.System.Threading;

namespace Smart_Stay_Awake.PowerManagement
{
    /// <summary>
    /// MODERN: Low-level wrapper for Win32 Power Request APIs.
    /// Uses CsWin32-generated type-safe bindings from Windows SDK metadata.
    /// Instance-based: stores a SafeFileHandle to the power request and manages its lifecycle.
    /// Process-level request: more robust diagnostics and explicit lifecycle management.
    /// Implements IDisposable for proper cleanup.
    /// </summary>
    internal sealed class ExecutionStateModernPowerRequests : IDisposable
    {
        private SafeFileHandle? _powerRequestHandle;
        private bool _isArmed = false;
        private bool _disposed = false;

        /// <summary>
        /// Gets whether keep-awake is currently armed.
        /// </summary>
        public bool IsArmed => _isArmed;

        /// <summary>
        /// Arms keep-awake: prevents system sleep and hibernation.
        /// Creates a power request and sets PowerRequestSystemRequired.
        /// Call this once when user activates keep-awake.
        /// </summary>
        /// <returns>True if successful, false if Power Request API calls failed.</returns>
        public bool ArmKeepAwake(AppState appState)
        {
            Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: Entered ArmKeepAwake ...");

            if (_disposed)
            {
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: FAILED - Object already disposed");
                return false;
            }

            // Idempotency: already armed?
            if (_isArmed && _powerRequestHandle != null && !_powerRequestHandle.IsInvalid)
            {
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: Already armed (no-op)");
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: Exiting ArmKeepAwake (already armed, returning true)");
                return true;
            }

            Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: Not armed yet, creating power request ...");

            try
            {
                // Step 1: Prepare reason string and REASON_CONTEXT
                string reasonText = GenerateReasonString(appState);

                REASON_CONTEXT rc = new REASON_CONTEXT
                {
                    Version = 0,  // POWER_REQUEST_CONTEXT_VERSION constant = 0 (per Windows SDK)
                    Flags = POWER_REQUEST_CONTEXT_FLAGS.POWER_REQUEST_CONTEXT_SIMPLE_STRING
                };

                Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: REASON_CONTEXT prepared: \"{reasonText}\"");
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: Version=0, Flags=POWER_REQUEST_CONTEXT_SIMPLE_STRING");

                // Step 2: Create the power request (Pattern A: unsafe/fixed for PWSTR)
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: Calling PowerCreateRequest ...");

                unsafe
                {
                    fixed (char* p = reasonText)  // Pin managed string for the duration of the call
                    {
                        rc.Reason.SimpleReasonString = new PWSTR(p);
                        _powerRequestHandle = PInvoke.PowerCreateRequest(in rc);
                    } // String unpinned after call returns (safe - Windows copies it internally)
                }

                if (_powerRequestHandle == null || _powerRequestHandle.IsInvalid)
                {
                    int lastError = Marshal.GetLastWin32Error();
                    Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: FAILED - PowerCreateRequest returned invalid handle (Win32Error: {lastError})");
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: System sleep/hibernation NOT blocked (create failed)");
                    return false;
                }

                Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: SUCCESS - PowerCreateRequest returned valid SafeFileHandle");

                // Step 3: Set the power request type (block system sleep/hibernation only, allow display sleep)
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: Calling PowerSetRequest (PowerRequestSystemRequired) ...");

                bool setResult = PInvoke.PowerSetRequest(_powerRequestHandle, POWER_REQUEST_TYPE.PowerRequestSystemRequired);

                if (!setResult)
                {
                    int lastError = Marshal.GetLastWin32Error();
                    Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: FAILED - PowerSetRequest returned false (Win32Error: {lastError})");
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: Cleaning up handle and returning failure ...");

                    // Cleanup the handle since we failed to activate the request
                    _powerRequestHandle.Dispose();
                    _powerRequestHandle = null;
                    return false;
                }

                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: SUCCESS - PowerSetRequest succeeded");
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: System sleep/hibernation now BLOCKED (process-level power request active)");
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: Display sleep is ALLOWED (PowerRequestDisplayRequired NOT set)");

                _isArmed = true;
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: State changed: _isArmed=false -> true");
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: Exiting ArmKeepAwake (success=true)");
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: EXCEPTION - {ex.GetType().Name}: {ex.Message}");
                Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: ArmKeepAwake: Stack trace: {ex.StackTrace}");

                // Cleanup on exception
                if (_powerRequestHandle != null)
                {
                    try { _powerRequestHandle.Dispose(); } catch { }
                    _powerRequestHandle = null;
                }

                return false;
            }
        }

        /// <summary>
        /// Disarms keep-awake: allows system sleep and hibernation again.
        /// Clears the power request and disposes the SafeFileHandle.
        /// Call this when app quits or when user deactivates keep-awake.
        /// </summary>
        /// <returns>True if successful, false if Power Request API calls failed.</returns>
        public bool DisarmKeepAwake()
        {
            Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: Entered DisarmKeepAwake ...");

            if (_disposed)
            {
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: Object already disposed (no-op)");
                return true;
            }

            // Idempotency: already disarmed?
            if (!_isArmed && (_powerRequestHandle == null || _powerRequestHandle.IsInvalid))
            {
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: Already disarmed (no-op)");
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: Exiting DisarmKeepAwake (already disarmed, returning true)");
                return true;
            }

            Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: Currently armed, clearing power request ...");

            bool success = true;

            try
            {
                // Step 1: Clear the power request type
                if (_isArmed && _powerRequestHandle != null && !_powerRequestHandle.IsInvalid)
                {
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: Calling PowerClearRequest (PowerRequestSystemRequired) ...");

                    bool clearResult = PInvoke.PowerClearRequest(_powerRequestHandle, POWER_REQUEST_TYPE.PowerRequestSystemRequired);

                    if (!clearResult)
                    {
                        int lastError = Marshal.GetLastWin32Error();
                        Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: WARNING - PowerClearRequest returned false (Win32Error: {lastError})");
                        success = false;
                    }
                    else
                    {
                        Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: SUCCESS - PowerClearRequest succeeded");
                    }
                }

                // Step 2: Dispose the SafeFileHandle (calls CloseHandle internally, safe to call multiple times)
                if (_powerRequestHandle != null)
                {
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: Disposing SafeFileHandle (will call CloseHandle internally) ...");

                    try
                    {
                        _powerRequestHandle.Dispose();
                        Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: SUCCESS - SafeFileHandle disposed (handle closed)");
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: WARNING - SafeFileHandle.Dispose threw exception: {ex.Message}");
                        success = false;
                    }

                    _powerRequestHandle = null;
                }

                if (success)
                {
                    _isArmed = false;
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: Keep-awake is now DISARMED");
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: State changed: _isArmed=true -> false");
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: System sleep/hibernation now ALLOWED");
                }
                else
                {
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: PARTIAL FAILURE - Some cleanup operations failed");
                    Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: State: _isArmed unchanged (CRITICAL: cleanup incomplete!)");
                }

                Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: Exiting DisarmKeepAwake (success={success})");
                return success;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: EXCEPTION - {ex.GetType().Name}: {ex.Message}");
                Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: DisarmKeepAwake: Stack trace: {ex.StackTrace}");

                // Best-effort cleanup
                if (_powerRequestHandle != null)
                {
                    try { _powerRequestHandle.Dispose(); } catch { }
                }
                _powerRequestHandle = null;
                _isArmed = false;

                return false;
            }
        }

        /// <summary>
        /// Implements IDisposable pattern for proper cleanup.
        /// Ensures power request is cleared and SafeFileHandle is disposed.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: Dispose called - cleaning up ...");

            DisarmKeepAwake();

            _disposed = true;

            Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: Dispose completed");
        }

        /// <summary>
        /// Generates a descriptive reason string based on app configuration.
        /// Includes app name, base message, and auto-quit timing if applicable.
        /// </summary>
        private static string GenerateReasonString(AppState appState)
        {
            Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: Entered GenerateReasonString ...");

            const string baseMessage = "Preventing automatic sleep & hibernation (display monitor may sleep) as requested.";

            if (appState.Mode == PlannedMode.Indefinite)
            {
                string result = $"{appState.AppDisplayName}: {baseMessage} (indefinitely).";
                Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: GenerateReasonString: Mode=Indefinite, result={result}");
                Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: Exiting GenerateReasonString (indefinite)");
                return result;
            }

            // Countdown mode - calculate auto-quit time
            DateTime autoQuitTime = DateTime.Now;

            if (appState.Mode == PlannedMode.ForDuration && appState.PlannedTotal.HasValue)
            {
                autoQuitTime = autoQuitTime.Add(appState.PlannedTotal.Value);
            }
            else if (appState.Mode == PlannedMode.UntilTimestamp && appState.PlannedUntilLocal.HasValue)
            {
                autoQuitTime = appState.PlannedUntilLocal.Value;
            }

            string formattedTime = autoQuitTime.ToString("yyyy-MM-dd HH:mm:ss");
            string resultTimed = $"{appState.AppDisplayName}: {baseMessage} Auto-quit at {formattedTime}.";

            Trace.WriteLine($"Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: GenerateReasonString: Mode={appState.Mode}, autoQuitTime={formattedTime}, result={resultTimed}");
            Trace.WriteLine("Smart_Stay_Awake: PowerManagement.ExecutionStateModernPowerRequests: Exiting GenerateReasonString (timed)");
            return resultTimed;
        }
    }
}