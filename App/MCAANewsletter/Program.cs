using System;
using System.IO;
using System.Windows.Forms;

namespace MCAANewsletter
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // The only place GDI+ is handed to the package layer. Downsampling is
            // off by default, so this is wired up but dormant until asked for.
            DocxPackage.Reduce = ImageReducer.TryReduce;

            string root = ResolveRoot();

            if (!Directory.Exists(Path.Combine(root, "Template")))
            {
                MessageBox.Show(
                    "This program expects to sit in the newsletter folder, " +
                    "alongside the Drafts, Published and Template folders.\n\n" +
                    "It is currently in:\n" + root + "\n\n" +
                    "Please move it into the newsletter folder and try again.",
                    "MCAA Newsletter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Directory.CreateDirectory(Path.Combine(root, "Drafts"));
            Directory.CreateDirectory(Path.Combine(root, "Published"));

            Application.Run(new MainForm(root));
        }

        /// <summary>
        /// The newsletter folder is wherever the program is, which is why there is
        /// no setting to configure and nothing to point at: she can move or rename
        /// the whole folder and it keeps working.
        ///
        /// The walk up the tree is for running from a build output directory during
        /// development; in her copy the very first check succeeds.
        /// </summary>
        static string ResolveRoot()
        {
            string start = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";

            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "Template")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "Published")))
                    return dir.FullName;
            }

            return start;
        }
    }
}
