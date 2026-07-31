using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MCAANewsletter
{
    /// <summary>
    /// Confirms the publish, and offers the master update.
    ///
    /// The master option is unticked and stays unticked unless she deliberately
    /// ticks it. It is the only thing in the app that changes what next month
    /// starts from, so it is spelled out in terms of what she would see rather
    /// than in terms of files.
    /// </summary>
    public sealed class PublishDialog : Form
    {
        readonly CheckBox _updateMaster;

        public bool UpdateMaster => _updateMaster.Checked;

        public PublishDialog(IssueName issue, bool willOverwrite)
        {
            Text = "Publish " + issue.Display;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(600, 420);
            BackColor = Color.White;

            var heading = new Label
            {
                Text = "Publish the " + issue.Display + " newsletter",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Ui.BodyInk,
                Bounds = new Rectangle(24, 20, 552, 30)
            };

            var what = new Label
            {
                Text = "These two files will be saved into the Published folder:\n\n" +
                       "     " + Path.GetFileName(issue.PublishedDocx) + "\n" +
                       "     " + Path.GetFileName(issue.PublishedPdf) + "\n\n" +
                       "Your working copy in the Drafts folder is left exactly as it is.",
                Font = Ui.Body,
                ForeColor = Ui.BodyInk,
                Bounds = new Rectangle(24, 56, 552, 120)
            };

            var overwrite = new Label
            {
                Text = willOverwrite
                    ? "Note: this issue has already been published once. Publishing again will replace those two files."
                    : "",
                Font = Ui.BodyBold,
                ForeColor = ColorTranslator.FromHtml("#B04A00"),
                Bounds = new Rectangle(24, 174, 552, 40)
            };

            var separator = new Label
            {
                BorderStyle = BorderStyle.Fixed3D,
                Bounds = new Rectangle(24, 220, 552, 2)
            };

            _updateMaster = new CheckBox
            {
                Text = "Also start next month from this issue",
                Font = Ui.BodyBold,
                ForeColor = Ui.BodyInk,
                Checked = false,
                Bounds = new Rectangle(24, 236, 552, 26)
            };

            var masterExplanation = new Label
            {
                Text =
                    "Leave this unticked unless you mean it.\n\n" +
                    "Ticked, next month's newsletter will begin as a copy of this " + issue.Display +
                    " issue, so you would be editing over this month's articles and photos.\n\n" +
                    "Unticked, next month starts from the same master you started from this time. " +
                    "Either way, the two published files above are saved.",
                Font = Ui.Body,
                ForeColor = Ui.MutedInk,
                Bounds = new Rectangle(44, 264, 532, 96)
            };

            var publish = Ui.MakeButton("Publish", true);
            publish.SetBounds(384, 370, 100, 38);
            publish.Click += (s, e) => DialogResult = DialogResult.OK;

            var cancel = Ui.MakeButton("Cancel", false);
            cancel.SetBounds(492, 370, 84, 38);
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;

            AcceptButton = publish;
            CancelButton = cancel;

            Controls.AddRange(new Control[]
            {
                heading, what, overwrite, separator, _updateMaster, masterExplanation, publish, cancel
            });
        }
    }
}
