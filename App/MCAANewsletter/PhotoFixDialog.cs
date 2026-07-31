using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MCAANewsletter
{
    public enum PhotoChoice { Cancel, Fix, LeaveAlone }

    /// <summary>
    /// Shows the stretched photos and offers to put them right.
    ///
    /// Each row shows the photo itself rather than naming a page. Word decides
    /// pagination at render time, so a page number would be a guess — and a
    /// thumbnail answers "which one do you mean?" better than a number anyway.
    /// </summary>
    public sealed class PhotoFixDialog : Form
    {
        readonly List<IDisposable> _disposables = new List<IDisposable>();

        public PhotoChoice Choice { get; private set; } = PhotoChoice.Cancel;

        public PhotoFixDialog(PhotoScanResult scan)
        {
            var photos = scan.DistinctPhotos;

            Text = "Some photos are the wrong shape";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 520);
            BackColor = Color.White;

            var heading = new Label
            {
                Text = scan.Summary,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = Ui.BodyInk,
                Bounds = new Rectangle(24, 20, 592, 30)
            };

            var explanation = new Label
            {
                Text = "This happens when a photo is resized by dragging the handle on a side " +
                       "instead of a corner. It can be put right automatically — nothing else " +
                       "in the newsletter will move.",
                Font = Ui.Body,
                ForeColor = Ui.MutedInk,
                Bounds = new Rectangle(24, 52, 592, 48)
            };

            var list = new Panel
            {
                Bounds = new Rectangle(24, 106, 592, 330),
                AutoScroll = true,
                BackColor = Ui.PaleGreen,
                BorderStyle = BorderStyle.FixedSingle
            };

            int y = 8;
            foreach (var photo in photos)
            {
                list.Controls.Add(BuildRow(photo, y, list.ClientSize.Width - 24));
                y += 96;
            }

            var fix = Ui.MakeButton("Fix these photos, then make the PDF", true);
            fix.SetBounds(24, 452, 300, 38);
            fix.Click += (s, e) => { Choice = PhotoChoice.Fix; DialogResult = DialogResult.OK; };

            var leave = Ui.MakeButton("Leave them as they are", false);
            leave.SetBounds(332, 452, 180, 38);
            leave.Click += (s, e) => { Choice = PhotoChoice.LeaveAlone; DialogResult = DialogResult.OK; };

            var cancel = Ui.MakeButton("Cancel", false);
            cancel.SetBounds(520, 452, 96, 38);
            cancel.Click += (s, e) => { Choice = PhotoChoice.Cancel; DialogResult = DialogResult.Cancel; };

            AcceptButton = fix;
            CancelButton = cancel;

            Controls.AddRange(new Control[] { heading, explanation, list, fix, leave, cancel });

            FormClosed += (s, e) =>
            {
                foreach (var d in _disposables) { try { d.Dispose(); } catch { } }
            };
        }

        Control BuildRow(PhotoFinding photo, int top, int width)
        {
            var row = new Panel
            {
                Bounds = new Rectangle(8, top, width, 88),
                BackColor = Color.White,
                BorderStyle = BorderStyle.None
            };

            var thumbnail = new PictureBox
            {
                Bounds = new Rectangle(8, 8, 72, 72),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White
            };

            Image image = LoadThumbnail(photo.ImageBytes);
            if (image != null) thumbnail.Image = image;

            var problem = new Label
            {
                Text = photo.Problem,
                Font = Ui.BodyBold,
                ForeColor = Ui.BodyInk,
                Bounds = new Rectangle(92, 10, width - 104, 22)
            };

            var fix = new Label
            {
                Text = photo.Fix,
                Font = Ui.Body,
                ForeColor = Ui.MutedInk,
                Bounds = new Rectangle(92, 34, width - 104, 46)
            };

            row.Controls.AddRange(new Control[] { thumbnail, problem, fix });
            return row;
        }

        /// <summary>
        /// Decodes at native proportions on purpose — she should see the photo as
        /// it really is, not as the document is currently squashing it.
        /// </summary>
        Image LoadThumbnail(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                var stream = new MemoryStream(bytes, false);
                var image = Image.FromStream(stream, false, false);
                _disposables.Add(stream);
                _disposables.Add(image);
                return image;
            }
            catch
            {
                // A format GDI+ will not decode still gets a row, just no picture.
                return null;
            }
        }
    }
}
