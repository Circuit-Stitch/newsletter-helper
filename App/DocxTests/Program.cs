using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MCAANewsletter;

namespace MCAANewsletter.Tests
{
    /// <summary>
    /// Checks the document surgery against the real newsletters.
    ///
    ///     dotnet run --project App/DocxTests -- &lt;newsletter-folder&gt; &lt;scratch-dir&gt;
    ///
    /// The newsletter folder is the one holding Template/, Drafts/ and Published/.
    /// It used to be this repository, and so used to default to the working
    /// directory; the newsletters now live outside the repo, so the path has to be
    /// given. Nothing there is written to — every output goes to the scratch
    /// directory, and Published/ is only ever read.
    /// </summary>
    static class Program
    {
        static string _root, _scratch;
        static int _failures, _checks;

        static int Main(string[] args)
        {
            _root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            _scratch = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "mcaa-tests");

            // Without this the first check dies on a raw DirectoryNotFoundException
            // naming some file deep inside the suite, which reads as a broken test
            // rather than a missing argument.
            if (!Directory.Exists(Path.Combine(_root, "Template")))
            {
                Console.Error.WriteLine(
                    "No \"Template\" folder under:  " + _root + "\n\n" +
                    "These tests run against the real newsletters, which live outside this\n" +
                    "repository. Pass the folder holding Template/, Drafts/ and Published/:\n\n" +
                    "    dotnet run --project App/DocxTests -- \"../MCAA Newsletters\"");
                return 2;
            }

            Directory.CreateDirectory(_scratch);

            Console.WriteLine("newsletters : " + _root);
            Console.WriteLine("scratch     : " + _scratch);

