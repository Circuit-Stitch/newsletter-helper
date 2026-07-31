using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MCAANewsletter
{
    /// <summary>
    /// Where the newsletter lives and what its files are called.
    ///
    /// This used to be derived entirely from where the .exe sat, which worked
    /// right up until it didn't: put the program anywhere without a Template
    /// folder beside it and there was no way back except moving the file in
    /// Explorer. The layout is now stated explicitly and re-checked on every
    /// launch by <see cref="Problem"/>.
    ///
    /// Note what is *not* stored here: anything about the state of an issue.
    /// Whether a draft exists, whether the PDF is made, whether it is published —
    /// all of that is still read off the filesystem on every refresh, so the app
    /// still cannot disagree with what is actually in the folder.
    /// </summary>
    public sealed class Settings
    {
        public const string DefaultDraftsFolder = "Drafts";
        public const string DefaultPublishedFolder = "Published";
        public const string DefaultTemplateFolder = "Template";
        public const string DefaultMasterFileName = "MCAA-Newsletter-MASTER.docx";
        public const string DefaultIssuePattern = "{year} {month} MCAA Newsletter";
        public const string DefaultDraftSuffix = "-DRAFT";

        string _root = "";

        /// <summary>
        /// Always stored as a full path. A relative one would resolve against
        /// whatever directory the program happened to be started in — the folder
        /// holding the .exe when double-clicked, but not when launched from a
        /// shortcut with its own "Start in" — so the same saved setting would mean
        /// two different folders on two different launches.
        /// </summary>
        public string Root
        {
            get { return _root; }
            set
            {
                string typed = (value ?? "").Trim();
                try { _root = typed.Length == 0 ? "" : Path.GetFullPath(typed); }
                catch (Exception) { _root = typed; }    // not a path; Problem() says so
            }
        }

        // ponytail: plain fields for the rest. This is a settings bag edited
        // field-by-field by one dialog; properties would buy nothing.
        public string DraftsFolder = DefaultDraftsFolder;
        public string PublishedFolder = DefaultPublishedFolder;
        public string TemplateFolder = DefaultTemplateFolder;
        public string MasterFileName = DefaultMasterFileName;
        public string IssuePattern = DefaultIssuePattern;
        public string DraftSuffix = DefaultDraftSuffix;

        public Settings Clone() => (Settings)MemberwiseClone();

        #region paths

        public string DraftsPath => Path.Combine(Root, DraftsFolder);
        public string PublishedPath => Path.Combine(Root, PublishedFolder);
        public string TemplatePath => Path.Combine(Root, TemplateFolder);
        public string MasterPath => Path.Combine(TemplatePath, MasterFileName);

        /// <summary>
        /// The safety copy taken before the master is replaced. Derived from the
        /// master's own name so that renaming one renames both.
        /// </summary>
        public string PreviousMasterPath => Path.Combine(TemplatePath,
            Path.GetFileNameWithoutExtension(MasterFileName) + " (previous)" +
            Path.GetExtension(MasterFileName));

        #endregion

        #region validation

        /// <summary>
        /// What stops the app running, worded as a sentence to show her, or null
        /// if nothing does.
        ///
        /// This runs on every launch and every time the settings window is
        /// touched. The settings file is authoritative — so this check refusing to
        /// proceed is the only thing standing between a folder that has moved and
        /// the app doing work in the wrong place.
        /// </summary>
        public string Problem()
        {
            foreach (var field in NamedParts())
            {
                string bad = BadName(field.Item1, field.Item2);
                if (bad != null) return bad;
            }

            string patternProblem = BadPattern();
            if (patternProblem != null) return patternProblem;

            if (DraftSuffix == null || DraftSuffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "The draft ending cannot contain any of:   \\ / : * ? \" < > |";

            if (string.IsNullOrWhiteSpace(Root))
                return "No newsletter folder has been chosen yet.";

            try { Path.GetFullPath(Root); }
            catch (Exception) { return "That is not a folder path Windows can use:\n\n" + Root; }

            if (!Directory.Exists(Root))
                return "There is no folder here:\n\n" + Root;

            if (!Directory.Exists(TemplatePath))
                return "There is no \"" + TemplateFolder + "\" folder inside:\n\n" + Root;

            return null;
        }

        /// <summary>
        /// Worth saying, not worth blocking on. The master can be legitimately
        /// absent — she may be setting the program up before the folder has
        /// finished copying — and the main window already explains that case in
        /// its own words rather than trapping her in a settings dialog.
        /// </summary>
        public string Warning()
        {
            if (Problem() != null) return null;
            return File.Exists(MasterPath)
                ? null
                : "\"" + MasterFileName + "\" is not in the \"" + TemplateFolder + "\" folder yet.";
        }

        IEnumerable<Tuple<string, string>> NamedParts()
        {
            yield return Tuple.Create("Template folder name", TemplateFolder);
            yield return Tuple.Create("Drafts folder name", DraftsFolder);
            yield return Tuple.Create("Published folder name", PublishedFolder);
            yield return Tuple.Create("master file name", MasterFileName);
        }

        /// <summary>
        /// These are single names that get joined onto the root, not paths. A
        /// slash or a ".." here would silently move the whole operation somewhere
        /// else on the disk, so they are checked rather than trusted.
        /// </summary>
        static string BadName(string label, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "The " + label + " cannot be left empty.";
            if (name != name.Trim())
                return "The " + label + " cannot start or end with a space.";
            if (name == "." || name == "..")
                return "The " + label + " needs to be a real name.";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "The " + label + " cannot contain any of:   \\ / : * ? \" < > |";
            return null;
        }

        string BadPattern()
        {
            if (string.IsNullOrWhiteSpace(IssuePattern))
                return "The newsletter file name cannot be left empty.";

            // Without both, every month lands on one file name and each issue
            // overwrites the last. This is the check that prevents losing work.
            if (IssuePattern.IndexOf(YearToken, StringComparison.Ordinal) < 0 ||
                IssuePattern.IndexOf(MonthToken, StringComparison.Ordinal) < 0)
                return "The newsletter file name has to include both " + YearToken + " and " +
                       MonthToken + ", spelled in lower case.\n\n" +
                       "Without them every month would try to use the same file name, " +
                       "and each issue would overwrite the one before it.";

            if (Expand(IssuePattern, 2026, 8).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return "The newsletter file name cannot contain any of:   \\ / : * ? \" < > |";

            return null;
        }

        #endregion

        #region naming

        public const string YearToken = "{year}";
        public const string MonthToken = "{month}";

        /// <summary>
        /// Month names come from the invariant culture on purpose. A machine set
        /// to another locale must still produce "August", or the archive would end
        /// up with two spellings of the same month.
        /// </summary>
        public static string Expand(string pattern, int year, int month) =>
            pattern
                .Replace(YearToken, year.ToString("0000", CultureInfo.InvariantCulture))
                .Replace(MonthToken, CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month));

        #endregion

        #region storage

        /// <summary>
        /// Kept in her profile rather than beside the .exe, so moving or renaming
        /// the program does not lose the setup, and so the newsletter folder she
        /// opens in Explorer stays free of the program's own files.
        /// </summary>
        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MCAA Newsletter", "settings.txt");

        /// <summary>Null if there is nothing saved, or it could not be read.</summary>
        public static Settings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;

                var s = new Settings();
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = trimmed.Substring(0, eq).Trim();
                    string value = trimmed.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "root": s.Root = value; break;
                        case "drafts": s.DraftsFolder = value; break;
                        case "published": s.PublishedFolder = value; break;
                        case "template": s.TemplateFolder = value; break;
                        case "master": s.MasterFileName = value; break;
                        case "issue-name": s.IssuePattern = value; break;
                        case "draft-ending": s.DraftSuffix = value; break;
                    }
                }
                return s;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public void Save()
        {
            string folder = Path.GetDirectoryName(FilePath);
            if (folder != null) Directory.CreateDirectory(folder);

            File.WriteAllLines(FilePath, new[]
            {
                "# MCAA Newsletter — where the files live.",
                "# Changed from the Settings button in the program.",
                "# Delete this file and the program will look for the folder itself.",
                "",
                "root=" + Root,
                "template=" + TemplateFolder,
                "drafts=" + DraftsFolder,
                "published=" + PublishedFolder,
                "master=" + MasterFileName,
                "issue-name=" + IssuePattern,
                "draft-ending=" + DraftSuffix
            });
        }

        /// <summary>
        /// First run only: look for the newsletter folder around the .exe, so the
        /// usual case — the program dropped in beside Drafts, Published and
        /// Template — needs no setting up at all.
        ///
        /// Deliberately never used to *replace* saved settings. If a saved folder
        /// has gone missing the app asks rather than quietly adopting a different
        /// one it happens to find near itself; working on the wrong master without
        /// noticing is a worse outcome than stopping to ask.
        /// </summary>
        public static Settings AutoDetect(string startDirectory)
        {
            for (var dir = new DirectoryInfo(startDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = new Settings { Root = dir.FullName };

                // Same predicate the app validates with, plus the master actually
                // being there. Guessing needs to be more certain than accepting.
                if (candidate.Problem() == null && candidate.Warning() == null)
                    return candidate;
            }
            return null;
        }

        #endregion
    }
}
