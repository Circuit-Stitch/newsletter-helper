using System;
using System.Globalization;
using System.IO;

namespace MCAANewsletter
{
    /// <summary>
    /// The one place that knows the naming convention. Everything else asks this
    /// for a path rather than building strings of its own.
    ///
    ///   Drafts/2026 August MCAA Newsletter-DRAFT.docx
    ///   Drafts/2026 August MCAA Newsletter-DRAFT.pdf
    ///   Published/2026 August MCAA Newsletter.docx
    ///   Published/2026 August MCAA Newsletter.pdf
    ///
    /// The folder and file names all come from <see cref="Settings"/>; the shape
    /// above is what they default to.
    /// </summary>
    public sealed class IssueName
    {
        public int Year { get; }
        public int Month { get; }
        public Settings Settings { get; }

        public IssueName(Settings settings, int year, int month)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (month < 1 || month > 12) throw new ArgumentOutOfRangeException(nameof(month));
            Settings = settings;
            Year = year;
            Month = month;
        }

        /// <summary>
        /// Month names are taken from the invariant culture on purpose. A machine
        /// set to another locale must still produce "August", or the archive would
        /// end up with two spellings of the same month.
        /// </summary>
        public string MonthName =>
            CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(Month);

        /// <summary>"August 2026" — how the issue is described on screen.</summary>
        public string Display => MonthName + " " + Year.ToString(CultureInfo.InvariantCulture);

        /// <summary>"2026 August MCAA Newsletter" — the stem both folders build on.</summary>
        public string BaseName => Settings.Expand(Settings.IssuePattern, Year, Month);

        public string DraftsFolder => Settings.DraftsPath;
        public string PublishedFolder => Settings.PublishedPath;
        public string TemplateFolder => Settings.TemplatePath;

        string DraftStem => BaseName + Settings.DraftSuffix;

        public string DraftDocx => Path.Combine(DraftsFolder, DraftStem + ".docx");
        public string DraftPdf => Path.Combine(DraftsFolder, DraftStem + ".pdf");

        /// <summary>Kept beside the draft when the photo repair rewrites it.</summary>
        public string DraftBackup =>
            Path.Combine(DraftsFolder, DraftStem + " (before photo fix).docx");

        public string PublishedDocx => Path.Combine(PublishedFolder, BaseName + ".docx");
        public string PublishedPdf => Path.Combine(PublishedFolder, BaseName + ".pdf");

        public string MasterDocx => Settings.MasterPath;
        public string PreviousMasterDocx => Settings.PreviousMasterPath;

        /// <summary>
        /// The issue the app opens on. She works a month ahead — the archive shows
        /// the August issue finished in July — so "next month" is the right guess
        /// far more often than the current one.
        /// </summary>
        public static IssueName DefaultFor(Settings settings, DateTime today)
        {
            DateTime next = new DateTime(today.Year, today.Month, 1).AddMonths(1);
            return new IssueName(settings, next.Year, next.Month);
        }

        public IssueName AddMonths(int months)
        {
            DateTime d = new DateTime(Year, Month, 1).AddMonths(months);
            return new IssueName(Settings, d.Year, d.Month);
        }

        public override string ToString() => Display;
    }
}
