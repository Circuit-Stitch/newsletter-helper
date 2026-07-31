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
    /// </summary>
    public sealed class IssueName
    {
        public const string DraftSuffix = "-DRAFT";
        public const string MasterFileName = "MCAA-Newsletter-MASTER.docx";
        public const string PreviousMasterFileName = "MCAA-Newsletter-MASTER (previous).docx";

        public int Year { get; }
        public int Month { get; }

        readonly string _root;

        public IssueName(string root, int year, int month)
        {
            if (month < 1 || month > 12) throw new ArgumentOutOfRangeException(nameof(month));
            _root = root;
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
        public string BaseName =>
            Year.ToString(CultureInfo.InvariantCulture) + " " + MonthName + " MCAA Newsletter";

        public string DraftsFolder => Path.Combine(_root, "Drafts");
        public string PublishedFolder => Path.Combine(_root, "Published");
        public string TemplateFolder => Path.Combine(_root, "Template");

        public string DraftDocx => Path.Combine(DraftsFolder, BaseName + DraftSuffix + ".docx");
        public string DraftPdf => Path.Combine(DraftsFolder, BaseName + DraftSuffix + ".pdf");

        /// <summary>Kept beside the draft when the photo repair rewrites it.</summary>
        public string DraftBackup =>
            Path.Combine(DraftsFolder, BaseName + DraftSuffix + " (before photo fix).docx");

        public string PublishedDocx => Path.Combine(PublishedFolder, BaseName + ".docx");
        public string PublishedPdf => Path.Combine(PublishedFolder, BaseName + ".pdf");

        public string MasterDocx => Path.Combine(TemplateFolder, MasterFileName);
        public string PreviousMasterDocx => Path.Combine(TemplateFolder, PreviousMasterFileName);

        /// <summary>
        /// The issue the app opens on. She works a month ahead — the archive shows
        /// the August issue finished in July — so "next month" is the right guess
        /// far more often than the current one.
        /// </summary>
        public static IssueName DefaultFor(string root, DateTime today)
        {
            DateTime next = new DateTime(today.Year, today.Month, 1).AddMonths(1);
            return new IssueName(root, next.Year, next.Month);
        }

        public IssueName AddMonths(int months)
        {
            DateTime d = new DateTime(Year, Month, 1).AddMonths(months);
            return new IssueName(_root, d.Year, d.Month);
        }

        public override string ToString() => Display;
    }
}
