// ============================================================================
// Drop-in UI layout diagnostics for Smart_Stay_Awake (no external deps)
// Place in UI/DebugLayout.cs.
// Call from MainForm at end of OnShown() and/or after you build your fields.
// ============================================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Smart_Stay_Awake.UI
{
    internal static class DebugLayout
    {
        // --- Snapshot sizes after layout -------------------------------------
        // Call with your actual controls:
        // DebugLayout.TraceLayoutSnapshot(this, pictureBox, bottomPanel, fieldsTable);
        public static void TraceLayoutSnapshot(Form form, Control? pictureBox, Control? bottomPanel, Control? fieldsTable)
        {
            try
            {
                form.SuspendLayout(); // small guard; we'll resume at the end
                Trace.WriteLine("[DebugLayout]===== [Layout] Snapshot BEGIN =====");

                // Form basics
                Trace.WriteLine($"[DebugLayout][Form] Text='{form.Text}', ClientSize={form.ClientSize.Width}x{form.ClientSize.Height}, " +
                                $"AutoScaleDimensions={form.CurrentAutoScaleDimensions.Width:0.##}x{form.CurrentAutoScaleDimensions.Height:0.##}, " +
                                $"DeviceDpi={form.DeviceDpi}");

                // Picture / Image area
                if (pictureBox != null)
                {
                    Trace.WriteLine($"[DebugLayout][Image] Name='{pictureBox.Name}', Bounds={Rect(pictureBox.Bounds)}, " +
                                    $"Dock={pictureBox.Dock}, Visible={pictureBox.Visible}, ZIndex={GetZIndex(form, pictureBox)}");
                }
                else
                {
                    Trace.WriteLine("[DebugLayout][Image] (null) — please pass the image control");
                }

                // Bottom / Fields container panel
                if (bottomPanel != null)
                {
                    Trace.WriteLine($"[DebugLayout][BottomPanel] Name='{bottomPanel.Name}', Bounds={Rect(bottomPanel.Bounds)}, " +
                                    $"Dock={bottomPanel.Dock}, Visible={bottomPanel.Visible}, PreferredSize={Size2(bottomPanel.PreferredSize)}, " +
                                    $"ZIndex={GetZIndex(form, bottomPanel)}");
                }
                else
                {
                    Trace.WriteLine("[DebugLayout][BottomPanel] (null) — please pass the panel that should contain text/buttons/fields");
                }

                // Fields table/panel with dummy fields
                if (fieldsTable != null)
                {
                    // Ensure PreferredSize is up-to-date
                    fieldsTable.PerformLayout();
                    Trace.WriteLine($"[DebugLayout][FieldsTable] Name='{fieldsTable.Name}', Bounds={Rect(fieldsTable.Bounds)}, " +
                                    $"Dock={fieldsTable.Dock}, Visible={fieldsTable.Visible}, " +
                                    $"PreferredSize={Size2(fieldsTable.PreferredSize)}, ChildCount={fieldsTable.Controls.Count}");
                    // Dump 1st level child rows/items
                    int i = 0;
                    foreach (Control c in fieldsTable.Controls)
                    {
                        Trace.WriteLine($"  [DebugLayout][FieldsChild {i++}] '{c.Name}' Type={c.GetType().Name}, Bounds={Rect(c.Bounds)}, AutoSize={c.AutoSize}, Dock={c.Dock}, Visible={c.Visible}");
                    }
                }
                else
                {
                    Trace.WriteLine("[DebugLayout][FieldsTable] (null) — please pass the layout panel holding your labels/values");
                }

                // Docking expectations check (image Top, bottom Fill)
                if (pictureBox != null && bottomPanel != null)
                {
                    bool goodImage = (pictureBox.Dock == DockStyle.Top);
                    bool goodBottom = (bottomPanel.Dock == DockStyle.Fill);
                    Trace.WriteLine($"[DebugLayout][DockCheck] ImageTop={goodImage}, BottomFill={goodBottom}");
                }

                // Z-order snapshot (topmost has index 0 in Controls collection)
                TraceZOrder(form);

                Trace.WriteLine("[DebugLayout]===== [Layout] Snapshot END =====");
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[DebugLayout][Layout] Snapshot FAILED: " + ex);
            }
            finally
            {
                form.ResumeLayout(true);
                form.PerformLayout();
            }
        }

        // --- Verify z-order and optionally flip to test visibility -----------
        // If you suspect z-order issues: call FlipZOrderForTest(form, pictureBox) ONCE.
        // It flips the index to the opposite end and prints the new order, then flips back.
        public static void TraceZOrder(Form form)
        {
            try
            {
                var names = form.Controls.Cast<Control>()
                                         .Select((c, idx) => $"[{idx}] '{c.Name}' Type={c.GetType().Name} Dock={c.Dock} Visible={c.Visible}")
                                         .ToArray();
                Trace.WriteLine("[DebugLayout][ZOrder] Form.Controls (0 = front/top):");
                foreach (var s in names) Trace.WriteLine("  " + s);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[DebugLayout][ZOrder] FAILED: " + ex);
            }
        }

        public static void FlipZOrderForTest(Form form, Control c)
        {
            try
            {
                int oldIdx = form.Controls.GetChildIndex(c);
                int newIdx = (oldIdx == 0) ? form.Controls.Count - 1 : 0;
                form.Controls.SetChildIndex(c, newIdx);
                Trace.WriteLine($"[DebugLayout][ZOrder] Flipped '{c.Name}' from {oldIdx} -> {newIdx}");
                TraceZOrder(form);
                // Flip it back so we don’t change behavior permanently
                form.Controls.SetChildIndex(c, oldIdx);
                Trace.WriteLine($"[DebugLayout][ZOrder] Restored '{c.Name}' index to {oldIdx}");
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[DebugLayout][ZOrder] Flip FAILED: " + ex);
            }
        }

        // --- Suspend/Resume sanity (call after building nested panels) -------
        // Pass every parent you called SuspendLayout() on, so we ensure they’re resumed.
        // REPLACE the existing EnsureResumed(...) in DebugLayout.cs with this version
        public static void EnsureResumed(params Control?[] containers)
        {
            foreach (var c in containers)
            {
                if (c == null) continue;
                try
                {
                    c.ResumeLayout(true);
                    c.PerformLayout();
                    Trace.WriteLine($"[DebugLayout][Layout] Resume+Perform '{c.Name}' OK, PreferredSize={Size2(c.PreferredSize)}, Bounds={Rect(c.Bounds)}");
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[DebugLayout][Layout] Resume '{c?.Name}' FAILED: {ex}");
                }
            }
        }

        // --- Helpers ----------------------------------------------------------
        private static string Rect(Rectangle r) => $"{r.X},{r.Y},{r.Width}x{r.Height}";
        private static string Size2(Size s) => $"{s.Width}x{s.Height}";
        private static int GetZIndex(Form f, Control c)
        {
            try { return f.Controls.GetChildIndex(c); } catch { return -1; }
        }
    }
}