            ScanGoldenReference();
            RepairIsCorrectAndComplete();
            MastersScanClean();
            SlimUndoesWhatWordDoes();
            SlimPreservesEveryArchivedDocument();
            DownsamplingRewiresThePackage();
            ZipCommentRoundTrips();
            SettingsRefuseBadLayouts();

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? $"PASS — {_checks} checks"
                : $"FAIL — {_failures} of {_checks} checks failed");
            return _failures == 0 ? 0 : 1;
        }

        #region checks

        /// <summary>
        /// The port must reproduce the Python original's decisions exactly. These
        /// numbers come from Scripts/fix_aspect_ratios.py run over the same file.
        /// </summary>
        static void ScanGoldenReference()
        {
            Section("Photo scan reproduces the Python reference");

            var scan = DocxPackage.ScanPhotos(Draft);

            Console.WriteLine($"{"image",-16}{"was off",9}  {"repair",-8}{"detail",12}  {"box before",14}{"box after",14}");
            foreach (var f in scan.DistinctPhotos)
            {
                string detail = f.Repair == PhotoRepair.Crop
                    ? $"{f.CropCost * 100:0.0}% {f.Axis}" : f.Axis;
                string after = f.Repair == PhotoRepair.Resize
                    ? $"{f.BoxAfterWidthIn:0.00}x{f.BoxAfterHeightIn:0.00}in" : "unchanged";
                Console.WriteLine($"{f.MediaName,-16}{f.Distortion * 100,8:+0.0;-0.0}%  " +
                                  $"{f.Repair.ToString().ToLowerInvariant(),-8}{detail,12}  " +
                                  $"{$"{f.BoxBeforeWidthIn:0.00}x{f.BoxBeforeHeightIn:0.00}in",14}{after,14}");
            }

            Check("24 distorted placements", scan.PlacementCount == 24, scan.PlacementCount.ToString());
            Check("12 distinct photos once duplicates are grouped",
                  scan.DistinctPhotos.Count == 12, scan.DistinctPhotos.Count.ToString());
            Check("nothing unreadable", scan.Unreadable.Count == 0, string.Join(",", scan.Unreadable));

            var worst = scan.DistinctPhotos.First();
            Check("worst is +39.8%", Math.Abs(worst.Distortion * 100 - 39.8) < 0.1,
                  (worst.Distortion * 100).ToString("0.0", CultureInfo.InvariantCulture));
            Check("worst is repaired by resizing the width",
                  worst.Repair == PhotoRepair.Resize && worst.Axis == "width", worst.Axis);
            Check("worst box 3.24x2.36in becomes 2.32x2.36in",
                  Near(worst.BoxBeforeWidthIn, 3.24) && Near(worst.BoxBeforeHeightIn, 2.36) &&
                  Near(worst.BoxAfterWidthIn, 2.32) && Near(worst.BoxAfterHeightIn, 2.36),
                  $"{worst.BoxBeforeWidthIn:0.00}x{worst.BoxBeforeHeightIn:0.00} -> " +
                  $"{worst.BoxAfterWidthIn:0.00}x{worst.BoxAfterHeightIn:0.00}");

            Check("5 photos resized, 7 cropped",
                  scan.DistinctPhotos.Count(f => f.Repair == PhotoRepair.Resize) == 5 &&
                  scan.DistinctPhotos.Count(f => f.Repair == PhotoRepair.Crop) == 7,
                  $"{scan.DistinctPhotos.Count(f => f.Repair == PhotoRepair.Resize)} resized, " +
                  $"{scan.DistinctPhotos.Count(f => f.Repair == PhotoRepair.Crop)} cropped");

            // The green header pills are stretched to page width on purpose.
            var names = new HashSet<string>(scan.Findings.Select(f => f.MediaName));
            Check("decorative chrome excluded",
                  !names.Contains("image8.png") && !names.Contains("image12.png"), "");
        }

        static void RepairIsCorrectAndComplete()
        {
            Section("Repair fixes everything and moves nothing else");

            string repaired = Path.Combine(_scratch, "repaired.docx");
            DocxPackage.RepairPhotos(Draft, repaired);

            var after = DocxPackage.ScanPhotos(repaired);
            Check("no distortion remains", !after.AnyProblems,
                  after.PlacementCount + " left");

            var before = ReadParts(Draft);
            var now = ReadParts(repaired);

            Check("only word/document.xml changed",
                  before.Keys.All(k => k == "word/document.xml" || Same(before[k], now[k])) &&
                  before.Count == now.Count, "");

            Check("every photo's bytes are untouched",
                  before.Keys.Where(k => k.StartsWith("word/media/"))
                            .All(k => Same(before[k], now[k])), "");

            string textBefore = VisibleText(before["word/document.xml"]);
            string textAfter = VisibleText(now["word/document.xml"]);
            Check("the words on the page are identical", textBefore == textAfter,
                  $"{textBefore.Length} vs {textAfter.Length} characters");

            var boxesBefore = Extents(before["word/document.xml"]);
            var boxesAfter = Extents(now["word/document.xml"]);
            Check("same number of picture boxes", boxesBefore.Count == boxesAfter.Count,
                  $"{boxesBefore.Count} vs {boxesAfter.Count}");

            bool noneGrew = boxesBefore.Count == boxesAfter.Count &&
                            boxesBefore.Zip(boxesAfter, (b, a) => a.Item1 <= b.Item1 && a.Item2 <= b.Item2).All(x => x);
            Check("no box grew", noneGrew, "");

            int shrunk = boxesBefore.Zip(boxesAfter, (b, a) => (a.Item1 < b.Item1 || a.Item2 < b.Item2) ? 1 : 0).Sum();
            Console.WriteLine($"   {shrunk} boxes reduced, {boxesBefore.Count - shrunk} left exactly as they were");
        }

        static void MastersScanClean()
        {
            Section("Both masters are already repaired");

            string onDisk = Path.Combine(_root, "Template", "MCAA-Newsletter-MASTER.docx");
            if (File.Exists(onDisk))
            {
                var scan = DocxPackage.ScanPhotos(onDisk);
                Check("master on disk has no distorted photos", !scan.AnyProblems,
                      scan.PlacementCount + " found");
            }
        }

        /// <summary>
        /// The case that happens every month. The draft is a document Word has
        /// saved, so it carries the Compatibility Mode 14 duplication: 42 media
        /// parts holding 22 distinct images.
        /// </summary>
        static void SlimUndoesWhatWordDoes()
        {
            Section("Slimming undoes the duplication Word adds on every save");

            string output = Path.Combine(_scratch, "slimmed-draft.docx");
            var result = DocxPackage.Slim(Draft, output);

            Console.WriteLine($"   {result.BytesBefore / 1048576.0:0.00}M -> {result.BytesAfter / 1048576.0:0.00}M " +
                              $"({result.PercentSaved:0}% smaller)");
            Console.WriteLine($"   {result.MediaPartsBefore} media parts -> {result.MediaPartsAfter}, " +
                              $"{result.DuplicatesRemoved} duplicates removed");

            Check("the original was actually improved on", !result.KeptOriginal, "kept original");
            Check("20 duplicate image parts removed", result.DuplicatesRemoved == 20,
                  result.DuplicatesRemoved.ToString());
            Check("42 media parts become 22",
                  result.MediaPartsBefore == 42 && result.MediaPartsAfter == 22,
                  $"{result.MediaPartsBefore} -> {result.MediaPartsAfter}");
            Check("the file gets meaningfully smaller", result.PercentSaved > 15,
                  $"{result.PercentSaved:0.0}%");

            var before = ReadParts(Draft);
            var after = ReadParts(output);

            Check("revision IDs are gone from the body",
                  Regex.Matches(Encoding.UTF8.GetString(after["word/document.xml"]), @"w:rsidR=").Count == 0,
                  Regex.Matches(Encoding.UTF8.GetString(after["word/document.xml"]), @"w:rsidR=").Count.ToString());

            Check("revision IDs are gone from settings",
                  !Encoding.UTF8.GetString(after["word/settings.xml"]).Contains("<w:rsids>"), "");

            Check("the words on the page survive slimming",
                  VisibleText(before["word/document.xml"]) == VisibleText(after["word/document.xml"]), "");

            Check("every picture box is exactly where it was",
                  Extents(before["word/document.xml"]).SequenceEqual(Extents(after["word/document.xml"])), "");

            Check("embedded fonts are kept",
                  after.Keys.Count(k => k.StartsWith("word/fonts/")) ==
                  before.Keys.Count(k => k.StartsWith("word/fonts/")),
                  $"{before.Keys.Count(k => k.StartsWith("word/fonts/"))} -> " +
                  $"{after.Keys.Count(k => k.StartsWith("word/fonts/"))}");

            // Every rId the body uses must still land on a part that exists.
            var rels = Encoding.UTF8.GetString(after["word/_rels/document.xml.rels"]);
            var targets = Regex.Matches(rels, @"Target=""(?:\.\./)?media/([^""]+)""")
                               .Cast<Match>().Select(m => m.Groups[1].Value).Distinct();
            var present = new HashSet<string>(
                after.Keys.Where(k => k.StartsWith("word/media/")).Select(k => k.Substring("word/media/".Length)),
                StringComparer.OrdinalIgnoreCase);
            Check("every picture reference still resolves", targets.All(present.Contains), "");

            try { File.Delete(output); } catch { }
        }

        static void SlimPreservesEveryArchivedDocument()
        {
            Section("Slimming preserves the body of every archived document");

            string published = Path.Combine(_root, "Published");
            if (!Directory.Exists(published)) { Console.WriteLine("   no Published/ folder"); return; }

            var files = Directory.GetFiles(published, "*.docx")
                                 .Where(f => !Path.GetFileName(f).StartsWith("~$"))
                                 .OrderBy(f => f).ToList();

            long totalBefore = 0, totalAfter = 0;
            int ok = 0, failed = 0, orphans = 0;

            foreach (string file in files)
            {
                string output = Path.Combine(_scratch, "slim-" + Path.GetFileName(file));
                try
                {
                    // Slim asserts internally that the body is unchanged and that
                    // every picture reference still resolves; a throw here IS the
                    // failure, which is why there is so little to assert outside it.
                    var result = DocxPackage.Slim(file, output);
                    totalBefore += result.BytesBefore;
                    totalAfter += result.BytesAfter;
                    orphans += result.OrphanParts.Count;
                    ok++;

                    Console.WriteLine($"   {Path.GetFileName(file),-46}" +
                                      $"{result.BytesBefore / 1048576.0,7:0.0}M ->{result.BytesAfter / 1048576.0,7:0.0}M" +
                                      $"{result.PercentSaved,6:0}%  " +
                                      (result.KeptOriginal
                                          ? "already clean, kept as-is"
                                          : $"{result.DuplicatesRemoved} dup") +
                                      (result.OrphanParts.Count > 0 ? $", {result.OrphanParts.Count} orphan" : ""));
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"   {Path.GetFileName(file),-46}FAILED: {ex.Message}");
                }
                finally
                {
                    try { if (File.Exists(output)) File.Delete(output); } catch { }
                }
            }

            Check($"all {files.Count} archived documents slim without altering the body",
                  failed == 0, failed + " failed");
            Console.WriteLine($"   total {totalBefore / 1048576.0:0.0}M -> {totalAfter / 1048576.0:0.0}M " +
                              $"({100 * (1 - (double)totalAfter / Math.Max(totalBefore, 1)):0}% smaller), " +
                              $"{orphans} orphan parts seen across {ok} documents");
        }

        /// <summary>
        /// Downsampling changes a part's extension, which means rewriting the
        /// relationship targets and [Content_Types].xml, and keeping names unique
        /// where a stem already exists under two extensions. That plumbing is where
        /// a bug would silently lose a photo, and it is testable without an image
        /// codec — so it is tested here, on every real document, with a stub
        /// re-encoder standing in for GDI+.
        ///
        /// What this does NOT cover is the encoding itself: resampling quality and
        /// alpha detection need System.Drawing and therefore a Windows run.
        /// </summary>
        static void DownsamplingRewiresThePackage()
        {
            Section("Downsampling rewires the package without losing a picture");

            var documents = new List<string> { Draft };
            string master = Path.Combine(_root, "Template", "MCAA-Newsletter-MASTER.docx");
            if (File.Exists(master)) documents.Add(master);
            string published = Path.Combine(_root, "Published");
            if (Directory.Exists(published))
                documents.AddRange(Directory.GetFiles(published, "*.docx")
                                            .Where(f => !Path.GetFileName(f).StartsWith("~$"))
                                            .OrderBy(f => f));

            // Whether any real document happens to hold one media stem under two
            // convertible extensions is an accident of how Word last saved it —
            // the archive here has none, so the collision guard would go
            // untested. This makes one on purpose, so the guard is proven from
            // any checkout rather than only on the machine that had a
            // Word-bloated master lying around.
            string fixture = BuildCollisionFixture(documents.First(File.Exists),
                                                   Path.Combine(_scratch, "collision-fixture.docx"));
            if (fixture != null) documents.Add(fixture);
            Console.WriteLine(fixture != null
                ? "   built a colliding fixture: " + Path.GetFileName(fixture)
                : "   could not build a colliding fixture from this corpus");

            int collisionsResolved = 0, converted = 0, broken = 0;

            try
            {
                foreach (string document in documents)
                {
                    // Stand-in for the real re-encoder: hands back a genuine JPEG
                    // lifted from the same document, so the output is a valid image
                    // and every convertible part changes extension.
                    byte[] stand_in = SmallestJpeg(document);
                    if (stand_in == null) continue;

                    // Each reduced part must come out DISTINCT, or the de-dupe
                    // collapses them all onto one and no name ever collides — which
                    // would leave the guard untested while looking like it passed.
                    // Trailing bytes after the JPEG end marker are ignored by
                    // readers, so this stays a valid image.
                    int nonce = 0;
                    DocxPackage.Reduce = (byte[] raw, string extension, int maxEdge, int quality,
                                          int minPhoto, out string newExtension) =>
                    {
                        newExtension = ".jpeg";
                        string e = (extension ?? "").ToLowerInvariant();
                        bool convertible = e == ".png" || e == ".jpg" || e == ".jpeg" ||
                                           e == ".bmp" || e == ".tif" || e == ".tiff";
                        if (!convertible || raw.Length <= stand_in.Length + 64) return null;

                        var distinct = new byte[stand_in.Length + (nonce++ % 61) + 1];
                        Buffer.BlockCopy(stand_in, 0, distinct, 0, stand_in.Length);
                        return distinct;
                    };

                    string output = Path.Combine(_scratch, "down-" + Path.GetFileName(document));
                    try
                    {
                        var before = ReadParts(document);
                        DocxPackage.Slim(document, output, downsample: true);
                        var after = ReadParts(output);

                        // Nothing in the body may move, extensions or not.
                        if (VisibleText(before["word/document.xml"]) != VisibleText(after["word/document.xml"]))
                        { broken++; Console.WriteLine($"   text changed in {Path.GetFileName(document)}"); }

                        if (!Extents(before["word/document.xml"]).SequenceEqual(Extents(after["word/document.xml"])))
                        { broken++; Console.WriteLine($"   boxes moved in {Path.GetFileName(document)}"); }

                        // Every reference must land on a part that exists.
                        var present = new HashSet<string>(
                            after.Keys.Where(k => k.StartsWith("word/media/"))
                                      .Select(k => k.Substring("word/media/".Length)),
                            StringComparer.OrdinalIgnoreCase);

                        foreach (var key in after.Keys.Where(k => k.EndsWith(".rels")))
                            foreach (Match m in Regex.Matches(Encoding.UTF8.GetString(after[key]),
                                                              @"Target=""(?:\.\./)?media/([^""]+)"""))
                                if (!present.Contains(m.Groups[1].Value))
                                {
                                    broken++;
                                    Console.WriteLine($"   dangling {m.Groups[1].Value} in {Path.GetFileName(document)}");
                                }

                        // Content types must cover every extension actually present.
                        string types = Encoding.UTF8.GetString(after["[Content_Types].xml"]);
                        foreach (string ext in present.Select(p => Path.GetExtension(p).TrimStart('.')).Distinct())
                            if (types.IndexOf("Extension=\"" + ext + "\"", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                broken++;
                                Console.WriteLine($"   {ext} uncovered in {Path.GetFileName(document)}");
                            }

                        collisionsResolved += present.Count(p => Regex.IsMatch(p, @"-\d+\.jpeg$"));
                        converted += present.Count(p => p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
                    }
                    catch (Exception ex)
                    {
                        broken++;
                        Console.WriteLine($"   {Path.GetFileName(document)} FAILED: {ex.Message}");
                    }
                    finally
                    {
                        try { if (File.Exists(output)) File.Delete(output); } catch { }
                    }
                }
            }
            finally { DocxPackage.Reduce = null; }

            Console.WriteLine($"   {documents.Count} documents, {converted} parts ended as .jpeg, " +
                              $"{collisionsResolved} name collisions resolved");

            Check("no document was broken by re-encoding", broken == 0, broken + " problems");
            Check("the name-collision guard was actually exercised", collisionsResolved > 0,
                  "no collisions occurred, so the guard is unproven");
        }

        /// <summary>
        /// Copies a document, renaming one media part so its stem already exists
        /// under another convertible extension — image8.png becomes image2.png
        /// while image2.jpeg is still there. Re-encoding image2.png then wants to
        /// write image2.jpeg, which is taken, so UniqueMediaName has to step in.
        ///
        /// Returns null if the source has no usable pair.
        /// </summary>
        static string BuildCollisionFixture(string source, string destination)
        {
            var parts = ReadParts(source);

            var media = parts.Keys.Where(k => k.StartsWith("word/media/")).ToList();
            var stems = new HashSet<string>(
                media.Select(k => Path.GetFileNameWithoutExtension(k)), StringComparer.OrdinalIgnoreCase);

            // The part to rename: convertible, and not itself a .jpeg.
            string donor = media.FirstOrDefault(k =>
            {
                string e = Path.GetExtension(k).ToLowerInvariant();
                return e == ".png" || e == ".bmp" || e == ".tif" || e == ".tiff";
            });
            // The stem to collide with: something already stored as .jpeg.
            string victim = media.FirstOrDefault(k =>
                Path.GetExtension(k).Equals(".jpeg", StringComparison.OrdinalIgnoreCase));
            if (donor == null || victim == null) return null;

            string newStem = Path.GetFileNameWithoutExtension(victim);
            string newName = "word/media/" + newStem + Path.GetExtension(donor);
            if (parts.ContainsKey(newName)) return null;

            string oldTarget = "media/" + Path.GetFileName(donor);
            string newTarget = "media/" + newStem + Path.GetExtension(donor);

            // The stand-in re-encoder below only rewrites parts bigger than the
            // smallest JPEG in the document, so a small donor would be skipped and
            // never collide. Padding past the largest part guarantees it is
            // converted whichever document this is built from; PNG readers stop at
            // IEND, so the trailing bytes change nothing that matters.
            int floor = media.Max(k => parts[k].Length) + 1024;
            byte[] donorBytes = parts[donor];
            if (donorBytes.Length < floor) Array.Resize(ref donorBytes, floor);

            using (var zip = new ZipArchive(File.Create(destination), ZipArchiveMode.Create))
                foreach (var part in parts)
                {
                    string name = part.Key == donor ? newName : part.Key;
                    byte[] data = part.Key == donor ? donorBytes : part.Value;

                    // Every relationship pointing at the donor has to follow it.
                    if (name.EndsWith(".rels", StringComparison.Ordinal))
                    {
                        string xml = Encoding.UTF8.GetString(data);
                        if (xml.Contains(oldTarget))
                            data = Encoding.UTF8.GetBytes(xml.Replace(oldTarget, newTarget));
                    }

                    var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                    using (var stream = entry.Open()) stream.Write(data, 0, data.Length);
                }

            return destination;
        }

        /// <summary>Smallest real JPEG in the package, used as a stand-in re-encode.</summary>
        static byte[] SmallestJpeg(string path)
        {
            var parts = ReadParts(path);
            return parts.Where(p => p.Key.StartsWith("word/media/") &&
                                    p.Value.Length > 2 && p.Value[0] == 0xFF && p.Value[1] == 0xD8)
                        .OrderBy(p => p.Value.Length)
                        .Select(p => p.Value)
                        .FirstOrDefault();
        }

        static void ZipCommentRoundTrips()
        {
            Section("Processed-stamp survives a write and is readable back");

            string file = Path.Combine(_scratch, "stamp.docx");
            DocxPackage.Slim(Draft, file);

            string stamp = ZipComment.Read(file);
            Check("stamp is written and reads back",
                  !string.IsNullOrEmpty(stamp) && stamp.StartsWith("MCAA-shrink"), stamp ?? "(none)");

            // A stamped file must still be a valid package.
            using (var zip = ZipFile.OpenRead(file))
                Check("stamped file is still a readable package",
                      zip.Entries.Any(e => e.FullName == "word/document.xml"), "");

            try { File.Delete(file); } catch { }
        }

        /// <summary>
        /// The settings screen is the only thing standing between a mistyped
        /// layout and the app doing work in the wrong place, so the rules it
        /// enforces are checked here rather than by clicking around the dialog.
        ///
        /// The repository itself is a valid newsletter folder, which makes it the
        /// natural fixture for the cases that are supposed to pass.
        /// </summary>
        static void SettingsRefuseBadLayouts()
        {
            Section("Settings accept a real folder and refuse a broken one");

            Settings Good() => new Settings { Root = _root };

            Check("the repository is accepted as it stands",
                  Good().Problem() == null, Good().Problem());
            Check("and its master is found",
                  Good().Warning() == null, Good().Warning());

            Check("a folder that does not exist is refused",
                  new Settings { Root = Path.Combine(_root, "no-such-folder") }.Problem() != null, "");

            var noTemplate = Good();
            noTemplate.TemplateFolder = "Nowhere";
            Check("a missing Template folder is refused", noTemplate.Problem() != null, "");

            // The one rule that prevents losing work: without both tokens every
            // month resolves to the same file name and each issue overwrites the
            // one before it.
            foreach (string pattern in new[] { "Newsletter", "{year} Newsletter", "{month} Newsletter", "" })
            {
                var s = Good();
                s.IssuePattern = pattern;
                Check("issue name \"" + pattern + "\" is refused", s.Problem() != null, "");
            }

            // These are single names joined onto the root, not paths. A slash or a
            // ".." would silently move the whole operation elsewhere on the disk.
            foreach (string name in new[] { "..", ".", "", "  ", "Sub\\Folder", "Sub/Folder", "a:b" })
            {
                var s = Good();
                s.DraftsFolder = name;
                Check("drafts folder \"" + name + "\" is refused", s.Problem() != null, "");
            }

            var missingMaster = Good();
            missingMaster.MasterFileName = "Not-Here.docx";
            Check("a missing master warns but does not block",
                  missingMaster.Problem() == null && missingMaster.Warning() != null, "");

            // Month names must not follow the machine's locale, or the archive
            // ends up with two spellings of the same month.
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                Check("month names stay English under another locale",
                      Settings.Expand(Settings.DefaultIssuePattern, 2026, 8) ==
                          "2026 August MCAA Newsletter",
                      Settings.Expand(Settings.DefaultIssuePattern, 2026, 8));
            }
            finally { CultureInfo.CurrentCulture = previous; }

            Check("the year is always four digits",
                  Settings.Expand("{year}", 26, 8) == "0026", Settings.Expand("{year}", 26, 8));

            var issue = new IssueName(Good(), 2026, 8);
            Check("paths still read as they always did",
                  issue.DraftDocx.EndsWith(Path.Combine("Drafts", "2026 August MCAA Newsletter-DRAFT.docx")) &&
                  issue.PublishedPdf.EndsWith(Path.Combine("Published", "2026 August MCAA Newsletter.pdf")),
                  issue.DraftDocx);

            var renamed = Good();
            renamed.MasterFileName = "Master.docx";
            Check("the previous-master copy follows the master's name",
                  Path.GetFileName(renamed.PreviousMasterPath) == "Master (previous).docx",
                  Path.GetFileName(renamed.PreviousMasterPath));

            // A relative path would otherwise resolve against whatever directory
            // the program was started in, so the same saved setting could mean two
            // different folders on two different launches.
            var relative = new Settings { Root = "." };
            Check("a typed folder is stored as a full path",
                  Path.IsPathRooted(relative.Root), relative.Root);
            Check("and surrounding spaces are dropped",
                  new Settings { Root = "  " + _root + "  " }.Root == Path.GetFullPath(_root), "");

            // AutoDetect is only ever allowed to guess a folder the app would also
            // accept, so it walks up from a build output directory to the root.
            Settings found = Settings.AutoDetect(Path.Combine(_root, "App", "DocxTests"));
            Check("auto-detection walks up to the newsletter folder",
                  found != null && Path.GetFullPath(found.Root) == Path.GetFullPath(_root),
                  found == null ? "(not found)" : found.Root);
        }

        #endregion

        #region helpers

        static string Draft => Path.Combine(_root, "Drafts", "MCAA-Newsletter-DRAFT.docx");

        static bool Near(double a, double b) => Math.Abs(a - b) < 0.005;

        static bool Same(byte[] a, byte[] b) =>
            a.Length == b.Length && a.SequenceEqual(b);

        static Dictionary<string, byte[]> ReadParts(string path)
        {
            var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var zip = ZipFile.OpenRead(path))
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) && entry.Length == 0) continue;
                    using (var s = entry.Open())
                    using (var m = new MemoryStream())
                    {
                        s.CopyTo(m);
                        parts[entry.FullName] = m.ToArray();
                    }
                }
            return parts;
        }

        /// <summary>Everything between the tags — what actually appears on the page.</summary>
        static string VisibleText(byte[] documentXml) =>
            Regex.Replace(Encoding.UTF8.GetString(documentXml), "<[^>]*>", "");

        /// <summary>Every picture box extent, in document order.</summary>
        static List<Tuple<long, long>> Extents(byte[] documentXml)
        {
            var list = new List<Tuple<long, long>>();
            foreach (Match m in Regex.Matches(Encoding.UTF8.GetString(documentXml),
                                              @"<a:ext cx=""(\d+)"" cy=""(\d+)"""))
                list.Add(Tuple.Create(long.Parse(m.Groups[1].Value), long.Parse(m.Groups[2].Value)));
            return list;
        }

        static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("== " + title + " ==");
        }

        static void Check(string what, bool passed, string detail)
        {
            _checks++;
            if (!passed) _failures++;
            Console.WriteLine($"   [{(passed ? "ok" : "FAIL")}] {what}" +
                              (passed || string.IsNullOrEmpty(detail) ? "" : "  -> " + detail));
        }

        #endregion
    }
}
