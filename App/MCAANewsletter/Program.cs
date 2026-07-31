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

            Settings settings = Resolve();
            if (settings == null) return;       // she closed the setup window

            Directory.CreateDirectory(settings.DraftsPath);
            Directory.CreateDirectory(settings.PublishedPath);

            Application.Run(new MainForm(settings));
        }

        /// <summary>
        /// Settled in this order, and the order is the whole point:
        ///
        ///   1. Saved settings, if there are any. These are authoritative.
        ///   2. Otherwise — first run only — look for the folder around the .exe,
        ///      so the usual install needs no setting up at all.
        ///   3. Whatever came out of those is re-checked. If it does not hold up,
        ///      ask, with the reason on screen.
        ///
        /// What deliberately does not happen: falling back to auto-detection when
        /// saved settings turn out to be wrong. A folder that has gone missing —
        /// a network drive offline, a renamed directory — would otherwise see the
        /// program quietly adopt some other newsletter folder near itself and
        /// start work on the wrong master. Stopping to ask is the better failure.
        /// </summary>
        static Settings Resolve()
        {
            string exeFolder = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";

            Settings saved = Settings.Load();
            Settings settings = saved ?? Settings.AutoDetect(exeFolder);

            if (settings != null && settings.Problem() == null)
                return settings;

            string reason = settings == null
                ? "Please point the program at the newsletter folder — the one holding the " +
                  "Template folder."
                : "The newsletter folder cannot be used at the moment:  " + settings.Problem();

            Settings chosen = SettingsDialog.Prompt(
                null, settings ?? new Settings { Root = exeFolder }, reason);

            if (chosen == null) return null;

            TrySave(chosen);
            return chosen;
        }

        /// <summary>
        /// A profile that cannot be written to is worth saying out loud — she
        /// would otherwise be asked to set the program up again every single time
        /// and have no idea why — but it is not worth refusing to run over.
        /// </summary>
        public static void TrySave(Settings settings)
        {
            try
            {
                settings.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "These settings could not be saved, so the program will ask again next time " +
                    "it starts. Everything else will work as normal.\n\n" +
                    "It tried to write:\n" + Settings.FilePath + "\n\n(" + ex.Message + ")",
                    "MCAA Newsletter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
