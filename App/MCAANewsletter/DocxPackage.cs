using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MCAANewsletter
{
    /// <summary>
    /// All the .docx surgery, done at the zip/XML level.
    ///
    /// Nothing here goes through Word, and that is the point. Word's own picture
    /// reset would work, but saving through Word 2010 re-duplicates every image
    /// and restores the revision IDs — the exact bloat these operations remove.
    /// Editing the package directly leaves every other byte alone.
    ///
    /// Ported from the Python pipeline in Scripts/, which was verified against the
    /// twelve real issues: word/document.xml came out byte-identical in all 20
    /// documents the de-dupe touched, and no picture box grew in the repair.
    /// </summary>
    public static class DocxPackage
    {
        public const int EmuPerInch = 914400;

        // --- photo repair thresholds, unchanged from the verified Python ---
        const double Tolerance = 0.005;     // within 0.5%, leave it alone
        const double CropLimit = 0.10;      // beyond this a crop removes real content
        const int MinShortEdge = 200;       // below this it is a rule or an icon
        const double MaxNativeRatio = 3.0;  // beyond this it is decorative chrome

        // --- downsampling, off by default (see Slim) ---
        const int MaxEdge = 1600;
        const int JpegQuality = 82;
        const int MinPhoto = 400;

        static string Marker => string.Format(CultureInfo.InvariantCulture,
            "MCAA-shrink v1 max_edge={0} q={1} min_photo={2}", MaxEdge, JpegQuality, MinPhoto);

        public delegate byte[] ImageReduceDelegate(byte[] raw, string extension, int maxEdge,
                                                   int quality, int minPhoto, out string newExtension);

        /// <summary>
        /// Optional downsampler, supplied by the Windows front end at startup.
        ///
        /// A delegate rather than a direct call because re-encoding needs GDI+ and
        /// the rest of this class does not. Keeping the dependency out here is what
        /// lets the package surgery be exercised on any machine, against the real
        /// documents, without Windows in the loop.
        /// </summary>
        public static ImageReduceDelegate Reduce;

        #region regexes

        static readonly Regex InlineBlock = new Regex(@"<wp:inline\b.*?</wp:inline>",
            RegexOptions.Singleline | RegexOptions.Compiled);
        static readonly Regex BlipEmbed = new Regex(@"<a:blip[^>]*r:embed=""(rId\d+)""", RegexOptions.Compiled);
        static readonly Regex AExt = new Regex(@"<a:ext cx=""(\d+)"" cy=""(\d+)""", RegexOptions.Compiled);
        static readonly Regex WpExtent = new Regex(@"<wp:extent cx=""(\d+)"" cy=""(\d+)""", RegexOptions.Compiled);
        static readonly Regex SrcRect = new Regex(@"<a:srcRect([^/>]*)/>", RegexOptions.Compiled);
        static readonly Regex SrcRectAttr = new Regex(@"([lrtb])=""(-?\d+)""", RegexOptions.Compiled);
        static readonly Regex StretchTag = new Regex(@"<a:stretch\b", RegexOptions.Compiled);
        static readonly Regex RelTarget = new Regex(@"Id=""(rId\d+)""[^>]*Target=""([^""]+)""", RegexOptions.Compiled);
        static readonly Regex MediaTarget = new Regex(@"Target=""((?:\.\./)?media/)([^""]+)""", RegexOptions.Compiled);

        static readonly Regex RsidAttr = new Regex(
            @"\s+w:rsid(?:R|RDefault|P|RPr|Tr|Del|Sect)=""[0-9A-Fa-f]+""", RegexOptions.Compiled);
        static readonly Regex RsidsBlock = new Regex(@"<w:rsids>.*?</w:rsids>",
            RegexOptions.Singleline | RegexOptions.Compiled);
        static readonly Regex DivsBlock = new Regex(@"<w:divs>.*?</w:divs>",
            RegexOptions.Singleline | RegexOptions.Compiled);
        static readonly Regex LastModifiedBy = new Regex(@"<cp:lastModifiedBy>.*?</cp:lastModifiedBy>",
            RegexOptions.Singleline | RegexOptions.Compiled);
        static readonly Regex RevisionTag = new Regex(@"<cp:revision>.*?</cp:revision>",
            RegexOptions.Singleline | RegexOptions.Compiled);
        static readonly Regex TypesOpen = new Regex(@"(<Types[^>]*>)", RegexOptions.Compiled);

        #endregion

        #region package read / write

        sealed class Part
        {
            public string Name;
            public byte[] Data;
            public DateTimeOffset LastWrite;
        }

        sealed class MediaImage
        {
            public int Width, Height;
            public byte[] Bytes;
            public string ContentKey;
        }

        static List<Part> ReadParts(string path)
        {
            var parts = new List<Part>();
            using (var archive = ZipFile.OpenRead(path))
            {
                foreach (var entry in archive.Entries)
                {
                    // Directory entries have an empty name and no content.
                    if (string.IsNullOrEmpty(entry.Name) && entry.Length == 0) continue;

                    using (var stream = entry.Open())
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        parts.Add(new Part
                        {
                            Name = entry.FullName,
                            Data = buffer.ToArray(),
                            LastWrite = entry.LastWriteTime
                        });
                    }
                }
            }
            return parts;
        }

        static void WriteParts(string path, IEnumerable<Part> parts)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var part in parts)
                {
                    var entry = archive.CreateEntry(part.Name, CompressionLevel.Optimal);
                    // The zip format cannot store dates outside 1980-2107; Word
                    // occasionally leaves an epoch-zero entry behind.
                    if (part.LastWrite.Year >= 1980 && part.LastWrite.Year <= 2107)
                        entry.LastWriteTime = part.LastWrite;

                    using (var stream = entry.Open())
                        stream.Write(part.Data, 0, part.Data.Length);
                }
            }
        }

        static string Decode(byte[] data) => new UTF8Encoding(false).GetString(data);
        static byte[] Encode(string text) => new UTF8Encoding(false).GetBytes(text);

        static bool IsMedia(string name) =>
            name.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase);

        static string BaseName(string path) => path.Substring(path.LastIndexOf('/') + 1);

        static string ReplaceFirst(string haystack, string needle, string replacement)
        {
            int i = haystack.IndexOf(needle, StringComparison.Ordinal);
            return i < 0 ? haystack : haystack.Substring(0, i) + replacement + haystack.Substring(i + needle.Length);
        }

        #endregion

        #region photo scan and repair

        /// <summary>Reads the document and reports distorted pictures. Writes nothing.</summary>
        public static PhotoScanResult ScanPhotos(string docxPath)
        {
            var parts = ReadParts(docxPath);
            string unused;
            return ExaminePhotos(parts, false, out unused);
        }

        /// <summary>
        /// Applies the repairs to <paramref name="srcPath"/> and writes the result
        /// to <paramref name="dstPath"/>. Every part other than word/document.xml
        /// is copied through untouched — media bytes included, so the repair is
        /// non-destructive and Word's crop tool can still drag a cropped photo
        /// back out.
        /// </summary>
        public static PhotoScanResult RepairPhotos(string srcPath, string dstPath)
        {
            var parts = ReadParts(srcPath);
            string repaired;
            var result = ExaminePhotos(parts, true, out repaired);

            if (result.AnyProblems)
            {
                var document = parts.First(p => p.Name == "word/document.xml");
                document.Data = Encode(repaired);
            }

            WriteToTempThenSwap(dstPath, temp => WriteParts(temp, parts));
            return result;
        }

        static PhotoScanResult ExaminePhotos(List<Part> parts, bool apply, out string newDocumentXml)
        {
            var result = new PhotoScanResult();

            var documentPart = parts.FirstOrDefault(p => p.Name == "word/document.xml");
            var relsPart = parts.FirstOrDefault(p => p.Name == "word/_rels/document.xml.rels");
            if (documentPart == null || relsPart == null)
                throw new InvalidDataException("This does not look like a Word document.");

            string document = Decode(documentPart.Data);

            // rId -> "media/image7.png"
            var relationships = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match m in RelTarget.Matches(Decode(relsPart.Data)))
                relationships[m.Groups[1].Value] = m.Groups[2].Value;

            // "media/image7.png" -> pixel size, bytes, and a content fingerprint.
            //
            // The fingerprint matters for the dialog: Word 2010 stores the same
            // photo twice, as image5.jpeg and image50.jpeg, so grouping findings by
            // part name would show her every photo twice over.
            var media = new Dictionary<string, MediaImage>(StringComparer.Ordinal);
            using (var sha1 = SHA1.Create())
            {
                foreach (var part in parts.Where(p => IsMedia(p.Name)))
                {
                    int w, h;
                    if (!ImageDimensions.TryRead(part.Data, out w, out h)) continue;

                    media["media/" + BaseName(part.Name)] = new MediaImage
                    {
                        Width = w,
                        Height = h,
                        Bytes = part.Data,
                        ContentKey = BitConverter.ToString(sha1.ComputeHash(part.Data))
                    };
                }
            }

            newDocumentXml = InlineBlock.Replace(document, match => RepairBlock(match.Value, relationships, media, apply, result));
            return result;
        }

        /// <summary>
        /// The repair, unchanged in substance from Scripts/fix_aspect_ratios.py.
        ///
        /// This runs over every wp:inline block, which also catches the second copy
        /// Word 2010 writes into each mc:Fallback branch — both placements of a
        /// picture must be corrected or they disagree.
        /// </summary>
        static string RepairBlock(string block,
                                  Dictionary<string, string> relationships,
                                  Dictionary<string, MediaImage> media,
                                  bool apply,
                                  PhotoScanResult result)
        {
            Match blip = BlipEmbed.Match(block);
            Match ext = AExt.Match(block);
            if (!blip.Success || !ext.Success) return block;

            string target;
            if (!relationships.TryGetValue(blip.Groups[1].Value, out target)) return block;

            MediaImage image;
            if (!media.TryGetValue(target, out image))
            {
                // A .wmf/.emf/.wdp part, or a format with no readable header.
                // Nothing to compare against, so it is left exactly as it is.
                return block;
            }

            int nativeWidth = image.Width, nativeHeight = image.Height;
            string name = BaseName(target);

            // Decorative chrome must not be corrected. The green header pills are
            // deliberately stretched to page width, and cropping them would slice
            // off their rounded end caps.
            double nativeRatio = (double)nativeWidth / nativeHeight;
            if (Math.Min(nativeWidth, nativeHeight) < MinShortEdge ||
                nativeRatio <= 1.0 / MaxNativeRatio || nativeRatio >= MaxNativeRatio)
                return block;

            // Any existing crop, as fractions of the native size.
            double cropLeft = 0, cropRight = 0, cropTop = 0, cropBottom = 0;
            Match existingCrop = SrcRect.Match(block);
            if (existingCrop.Success)
            {
                foreach (Match a in SrcRectAttr.Matches(existingCrop.Groups[1].Value))
                {
                    double v = int.Parse(a.Groups[2].Value, CultureInfo.InvariantCulture) / 100000.0;
                    switch (a.Groups[1].Value)
                    {
                        case "l": cropLeft = v; break;
                        case "r": cropRight = v; break;
                        case "t": cropTop = v; break;
                        case "b": cropBottom = v; break;
                    }
                }
            }

            double visibleWidth = nativeWidth * (1 - cropLeft - cropRight);
            double visibleHeight = nativeHeight * (1 - cropTop - cropBottom);
            long boxWidth = long.Parse(ext.Groups[1].Value, CultureInfo.InvariantCulture);
            long boxHeight = long.Parse(ext.Groups[2].Value, CultureInfo.InvariantCulture);
            if (visibleWidth <= 0 || visibleHeight <= 0 || boxWidth == 0 || boxHeight == 0) return block;

            double boxRatio = (double)boxWidth / boxHeight;
            double photoRatio = visibleWidth / visibleHeight;
            double distortion = boxRatio / photoRatio - 1;
            if (Math.Abs(distortion) <= Tolerance) return block;

            // What a crop would cost, as a fraction of the whole photo.
            double cropCost = photoRatio > boxRatio
                ? (visibleWidth - visibleHeight * boxRatio) / nativeWidth
                : (visibleHeight - visibleWidth / boxRatio) / nativeHeight;

            var finding = new PhotoFinding
            {
                MediaName = name,
                ContentKey = image.ContentKey,
                Distortion = distortion,
                NativeWidth = nativeWidth,
                NativeHeight = nativeHeight,
                ImageBytes = image.Bytes,
                BoxBeforeWidthIn = (double)boxWidth / EmuPerInch,
                BoxBeforeHeightIn = (double)boxHeight / EmuPerInch
            };

            if (cropCost <= CropLimit)
            {
                // Mild: centred crop. The box footprint is untouched, so nothing
                // in the floating layout can shift.
                if (photoRatio > boxRatio)
                {
                    cropLeft += cropCost / 2; cropRight += cropCost / 2; finding.Axis = "sides";
                }
                else
                {
                    cropTop += cropCost / 2; cropBottom += cropCost / 2; finding.Axis = "top/bottom";
                }

                finding.Repair = PhotoRepair.Crop;
                finding.CropCost = cropCost;
                finding.BoxAfterWidthIn = finding.BoxBeforeWidthIn;
                finding.BoxAfterHeightIn = finding.BoxBeforeHeightIn;

                if (apply)
                {
                    string replacement = string.Format(CultureInfo.InvariantCulture,
                        "<a:srcRect l=\"{0}\" t=\"{1}\" r=\"{2}\" b=\"{3}\"/>",
                        (long)Math.Round(cropLeft * 100000), (long)Math.Round(cropTop * 100000),
                        (long)Math.Round(cropRight * 100000), (long)Math.Round(cropBottom * 100000));

                    if (existingCrop.Success)
                    {
                        block = ReplaceFirst(block, existingCrop.Value, replacement);
                    }
                    else if (StretchTag.IsMatch(block))
                    {
                        block = StretchTag.Replace(block, replacement + "$0", 1);
                    }
                    else
                    {
                        // No srcRect to amend and no stretch to insert before. The
                        // markup is a shape this repair was not written for, so it
                        // is reported rather than guessed at.
                        result.Unreadable.Add(name);
                        return block;
                    }
                }
            }
            else
            {
                // Severe: a crop this deep removes real content — on one artwork it
                // was cutting off the artist's signature. Shrink the over-long box
                // dimension instead. A box only ever gets smaller, so it cannot
                // collide with anything around it.
                long newWidth, newHeight;
                if (distortion > 0) { newWidth = (long)Math.Round(boxHeight * photoRatio); newHeight = boxHeight; finding.Axis = "width"; }
                else { newWidth = boxWidth; newHeight = (long)Math.Round(boxWidth / photoRatio); finding.Axis = "height"; }

                if (newWidth > boxWidth || newHeight > boxHeight)
                    throw new InvalidOperationException("photo repair tried to grow a box — refusing");

                finding.Repair = PhotoRepair.Resize;
                finding.BoxAfterWidthIn = (double)newWidth / EmuPerInch;
                finding.BoxAfterHeightIn = (double)newHeight / EmuPerInch;

                if (apply)
                {
                    // wp:extent and a:ext are not always equal — Word leaves a small
                    // effect-extent delta on most pictures here. Scale each by the
                    // same factor rather than assuming they match, or wp:extent
                    // silently keeps the old size and the two disagree.
                    double scaleX = (double)newWidth / boxWidth, scaleY = (double)newHeight / boxHeight;

                    Match wpExtent = WpExtent.Match(block);
                    if (wpExtent.Success)
                    {
                        long ox = long.Parse(wpExtent.Groups[1].Value, CultureInfo.InvariantCulture);
                        long oy = long.Parse(wpExtent.Groups[2].Value, CultureInfo.InvariantCulture);
                        block = ReplaceFirst(block, wpExtent.Value, string.Format(CultureInfo.InvariantCulture,
                            "<wp:extent cx=\"{0}\" cy=\"{1}\"",
                            (long)Math.Round(ox * scaleX), (long)Math.Round(oy * scaleY)));
                    }

                    block = block.Replace(
                        string.Format(CultureInfo.InvariantCulture, "<a:ext cx=\"{0}\" cy=\"{1}\"", boxWidth, boxHeight),
                        string.Format(CultureInfo.InvariantCulture, "<a:ext cx=\"{0}\" cy=\"{1}\"", newWidth, newHeight));
                }
            }

            result.Findings.Add(finding);
            return block;
        }

        #endregion

        #region slimming

        /// <summary>
        /// Writes a slimmed copy of <paramref name="srcPath"/> to <paramref name="dstPath"/>.
        ///
        /// Two passes, with very different characters:
        ///
        ///   De-dupe + strip (always) — collapses byte-identical media parts onto
        ///   one, repoints the relationships, and removes the revision IDs and
        ///   stale web markup. Lossless. word/document.xml changes only by having
        ///   rsid attributes removed, which is asserted before the file is kept.
        ///
        ///   Downsample (on for publishing) — re-encodes photos at 1600px / q82.
        ///   Also cannot move anything, since geometry lives in document.xml and
        ///   that is not touched, but it does reduce stored photo resolution. 1600px
        ///   is still sharper than the page prints; a photo covering the full 5.6in
        ///   column reproduces at roughly 285 dpi.
        ///
        /// Re-encoding changes a part's extension, so the relationship targets and
        /// [Content_Types].xml are rewritten to match, and names are kept unique —
        /// see UniqueMediaName for why that last part is not theoretical.
        ///
        /// Embedded fonts are deliberately kept, so the script-font signature still
        /// renders on any machine.
        /// </summary>
        public static SlimResult Slim(string srcPath, string dstPath, bool downsample = false)
        {
            var parts = ReadParts(srcPath);
            var result = new SlimResult { BytesBefore = new FileInfo(srcPath).Length };

            byte[] originalDocument = parts.First(p => p.Name == "word/document.xml").Data;

            // Re-encoding an already-downsampled file would cost a little JPEG
            // quality for almost no size gain, so a matching stamp turns it off.
            if (downsample && string.Equals(SafeReadStamp(srcPath), Marker, StringComparison.Ordinal))
                downsample = false;

            var mediaParts = parts.Where(p => IsMedia(p.Name)).ToList();
            result.MediaPartsBefore = mediaParts.Count;

            // old part name -> canonical part name, collapsing identical content
            var canonical = new Dictionary<string, string>(StringComparer.Ordinal);
            var byHash = new Dictionary<string, string>(StringComparer.Ordinal);
            var keptBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var usedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Every media name already in the package. Re-encoding changes a part's
            // extension, and these documents genuinely contain stems that exist
            // under two of them — the master has image2, image3, image4, image6,
            // image9, image11 and image13 twice over. Without this, converting
            // image2.png would land on the existing image2.jpeg and one of the two
            // photos would be lost.
            var takenNames = new HashSet<string>(
                mediaParts.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            using (var sha1 = SHA1.Create())
            {
                foreach (var part in mediaParts)
                {
                    byte[] data = part.Data;
                    string name = part.Name;

                    if (downsample && Reduce != null)
                    {
                        string newExtension;
                        byte[] reduced = Reduce(data, Path.GetExtension(name),
                                                MaxEdge, JpegQuality, MinPhoto, out newExtension);
                        if (reduced != null)
                        {
                            data = reduced;
                            name = UniqueMediaName(
                                Path.ChangeExtension(part.Name, newExtension).Replace('\\', '/'),
                                part.Name, takenNames);
                        }
                    }

                    string hash = BitConverter.ToString(sha1.ComputeHash(data));
                    string existing;
                    if (byHash.TryGetValue(hash, out existing))
                    {
                        canonical[part.Name] = existing;
                        result.DuplicatesRemoved++;
                    }
                    else
                    {
                        byHash[hash] = name;
                        canonical[part.Name] = name;
                        keptBytes[name] = data;
                        usedExtensions.Add(Path.GetExtension(name).TrimStart('.'));
                    }
                }
            }

            // Relationships are written with bare file names, so the rewrite map is
            // keyed the same way.
            var renameByFileName = canonical.ToDictionary(
                kv => BaseName(kv.Key), kv => BaseName(kv.Value), StringComparer.Ordinal);

            var referenced = CollectReferencedMedia(parts);
            result.OrphanParts = mediaParts
                .Select(p => BaseName(p.Name))
                .Where(n => !referenced.Contains(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var output = new List<Part>();
            var written = new HashSet<string>(StringComparer.Ordinal);

            foreach (var part in parts)
            {
                if (IsMedia(part.Name))
                {
                    string keep = canonical[part.Name];
                    if (!written.Add(keep)) continue;           // a duplicate, already written
                    output.Add(new Part { Name = keep, Data = keptBytes[keep], LastWrite = part.LastWrite });
                    continue;
                }

                byte[] data = part.Data;

                if (part.Name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                    data = Encode(RewriteMediaTargets(Decode(data), renameByFileName));
                else if (part.Name == "[Content_Types].xml")
                    data = Encode(EnsureImageContentTypes(Decode(data), usedExtensions));
                else if (part.Name == "word/document.xml")
                    data = Encode(RsidAttr.Replace(Decode(data), string.Empty));
                else if (part.Name == "word/settings.xml")
                    data = Encode(RsidsBlock.Replace(Decode(data), string.Empty));
                else if (part.Name == "word/webSettings.xml")
                    data = Encode(DivsBlock.Replace(Decode(data), string.Empty));
                else if (part.Name == "docProps/core.xml")
                {
                    string text = Decode(data);
                    text = LastModifiedBy.Replace(text, string.Empty);
                    text = RevisionTag.Replace(text, "<cp:revision>1</cp:revision>");
                    data = Encode(text);
                }

                output.Add(new Part { Name = part.Name, Data = data, LastWrite = part.LastWrite });
            }

            AssertLayoutPreserved(originalDocument, output.First(p => p.Name == "word/document.xml").Data);
            AssertRelationshipsResolve(output);

            result.MediaPartsAfter = written.Count;

            WriteToTempThenSwap(dstPath, temp =>
            {
                WriteParts(temp, output);
                ZipComment.Write(temp, Marker);
            });

            result.BytesAfter = new FileInfo(dstPath).Length;

            // A document that has already been through this has nothing left to
            // give, and re-deflating its photos can cost a few bytes rather than
            // save them. Publishing must never hand back a bigger file than it was
            // given, so in that case the original is archived verbatim.
            if (result.BytesAfter >= result.BytesBefore)
            {
                File.Copy(srcPath, dstPath, true);
                result.BytesAfter = result.BytesBefore;
                result.DuplicatesRemoved = 0;
                result.MediaPartsAfter = result.MediaPartsBefore;
                result.KeptOriginal = true;
            }

            return result;
        }

        /// <summary>
        /// A media part name that is free, given everything already in the package
        /// and everything claimed by an earlier re-encode in this same pass. Keeping
        /// its own name is always allowed — that is not a collision with itself.
        /// </summary>
        static string UniqueMediaName(string candidate, string ownName, HashSet<string> taken)
        {
            if (string.Equals(candidate, ownName, StringComparison.OrdinalIgnoreCase))
                return candidate;

            if (taken.Add(candidate)) return candidate;

            string folder = candidate.Substring(0, candidate.LastIndexOf('/') + 1);
            string stem = Path.GetFileNameWithoutExtension(candidate);
            string extension = Path.GetExtension(candidate);

            for (int suffix = 1; ; suffix++)
            {
                string attempt = folder + stem + "-" + suffix.ToString(CultureInfo.InvariantCulture) + extension;
                if (taken.Add(attempt)) return attempt;
            }
        }

        static string SafeReadStamp(string path)
        {
            try { return ZipComment.Read(path); }
            catch { return null; }
        }

        static HashSet<string> CollectReferencedMedia(IEnumerable<Part> parts)
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts.Where(p => p.Name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
                foreach (Match m in MediaTarget.Matches(Decode(part.Data)))
                    referenced.Add(m.Groups[2].Value);
            return referenced;
        }

        static string RewriteMediaTargets(string rels, Dictionary<string, string> rename) =>
            MediaTarget.Replace(rels, m =>
            {
                string file = m.Groups[2].Value, mapped;
                return "Target=\"" + m.Groups[1].Value + (rename.TryGetValue(file, out mapped) ? mapped : file) + "\"";
            });

        static string EnsureImageContentTypes(string contentTypes, HashSet<string> extensions)
        {
            foreach (string extension in extensions.OrderBy(e => e, StringComparer.Ordinal))
            {
                string mime;
                switch (extension.ToLowerInvariant())
                {
                    case "jpeg": case "jpg": mime = "image/jpeg"; break;
                    case "png": mime = "image/png"; break;
                    default: continue;
                }

                if (contentTypes.IndexOf("Extension=\"" + extension + "\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                contentTypes = TypesOpen.Replace(contentTypes,
                    "$1<Default Extension=\"" + extension + "\" ContentType=\"" + mime + "\"/>", 1);
            }
            return contentTypes;
        }

        /// <summary>
        /// The layout-safety guarantee, enforced rather than merely documented.
        ///
        /// Slimming is allowed to remove revision-ID attributes from document.xml
        /// and nothing else. Every piece of layout geometry — every box extent,
        /// every crop, every anchor — lives in this part, so if it is unchanged
        /// once rsids are discounted, the published document cannot have moved.
        /// </summary>
        static void AssertLayoutPreserved(byte[] before, byte[] after)
        {
            string a = RsidAttr.Replace(Decode(before), string.Empty);
            string b = RsidAttr.Replace(Decode(after), string.Empty);
            if (!string.Equals(a, b, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Slimming altered the document body. The file has not been changed. " +
                    "This is a bug — please send this message to Kyle.");
        }

        static void AssertRelationshipsResolve(List<Part> parts)
        {
            var present = new HashSet<string>(
                parts.Where(p => IsMedia(p.Name)).Select(p => BaseName(p.Name)), StringComparer.OrdinalIgnoreCase);

            foreach (string target in CollectReferencedMedia(parts))
                if (!present.Contains(target))
                    throw new InvalidOperationException(
                        "Slimming left a picture reference pointing at nothing (" + target + "). " +
                        "The file has not been changed. This is a bug — please send this message to Kyle.");
        }

        #endregion

        /// <summary>
        /// Builds into a .tmp beside the target and swaps it in only on success, so
        /// an interrupted run can never leave a half-written document behind.
        /// </summary>
        static void WriteToTempThenSwap(string destination, Action<string> build)
        {
            string temp = destination + ".tmp";
            try
            {
                build(temp);
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(temp, destination);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* nothing useful to do */ }
                throw;
            }
        }
    }
}
