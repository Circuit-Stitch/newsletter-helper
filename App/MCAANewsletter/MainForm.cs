using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MCAANewsletter
{
    /// <summary>
    /// The whole program: one window, three steps, one live button at a time.
    /// </summary>
    public sealed class MainForm : Form
    {
        Settings _settings;
        readonly ComboBox _issuePicker = new ComboBox();
        readonly StepPanel _step1 = new StepPanel("1.  Start the newsletter");
        readonly StepPanel _step2 = new StepPanel("2.  Check the photos and make the PDF");
        readonly StepPanel _step3 = new StepPanel("3.  Publish");

        /// <summary>
        /// Mirrors the picker rather than asking it.
        ///
        /// The slow steps run their work on a worker thread, and reading
        /// ComboBox.SelectedItem there is a cross-thread control access — it
        /// throws under a debugger and is undefined without one. Keeping the
        /// answer in a plain field means the worker never touches a control.
        /// </summary>
        IssueName _issue;

        IssueName Issue => _issue;

        public MainForm(Settings settings)
        {
            _settings = settings;
            BuildLayout();
            PopulateIssues();
            RefreshState();

            // Re-read the folder whenever she comes back to the window; she may
            // have been in Word or Explorer in the meantime.
            Activated += (s, e) => RefreshState();
        }

        #region layout

        void BuildLayout()
        {
            Text = "MCAA Newsletter";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(680, 590);
            BackColor = Color.White;
            Font = Ui.Body;

            var banner = new Panel
            {
                Bounds = new Rectangle(0, 0, 680, 68),
                BackColor = Ui.RuleGreen
            };
            banner.Controls.Add(new Label
            {
                Text = "MCAA Newsletter",
                Font = Ui.Title,
                ForeColor = Color.Black,
                Bounds = new Rectangle(24, 18, 400, 34)
            });

            var issueLabel = new Label
            {
                Text = "Which newsletter?",
                Font = Ui.BodyBold,
                ForeColor = Ui.BodyInk,
                Bounds = new Rectangle(24, 92, 150, 24)
            };

            _issuePicker.DropDownStyle = ComboBoxStyle.DropDownList;
            _issuePicker.Font = new Font("Segoe UI", 11F, FontStyle.Regular);
            _issuePicker.Bounds = new Rectangle(180, 88, 220, 30);
            _issuePicker.SelectedIndexChanged += (s, e) =>
            {
                _issue = _issuePicker.SelectedItem as IssueName;
                RefreshState();
            };

            var steps = new Panel
            {
                Bounds = new Rectangle(24, 134, 632, 340),
                BackColor = Color.White
            };
            _step1.Dock = DockStyle.None; _step1.Bounds = new Rectangle(0, 0, 632, 108);
            _step2.Dock = DockStyle.None; _step2.Bounds = new Rectangle(0, 112, 632, 108);
            _step3.Dock = DockStyle.None; _step3.Bounds = new Rectangle(0, 224, 632, 108);
            steps.Controls.AddRange(new Control[] { _step1, _step2, _step3 });

            _step1.Action.Click += (s, e) => Guarded(Step1Clicked);
            _step2.Action.Click += (s, e) => Guarded(Step2Clicked);
            _step3.Action.Click += (s, e) => Guarded(Step3Clicked);

            var openDrafts = Ui.MakeButton("Open the Drafts folder", false);
            openDrafts.SetBounds(24, 500, 200, 36);
            openDrafts.Click += (s, e) => OpenFolder(Issue.DraftsFolder);

            var openPublished = Ui.MakeButton("Open the Published folder", false);
            openPublished.SetBounds(236, 500, 210, 36);
            openPublished.Click += (s, e) => OpenFolder(Issue.PublishedFolder);

            var help = new LinkLabel
            {
                Text = "Where do these files live?",
                Font = Ui.Body,
                Bounds = new Rectangle(24, 548, 260, 24),
                LinkColor = Ui.DeepGreen
            };
            help.Click += (s, e) => OpenFolder(_settings.Root);

            // A link rather than a button: changing this is a once-in-a-blue-moon
            // thing, and it should not look like a fourth step.
            var change = new LinkLabel
            {
                Text = "Change where the files live…",
                Font = Ui.Body,
                TextAlign = ContentAlignment.MiddleRight,
                Bounds = new Rectangle(376, 548, 280, 24),
                LinkColor = Ui.MutedInk
            };
            change.Click += (s, e) => Guarded(ChangeSettings);

            Controls.AddRange(new Control[]
            {
                banner, issueLabel, _issuePicker, steps, openDrafts, openPublished, help, change
            });
        }

        void PopulateIssues()
        {
            // Rebuilt whenever the settings change, because every IssueName holds
            // the settings it was built from and would otherwise go on naming
            // files by the old layout.
            IssueName start = _issue != null
                ? new IssueName(_settings, _issue.Year, _issue.Month)
                : IssueName.DefaultFor(_settings, DateTime.Today);

            _issuePicker.Items.Clear();

            // A year either side of the issue she is most likely to want. Enough
            // to fix a mistake or catch up on a missed month, short enough to scan.
            for (int offset = -12; offset <= 12; offset++)
                _issuePicker.Items.Add(start.AddMonths(offset));

            _issuePicker.SelectedIndex = 12;    // the default: next month
        }

        void ChangeSettings()
        {
            Settings chosen = SettingsDialog.Prompt(this, _settings, null);
            if (chosen == null) return;

            _settings = chosen;
            Program.TrySave(_settings);

            Directory.CreateDirectory(_settings.DraftsPath);
            Directory.CreateDirectory(_settings.PublishedPath);

            PopulateIssues();
            RefreshState();
        }

        #endregion

        #region state

        void RefreshState()
        {
            if (Issue == null) return;

            IssueState state;
            try { state = IssueState.For(Issue); }
            catch (Exception ex) { ShowProblem(ex); return; }

            ApplyStep1(state);
            ApplyStep2(state);
            ApplyStep3(state);
        }

        void ApplyStep1(IssueState state)
        {
            _step1.Title = "1.  Start the " + Issue.Display + " newsletter";

            if (!state.MasterExists)
            {
                _step1.Apply(StepStatus.Waiting, "Start this newsletter");
                _step1.Detail = "The master file is missing from the Template folder, so a new " +
                                "newsletter cannot be started. Please tell Kyle.";
                return;
            }

            if (state.Step1 == StepStatus.Done)
            {
                _step1.Apply(StepStatus.Done, "Open in Word");
                _step1.Detail = "Started " + Format(state.DraftStarted) +
                                ". Your copy is in the Drafts folder.";
            }
            else
            {
                _step1.Apply(StepStatus.Current, "Start this newsletter");
                _step1.Detail = "Makes your own copy for " + Issue.Display + " from the master, " +
                                "ready to edit.";
            }
        }

        void ApplyStep2(IssueState state)
        {
            switch (state.Step2)
            {
                case StepStatus.Waiting:
                    _step2.Apply(StepStatus.Waiting, "Check and make the PDF");
                    _step2.Detail = "Available once you have started the newsletter.";
                    break;

                case StepStatus.Current:
                    _step2.Apply(StepStatus.Current, "Check and make the PDF");
                    _step2.Detail = state.DraftIsOpenInWord
                        ? "Please close the newsletter in Word first, then click here."
                        : "Checks that every photo is the right shape, then makes the PDF.";
                    break;

                default:
                    _step2.Apply(StepStatus.Done, "Make the PDF again");
                    _step2.Detail = "PDF made " + Format(state.DraftPdfMadeOn) +
                                    ". Click again if you have changed anything since.";
                    break;
            }
        }

        void ApplyStep3(IssueState state)
        {
            switch (state.Step3)
            {
                case StepStatus.Waiting:
                    _step3.Apply(StepStatus.Waiting, "Publish");
                    _step3.Detail = "Available once the PDF has been made.";
                    break;

                case StepStatus.Current:
                    _step3.Apply(StepStatus.Current, "Publish");
                    _step3.Detail = "Saves the newsletter and the PDF into the Published folder.";
                    break;

                default:
                    _step3.Apply(StepStatus.Done, "Open the Published folder");
                    _step3.Detail = "Published " + Format(state.PublishedOn) + ". All done.";
                    break;
            }
        }

        static string Format(DateTime? when) =>
            when.HasValue ? when.Value.ToString("d MMMM", CultureInfo.CurrentCulture) : "";

        #endregion

        #region actions

        void Step1Clicked()
        {
            var state = IssueState.For(Issue);

            if (state.DraftExists)
            {
                OpenInWord(Issue.DraftDocx);
                return;
            }

            Directory.CreateDirectory(Issue.DraftsFolder);
            File.Copy(Issue.MasterDocx, Issue.DraftDocx);
            RefreshState();

            var answer = MessageBox.Show(this,
                "Your copy of the " + Issue.Display + " newsletter is ready.\n\n" +
                "Open it in Word now?",
                "Ready to edit", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (answer == DialogResult.Yes) OpenInWord(Issue.DraftDocx);
        }

        void Step2Clicked()
        {
            var state = IssueState.For(Issue);
            if (!state.DraftExists)
            {
                ShowMessage("There is no " + Issue.Display + " newsletter to check yet. Start it first.");
                RefreshState();
                return;
            }

            if (!ClearToTouchTheDraft()) return;

            PhotoScanResult scan = null;
            BusyDialog.Run(this, "Checking the photos…", () => scan = DocxPackage.ScanPhotos(Issue.DraftDocx));

            if (scan.AnyProblems)
            {
                using (var dialog = new PhotoFixDialog(scan))
                {
                    dialog.ShowDialog(this);
                    if (dialog.Choice == PhotoChoice.Cancel) return;

                    if (dialog.Choice == PhotoChoice.Fix)
                    {
                        BusyDialog.Run(this, "Putting the photos right…", () =>
                        {
                            // Repair reads the backup and writes the draft, so the
                            // file she works in is replaced only once, at the end.
                            File.Copy(Issue.DraftDocx, Issue.DraftBackup, true);
                            DocxPackage.RepairPhotos(Issue.DraftBackup, Issue.DraftDocx);
                        });
                    }
                }
            }

            BusyDialog.Run(this, "Making the PDF. This can take a minute…",
                           () => WordExport.ExportPdf(Issue.DraftDocx, Issue.DraftPdf));

            RefreshState();

            var answer = MessageBox.Show(this,
                "The PDF is ready, in the Drafts folder.\n\n" +
                "Would you like to look at it now?",
                "PDF ready", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

            if (answer == DialogResult.Yes) OpenFile(Issue.DraftPdf);
        }

        void Step3Clicked()
        {
            var state = IssueState.For(Issue);

            if (state.Step3 == StepStatus.Done)
            {
                OpenFolder(Issue.PublishedFolder);
                return;
            }

            if (!state.DraftExists || !state.DraftPdfExists)
            {
                ShowMessage("The PDF has not been made yet, so there is nothing to publish.");
                RefreshState();
                return;
            }

            if (!ClearToTouchTheDraft()) return;

            bool updateMaster;
            using (var dialog = new PublishDialog(Issue, state.PublishedDocxExists || state.PublishedPdfExists))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                updateMaster = dialog.UpdateMaster;
            }

            SlimResult slim = null;
            BusyDialog.Run(this, "Publishing…", () =>
            {
                Directory.CreateDirectory(Issue.PublishedFolder);

                // The PDF is the record of what actually went out, so it is copied
                // byte for byte and never rebuilt.
                File.Copy(Issue.DraftPdf, Issue.PublishedPdf, true);

                slim = DocxPackage.Slim(Issue.DraftDocx, Issue.PublishedDocx, downsample: true);

                if (updateMaster)
                {
                    if (File.Exists(Issue.MasterDocx))
                        File.Copy(Issue.MasterDocx, Issue.PreviousMasterDocx, true);

                    // The slimmed copy becomes the master, not her working draft:
                    // starting next month from a file already carrying duplicate
                    // images is how this grew in the first place.
                    File.Copy(Issue.PublishedDocx, Issue.MasterDocx, true);
                }
            });

            RefreshState();

            string message = "The " + Issue.Display + " newsletter is published.\n\n" +
                             "Both files are in the Published folder.";

            // Only worth saying when the two figures actually read differently:
            // "came down from 8.4 MB to 8.4 MB" reads like a program that has
            // miscounted.
            if (slim != null && slim.BytesSaved > 0 &&
                Math.Round(slim.BytesBefore / 1048576.0, 1) > Math.Round(slim.BytesAfter / 1048576.0, 1))
                message += string.Format(CultureInfo.CurrentCulture,
                    "\n\nThe published Word file came down from {0:0.0} MB to {1:0.0} MB — the photos " +
                    "were saved at printing size and the copies Word makes of them were removed. " +
                    "The page itself is unchanged, and your working copy in Drafts still has the " +
                    "photos exactly as you put them in.",
                    slim.BytesBefore / 1048576.0, slim.BytesAfter / 1048576.0);

            if (updateMaster)
                message += "\n\nNext month will start from this issue.";

            MessageBox.Show(this, message, "Published", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Whether the draft can be read right now, and a way out if it cannot.
        ///
        /// Word signals ownership with a hidden "~$" file, which it deletes when
        /// it closes properly — but not when it crashes or the drive is pulled.
        /// A leftover file used to jam this permanently: nothing holds the
        /// document, yet every attempt was met with "close it in Word", which she
        /// had already done. So the two cases are told apart and the dead one is
        /// offered a way out.
        /// </summary>
        bool ClearToTouchTheDraft()
        {
            if (!WordExport.IsOpenElsewhere(Issue.DraftDocx)) return true;

            if (!WordExport.HasStaleOwnerFile(Issue.DraftDocx))
            {
                ShowMessage("The newsletter is still open in Word.\n\n" +
                            "Please save it and close Word, then click this button again.");
                return false;
            }

            var answer = MessageBox.Show(this,
                "The " + Issue.Display + " newsletter looks like it is open in Word, but Word " +
                "does not have it — this is usually what Word leaves behind when it closes " +
                "unexpectedly.\n\n" +
                "Shall I tidy that up and carry on?",
                "Left over from last time", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return false;

            if (!WordExport.RemoveOwnerFile(Issue.DraftDocx))
            {
                ShowMessage("That leftover file could not be removed, so this step " +
                            "cannot go ahead yet.\n\n" +
                            "Restarting the computer usually clears it.");
                return false;
            }

            RefreshState();
            return true;
        }

        #endregion

        #region plumbing

        /// <summary>
        /// Every button goes through here. A problem she can act on is shown as a
        /// plain sentence; anything else says so honestly rather than pretending
        /// the step worked.
        /// </summary>
        void Guarded(Action action)
        {
            try
            {
                action();
            }
            catch (UserFixableException ex)
            {
                ShowMessage(ex.Message);
                RefreshState();
            }
            catch (Exception ex)
            {
                ShowProblem(ex);
                RefreshState();
            }
        }

        void ShowMessage(string text) =>
            MessageBox.Show(this, text, "MCAA Newsletter", MessageBoxButtons.OK, MessageBoxIcon.Information);

        void ShowProblem(Exception ex) =>
            MessageBox.Show(this,
                "Something went wrong, and nothing has been changed.\n\n" +
                ex.Message + "\n\n" +
                "If this keeps happening, send this message to Kyle.",
                "MCAA Newsletter", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        void OpenInWord(string path) => OpenFile(path);

        void OpenFile(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { ShowProblem(ex); }
        }

        void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex) { ShowProblem(ex); }
        }

        #endregion
    }
}
