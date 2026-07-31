using System;
using System.IO;

namespace MCAANewsletter
{
    public enum StepStatus
    {
        /// <summary>Already done. Shown ticked, button offers a way back in.</summary>
        Done,
        /// <summary>The one thing to do next. The only enabled button on the window.</summary>
        Current,
        /// <summary>Not reachable yet. Shown greyed with a reason.</summary>
        Waiting
    }

    /// <summary>
    /// Where an issue stands, derived entirely from what is on disk.
    ///
    /// The app deliberately remembers nothing between runs. Anything it stored
    /// could disagree with the folder — she might delete a draft in Explorer, or
    /// copy one in from elsewhere — and a wrong answer here is worse than a
    /// slightly slower one.
    /// </summary>
    public sealed class IssueState
    {
        public IssueName Issue { get; private set; }

        public bool MasterExists { get; private set; }
        public bool DraftExists { get; private set; }
        public bool DraftPdfExists { get; private set; }
        public bool PublishedDocxExists { get; private set; }
        public bool PublishedPdfExists { get; private set; }
        public bool DraftIsOpenInWord { get; private set; }

        public DateTime? DraftStarted { get; private set; }
        public DateTime? PublishedOn { get; private set; }

        public bool FullyPublished => PublishedDocxExists && PublishedPdfExists;

        public StepStatus Step1 { get; private set; }
        public StepStatus Step2 { get; private set; }
        public StepStatus Step3 { get; private set; }

        public static IssueState For(IssueName issue)
        {
            var s = new IssueState { Issue = issue };

            s.MasterExists = File.Exists(issue.MasterDocx);
            s.DraftExists = File.Exists(issue.DraftDocx);
            s.DraftPdfExists = File.Exists(issue.DraftPdf);
            s.PublishedDocxExists = File.Exists(issue.PublishedDocx);
            s.PublishedPdfExists = File.Exists(issue.PublishedPdf);

            if (s.DraftExists)
            {
                s.DraftStarted = File.GetCreationTime(issue.DraftDocx);
                s.DraftIsOpenInWord = WordExport.IsOpenElsewhere(issue.DraftDocx);
            }
            if (s.PublishedPdfExists)
                s.PublishedOn = File.GetLastWriteTime(issue.PublishedPdf);

            if (s.FullyPublished)
            {
                s.Step1 = StepStatus.Done;
                s.Step2 = StepStatus.Done;
                s.Step3 = StepStatus.Done;
            }
            else if (s.DraftExists && s.DraftPdfExists)
            {
                s.Step1 = StepStatus.Done;
                s.Step2 = StepStatus.Done;
                s.Step3 = StepStatus.Current;
            }
            else if (s.DraftExists)
            {
                s.Step1 = StepStatus.Done;
                s.Step2 = StepStatus.Current;
                s.Step3 = StepStatus.Waiting;
            }
            else
            {
                s.Step1 = StepStatus.Current;
                s.Step2 = StepStatus.Waiting;
                s.Step3 = StepStatus.Waiting;
            }

            return s;
        }
    }
}
