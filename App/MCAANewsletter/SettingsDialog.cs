using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MCAANewsletter
{
    /// <summary>
    /// Where the newsletter folder is and what its files are called.
    ///
    /// Every box re-checks the whole setup as it is typed in, and Save stays
    /// greyed until the answer is usable. That live check is the point of the
    /// window: a settings screen that lets you save a folder that is not there
    /// has only moved the failure to a worse moment.
    /// </summary>
    public sealed class SettingsDialog : Form
    {
        static readonly Color OkInk = Ui.DeepGreen;
        static readonly Color WarnInk = ColorTranslator.FromHtml("#8A6D1F");
        static readonly Color BadInk = ColorTranslator.FromHtml("#A33A2A");

        readonly Settings _settings;

        readonly TextBox _root = new TextBox();
        readonly TextBox _template = new TextBox();
        readonly TextBox _drafts = new TextBox();
        readonly TextBox _published = new TextBox();
        readonly TextBox _master = new TextBox();
        readonly TextBox _pattern = new TextBox();
        readonly TextBox _suffix = new TextBox();

        readonly Label _reason = new Label();
        readonly Label _status = new Label();
        readonly Label _example = new Label();
        readonly Button _save = Ui.MakeButton("Save", true);

        /// <summary>
        /// Shows the window over <paramref name="owner"/> and returns the settings
        /// she saved, or null if she closed it without saving. The settings passed
        /// in are never modified — an edit that is cancelled has to leave the
        /// running app exactly as it was.
        /// </summary>
        public static Settings Prompt(IWin32Window owner, Settings current, string reason)
        {
            using (var dialog = new SettingsDialog(current.Clone(), reason))
            {
                // Opened before the main window exists on a first run, and there
                // is nothing to centre on then.
                if (owner == null) dialog.StartPosition = FormStartPosition.CenterScreen;

                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._settings : null;
            }
        }

        SettingsDialog(Settings settings, string reason)
        {
            _settings = settings;

            Text = "MCAA Newsletter — Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(640, 592);
            BackColor = Color.White;
            Font = Ui.Body;

            int y = 20;

            Add(new Label
            {
                Text = "Where the newsletter lives",
                Font = Ui.StepTitle,
                ForeColor = Ui.BodyInk,
                Bounds = new Rectangle(24, y, 500, 26)
            });

            Add(new PictureBox
            {
                Image = Ui.Logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Bounds = new Rectangle(552, 16, 64, 64)
            });
            y += 30;

            // Problem messages are written for a message box, where a path on its
            // own line reads well. Here they have to flow as a paragraph.
            _reason.Text = OneLine(reason) ??
                "The program looks in this folder for the master, keeps your working copies " +
                "in Drafts, and puts finished issues in Published.";
            _reason.Font = Ui.Body;
            _reason.ForeColor = reason == null ? Ui.MutedInk : BadInk;
            // Stops short of the logo, and taller than the text needs: the case
            // that matters is a long problem message, not the default blurb.
            _reason.Bounds = new Rectangle(24, y, 500, 58);
            Add(_reason);
            y += 68;

            // --- the folder itself -------------------------------------------
            Add(Heading("Newsletter folder", y));
            y += 26;

            _root.Font = Ui.Body;
            _root.Bounds = new Rectangle(24, y, 470, 26);
            _root.Text = _settings.Root;
            Add(_root);

            var browse = Ui.MakeButton("Browse…", false);
            browse.SetBounds(504, y - 4, 112, 30);
            browse.Click += (s, e) => Browse();
            Add(browse);
            y += 46;

            // --- folders inside it -------------------------------------------
            Add(Heading("The folders inside it", y));
            y += 28;

            _template.Text = _settings.TemplateFolder;
            _drafts.Text = _settings.DraftsFolder;
            _published.Text = _settings.PublishedFolder;
            // Hints are one line and must not wrap; keep them short enough to fit.
            y = Row("Master lives in", _template, y, "holds the master document");
            y = Row("Your copies in", _drafts, y, "where new copies are put");
            y = Row("Finished ones in", _published, y, "where finished issues go");

            // --- names ---------------------------------------------------------
            y += 12;
            Add(Heading("What the files are called", y));
            y += 28;

            _master.Text = _settings.MasterFileName;
            _pattern.Text = _settings.IssuePattern;
            _suffix.Text = _settings.DraftSuffix;
            y = Row("Master file", _master, y, "every issue starts from this");
            y = Row("Each issue", _pattern, y, "{year} and {month} are filled in");
            y = Row("Draft ending", _suffix, y, "added while you work on it");

            // --- live check ------------------------------------------------------
            y += 10;
            var rule = new Panel { Bounds = new Rectangle(24, y, 592, 1), BackColor = Ui.BorderGreen };
            Add(rule);
            y += 12;

            _status.Font = Ui.BodyBold;
            _status.Bounds = new Rectangle(24, y, 592, 38);
            Add(_status);
            y += 40;

            _example.Font = Ui.Body;
            _example.ForeColor = Ui.MutedInk;
            _example.Bounds = new Rectangle(24, y, 592, 20);
            Add(_example);
            y += 32;

            // --- buttons ----------------------------------------------------------
            var reset = Ui.MakeButton("Use the usual names", false);
            reset.SetBounds(24, y, 180, 34);
            reset.Click += (s, e) => ResetNames();
            Add(reset);

            var cancel = Ui.MakeButton("Cancel", false);
            cancel.SetBounds(390, y, 110, 34);
            cancel.DialogResult = DialogResult.Cancel;
            Add(cancel);

            _save.SetBounds(510, y, 106, 34);
            _save.DialogResult = DialogResult.OK;
            Add(_save);

            AcceptButton = _save;
            CancelButton = cancel;

            foreach (TextBox box in new[] { _root, _template, _drafts, _published, _master, _pattern, _suffix })
                box.TextChanged += (s, e) => Recheck();

            Recheck();
        }

        #region layout helpers

        void Add(Control c) => Controls.Add(c);

        static Label Heading(string text, int y) => new Label
        {
            Text = text,
            Font = Ui.BodyBold,
            ForeColor = Ui.BodyInk,
            Bounds = new Rectangle(24, y, 400, 22)
        };

        int Row(string label, TextBox box, int y, string hint)
        {
            Add(new Label
            {
                Text = label,
                Font = Ui.Body,
                ForeColor = Ui.BodyInk,
                TextAlign = ContentAlignment.MiddleRight,
                Bounds = new Rectangle(24, y, 118, 24)
            });

            box.Font = Ui.Body;
            box.Bounds = new Rectangle(150, y, 220, 24);
            Add(box);

            Add(new Label
            {
                Text = hint,
                Font = Ui.Body,
                ForeColor = Ui.MutedInk,
                Bounds = new Rectangle(384, y + 2, 232, 22)
            });

            return y + 30;
        }

        #endregion

        void Browse()
        {
            using (var picker = new FolderBrowserDialog())
            {
                picker.Description = "Choose the folder the newsletter lives in.";
                picker.ShowNewFolderButton = false;

                try { if (Directory.Exists(_root.Text)) picker.SelectedPath = _root.Text; }
                catch (ArgumentException) { /* whatever is typed is not a path; start at the default */ }

                if (picker.ShowDialog(this) == DialogResult.OK)
                    _root.Text = picker.SelectedPath;
            }
        }

        void ResetNames()
        {
            _template.Text = Settings.DefaultTemplateFolder;
            _drafts.Text = Settings.DefaultDraftsFolder;
            _published.Text = Settings.DefaultPublishedFolder;
            _master.Text = Settings.DefaultMasterFileName;
            _pattern.Text = Settings.DefaultIssuePattern;
            _suffix.Text = Settings.DefaultDraftSuffix;
        }

        /// <summary>
        /// Pulls the boxes into the settings and says what is wrong with them, on
        /// every keystroke. Save is only ever enabled on a setup that works.
        /// </summary>
        void Recheck()
        {
            _settings.Root = _root.Text.Trim();
            _settings.TemplateFolder = _template.Text;
            _settings.DraftsFolder = _drafts.Text;
            _settings.PublishedFolder = _published.Text;
            _settings.MasterFileName = _master.Text;
            _settings.IssuePattern = _pattern.Text;
            _settings.DraftSuffix = _suffix.Text;

            string problem = _settings.Problem();
            string warning = problem == null ? _settings.Warning() : null;

            if (problem != null)
            {
                _status.ForeColor = BadInk;
                _status.Text = "✗   " + OneLine(problem);
            }
            else if (warning != null)
            {
                _status.ForeColor = WarnInk;
                _status.Text = "!   " + warning + "  You can still save this.";
            }
            else
            {
                _status.ForeColor = OkInk;
                _status.Text = "✓   Found " + _settings.MasterFileName + " in the " +
                               _settings.TemplateFolder + " folder.";
            }

            // The reason is why the window opened, not what is wrong now. Once she
            // has fixed it, leaving it up contradicts the green tick underneath.
            if (problem == null) _reason.Visible = false;

            _save.Enabled = problem == null;
            _example.Text = "The next issue would be called:   " + Example();
        }

        /// <summary>
        /// The names are checked before the paths, so this can be built from the
        /// boxes even while the folder itself is wrong — which is exactly when
        /// seeing the result is most use.
        /// </summary>
        string Example()
        {
            try
            {
                DateTime next = DateTime.Today.AddMonths(1);
                return _settings.DraftsFolder + "\\" +
                       Settings.Expand(_settings.IssuePattern, next.Year, next.Month) +
                       _settings.DraftSuffix + ".docx";
            }
            catch (Exception) { return "—"; }
        }

        /// <summary>
        /// Problem messages are written for a message box, where a path on its own
        /// line reads well. In a two-line status strip it does not.
        /// </summary>
        static string OneLine(string text) => text == null ? null :
            string.Join(" ", text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
