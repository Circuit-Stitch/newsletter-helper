using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MCAANewsletter
{
    public enum PhotoRepair
    {
        /// <summary>Centred crop inside the existing box. Footprint untouched.</summary>
        Crop,
        /// <summary>The over-long box dimension is reduced. A box never grows.</summary>
        Resize
    }

    /// <summary>
    /// One distorted picture placement. A photo used twice produces two findings;
    /// the dialog groups them by <see cref="MediaName"/> before showing anything.
    /// </summary>
    public sealed class PhotoFinding
    {
        /// <summary>e.g. "image7.png" — the part name inside word/media/.</summary>
        public string MediaName;

        /// <summary>
        /// Hash of the image bytes. Word 2010 stores each photo twice — once for
        /// the drawing and once for its mc:Fallback copy — under different part
        /// names, so this is what identifies a photo as far as she is concerned.
        /// </summary>
        public string ContentKey;

        /// <summary>
        /// Signed. Positive means the box is proportionally wider than the photo,
        /// so the picture is stretched sideways; negative means stretched tall.
        /// </summary>
        public double Distortion;

        public PhotoRepair Repair;

        /// <summary>"sides", "top/bottom" for a crop; "width", "height" for a resize.</summary>
        public string Axis;

        /// <summary>Fraction of the photo a crop removes. Zero for a resize.</summary>
        public double CropCost;

        public double BoxBeforeWidthIn, BoxBeforeHeightIn;
        public double BoxAfterWidthIn, BoxAfterHeightIn;

        public int NativeWidth, NativeHeight;

        /// <summary>The image bytes, so the dialog can show her which photo it means.</summary>
        public byte[] ImageBytes;

        public double DistortionPercent => Math.Abs(Distortion) * 100.0;

        /// <summary>
        /// What is wrong, in her words. No "aspect ratio" and no percentages
        /// dressed up as precision — just the direction and roughly how much.
        /// </summary>
        public string Problem
        {
            get
            {
                string direction = Distortion > 0 ? "wide" : "tall";
                double pct = DistortionPercent;
                string amount = pct >= 10 ? "noticeably" : "slightly";
                return string.Format(CultureInfo.InvariantCulture,
                    "Stretched {0} too {1} — about {2:0}% out of shape.", amount, direction, pct);
            }
        }

        /// <summary>What the fix will do, in her words.</summary>
        public string Fix
        {
            get
            {
                if (Repair == PhotoRepair.Crop)
                    return string.Format(CultureInfo.InvariantCulture,
                        "Trim about {0:0}% off the {1} so it sits square in the same space.",
                        CropCost * 100.0, Axis == "sides" ? "sides" : "top and bottom");

                string dimension = Axis == "width" ? "narrower" : "shorter";
                return string.Format(CultureInfo.InvariantCulture,
                    "Make the space {0} — {1:0.0} × {2:0.0} in becomes {3:0.0} × {4:0.0} in. Nothing is cut off.",
                    dimension, BoxBeforeWidthIn, BoxBeforeHeightIn, BoxAfterWidthIn, BoxAfterHeightIn);
            }
        }
    }

    public sealed class PhotoScanResult
    {
        public List<PhotoFinding> Findings = new List<PhotoFinding>();

        /// <summary>
        /// Pictures the scan could not measure — an unrecognised media format, or
        /// markup it did not know how to read. Surfaced rather than swallowed, so
        /// a document that quietly falls outside the tested shape is visible.
        /// </summary>
        public List<string> Unreadable = new List<string>();

        public bool AnyProblems => Findings.Count > 0;

        /// <summary>
        /// One finding per distinct photo, worst first — what the dialog lists.
        /// Grouped by content, not by part name, or she would be shown every photo
        /// twice: Word stores each one under two names.
        /// </summary>
        public List<PhotoFinding> DistinctPhotos =>
            Findings.GroupBy(f => f.ContentKey ?? f.MediaName)
                    .Select(g => g.OrderByDescending(f => Math.Abs(f.Distortion)).First())
                    .OrderByDescending(f => Math.Abs(f.Distortion))
                    .ToList();

        public int PlacementCount => Findings.Count;

        public string Summary
        {
            get
            {
                int photos = DistinctPhotos.Count;
                if (photos == 0) return "Every photo is the right shape.";

                string noun = photos == 1 ? "photo looks" : "photos look";
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} {1} stretched out of shape.", photos, noun);
            }
        }
    }

    public sealed class SlimResult
    {
        public long BytesBefore, BytesAfter;
        public int MediaPartsBefore, MediaPartsAfter;
        public int DuplicatesRemoved;

        /// <summary>
        /// True when slimming found nothing to win and the original was archived
        /// exactly as it was. Not a failure — it is what a clean document looks
        /// like going through a second time.
        /// </summary>
        public bool KeptOriginal;

        /// <summary>
        /// Media parts present in the zip that nothing referenced. Reported rather
        /// than assumed — the draft's part count and its reference count disagree,
        /// and it is worth knowing on real data which of the two explains it.
        /// </summary>
        public List<string> OrphanParts = new List<string>();

        public long BytesSaved => BytesBefore - BytesAfter;
        public double PercentSaved => BytesBefore == 0 ? 0 : 100.0 * BytesSaved / BytesBefore;
    }
}
