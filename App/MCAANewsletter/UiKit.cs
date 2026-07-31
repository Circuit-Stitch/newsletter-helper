using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MCAANewsletter
{
    /// <summary>
    /// Shared look. The palette is the newsletter's own, sampled from the real
    /// issues and recorded in newsletter-design-spec.md, so the program looks like
    /// it belongs to the thing it produces.
    /// </summary>
    public static class Ui
    {
        public static readonly Color PaleGreen = ColorTranslator.FromHtml("#EBF1DE");
        public static readonly Color RuleGreen = ColorTranslator.FromHtml("#9BBB59");
        public static readonly Color DeepGreen = ColorTranslator.FromHtml("#76923C");
        public static readonly Color BorderGreen = ColorTranslator.FromHtml("#A6B880");
        public static readonly Color BodyInk = ColorTranslator.FromHtml("#363636");
        public static readonly Color MutedInk = ColorTranslator.FromHtml("#6B6B6B");

        // Type is a size up from Windows' default throughout. The person using
        // this is 70 and reading it on a laptop screen.
        public static readonly Font Title = new Font("Segoe UI", 17F, FontStyle.Regular);
        public static readonly Font StepTitle = new Font("Segoe UI", 12F, FontStyle.Bold);
        public static readonly Font Body = new Font("Segoe UI", 10F, FontStyle.Regular);
        public static readonly Font BodyBold = new Font("Segoe UI", 10F, FontStyle.Bold);
        public static readonly Font Glyph = new Font("Segoe UI", 15F, FontStyle.Bold);
        public static readonly Font ButtonFont = new Font("Segoe UI", 10F, FontStyle.Regular);

        public static Button MakeButton(string text, bool primary)
        {
            var b = new Button
            {
                Text = text,
                Font = ButtonFont,
                AutoSize = false,
                Height = 34,
                FlatStyle = FlatStyle.System,
                UseVisualStyleBackColor = true,
                Padding = new Padding(10, 0, 10, 0)
            };
            if (primary) b.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            return b;
        }
    }

    /// <summary>
    /// One numbered step: status glyph, title, a line of explanation, and at most
    /// one button. Exactly one step on the window is ever enabled.
    /// </summary>
    public sealed class StepPanel : Panel
    {
        readonly Label _glyph = new Label();
        readonly Label _title = new Label();
        readonly Label _detail = new Label();
        public Button Action { get; } = Ui.MakeButton("", true);

        public StepPanel(string title)
        {
            Height = 108;
            Dock = DockStyle.Top;
            Padding = new Padding(0, 10, 0, 10);
            BackColor = Color.Transparent;

            _glyph.SetBounds(14, 16, 34, 30);
            _glyph.Font = Ui.Glyph;
            _glyph.TextAlign = ContentAlignment.MiddleCenter;

            _title.SetBounds(50, 18, 460, 24);
            _title.Font = Ui.StepTitle;
            _title.Text = title;

            _detail.SetBounds(52, 44, 380, 46);
            _detail.Font = Ui.Body;
            _detail.ForeColor = Ui.MutedInk;

            Action.SetBounds(0, 0, 230, 34);
            Action.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            Controls.AddRange(new Control[] { _glyph, _title, _detail, Action });
            Resize += (s, e) => Action.Location = new Point(Width - Action.Width - 16, 46);
        }

        public string Title { get => _title.Text; set => _title.Text = value; }
        public string Detail { get => _detail.Text; set => _detail.Text = value; }

        public void Apply(StepStatus status, string buttonText, bool buttonVisible = true)
        {
            switch (status)
            {
                case StepStatus.Done:
                    _glyph.Text = "✓";                 // ✓
                    _glyph.ForeColor = Ui.DeepGreen;
                    _title.ForeColor = Ui.BodyInk;
                    Action.Enabled = true;
                    Action.Font = Ui.ButtonFont;
                    break;

                case StepStatus.Current:
                    _glyph.Text = "▶";                 // ▶
                    _glyph.ForeColor = Ui.DeepGreen;
                    _title.ForeColor = Ui.BodyInk;
                    Action.Enabled = true;
                    Action.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
                    break;

                default:
                    _glyph.Text = "○";                 // ○
                    _glyph.ForeColor = Color.Silver;
                    _title.ForeColor = Color.Gray;
                    Action.Enabled = false;
                    Action.Font = Ui.ButtonFont;
                    break;
            }

            Action.Text = buttonText;
            Action.Visible = buttonVisible;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Ui.BorderGreen))
                e.Graphics.DrawLine(pen, 12, Height - 1, Width - 12, Height - 1);
        }
    }

    /// <summary>
    /// Modal "working" window for the slow steps.
    ///
    /// The work runs on a dedicated STA thread rather than the thread pool: it
    /// drives Word through COM, and COM is apartment-affine. Doing it on the UI
    /// thread instead would freeze the window, which looks broken.
    /// </summary>
    public sealed class BusyDialog : Form
    {
        BusyDialog(string message)
        {
            Text = "MCAA Newsletter";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 116);
            BackColor = Color.White;

            Controls.Add(new Label
            {
                Text = message,
                Font = Ui.Body,
                ForeColor = Ui.BodyInk,
                Bounds = new Rectangle(24, 24, 372, 40)
            });

            Controls.Add(new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Bounds = new Rectangle(24, 72, 372, 18)
            });
        }

        /// <summary>
        /// Runs <paramref name="work"/> behind a modal wait window. Any exception
        /// it raises is rethrown on the calling thread once the window closes, so
        /// callers can handle failures exactly as if the call were synchronous.
        /// </summary>
        public static void Run(IWin32Window owner, string message, Action work)
        {
            Exception failure = null;

            using (var dialog = new BusyDialog(message))
            {
                dialog.Shown += (s, e) =>
                {
                    var thread = new Thread(() =>
                    {
                        try { work(); }
                        catch (Exception ex) { failure = ex; }
                        finally
                        {
                            try { dialog.BeginInvoke(new Action(dialog.Close)); }
                            catch (InvalidOperationException) { /* already gone */ }
                        }
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.IsBackground = true;
                    thread.Start();
                };

                dialog.ShowDialog(owner);
            }

            if (failure != null) throw failure;
        }
    }
}
