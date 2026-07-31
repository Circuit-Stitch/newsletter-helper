using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MCAANewsletter
{
    /// <summary>
    /// Raised for anything the user can act on themselves. The message is shown
    /// to her verbatim, so it has to read like a sentence a person would say.
    /// </summary>
    public sealed class UserFixableException : Exception
    {
        public UserFixableException(string message) : base(message) { }
    }

    /// <summary>
    /// PDF export, driven through Word by late binding.
    ///
    /// Late binding rather than a referenced interop assembly: the app is built on
    /// a machine that does not have Office 2010 installed, and a PIA reference
    /// would pin it to whichever Word version the build machine happened to have.
    /// `dynamic` asks the installed Word whatever it turns out to be.
    ///
    /// The document is opened read-only and closed without saving. That matters
    /// more than it looks: a Word save on this document re-duplicates every image
    /// and restores the revision IDs, which is exactly the bloat the rest of the
    /// app exists to remove.
    /// </summary>
    public static class WordExport
    {
        const int WdExportFormatPdf = 17;
        const int WdExportOptimizeForPrint = 0;
        const int WdDoNotSaveChanges = 0;
        const int WdAlertsNone = 0;
        const int WdExportAllDocument = 0;
        const int WdExportDocumentContent = 0;
        const int WdExportCreateNoBookmarks = 0;

        public static bool IsWordInstalled => Type.GetTypeFromProgID("Word.Application") != null;

        /// <summary>
        /// Whether something currently has the document open. Two checks, because
        /// neither is sufficient alone: Word signals ownership with a hidden "~$"
        /// file but does not necessarily hold an exclusive handle, and a different
        /// program might hold the handle without leaving an owner file.
        /// </summary>
        public static bool IsOpenElsewhere(string path)
        {
            if (!File.Exists(path)) return false;
            return File.Exists(OwnerFilePath(path)) || IsLocked(path);
        }

        /// <summary>
        /// An owner file with nothing actually holding the document — what Word
        /// leaves behind when it crashes, or when the drive is pulled mid-edit.
        ///
        /// Worth telling apart from a genuine open: on its own the owner file
        /// makes <see cref="IsOpenElsewhere"/> true forever, so the app would go
        /// on saying "close it in Word" to someone who has already closed Word
        /// and has no other way to clear it.
        /// </summary>
        public static bool HasStaleOwnerFile(string path)
        {
            return File.Exists(path) && File.Exists(OwnerFilePath(path)) && !IsLocked(path);
        }

        /// <summary>Deletes the leftover owner file. True if it is gone afterwards.</summary>
        public static bool RemoveOwnerFile(string path)
        {
            string owner = OwnerFilePath(path);
            try
            {
                if (File.Exists(owner))
                {
                    // Word marks it hidden, and File.Delete will not remove a
                    // read-only file.
                    File.SetAttributes(owner, FileAttributes.Normal);
                    File.Delete(owner);
                }
                return !File.Exists(owner);
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        static bool IsLocked(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return false;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        /// <summary>
        /// Word's owner file: "~$" followed by the name with its first two
        /// characters removed, in the same folder.
        /// </summary>
        static string OwnerFilePath(string path)
        {
            string name = Path.GetFileName(path);
            string stem = name.Length > 2 ? name.Substring(2) : name;
            return Path.Combine(Path.GetDirectoryName(path) ?? ".", "~$" + stem);
        }

        public static void ExportPdf(string docxPath, string pdfPath)
        {
            if (!File.Exists(docxPath))
                throw new UserFixableException("The newsletter file could not be found:\n\n" + docxPath);

            Type wordType = Type.GetTypeFromProgID("Word.Application");
            if (wordType == null)
                throw new UserFixableException(
                    "Microsoft Word does not seem to be installed on this computer, " +
                    "so the PDF cannot be made here.");

            dynamic word = null;
            dynamic document = null;

            // Held rather than reached through in one expression: Documents is a
            // COM object in its own right, and "word.Documents.Open(...)" leaves a
            // live reference on Word that nothing is able to release afterwards.
            object documents = null;

            try
            {
                try
                {
                    word = Activator.CreateInstance(wordType);
                }
                catch (Exception ex)
                {
                    throw new UserFixableException(
                        "Word could not be started, so the PDF was not made.\n\n" +
                        "Try opening Word on its own first, then click this button again.\n\n" +
                        "(" + ex.Message + ")");
                }

                word.Visible = false;
                word.DisplayAlerts = WdAlertsNone;
                word.ScreenUpdating = false;

                documents = word.Documents;
                document = ((dynamic)documents).Open(
                    FileName: docxPath,
                    ConfirmConversions: false,
                    ReadOnly: true,
                    AddToRecentFiles: false,
                    Revert: true,
                    Visible: false);

                // Delete any stale output first: Word will not overwrite a PDF that
                // something else has open, and the error it raises is unhelpful.
                if (File.Exists(pdfPath))
                {
                    try { File.Delete(pdfPath); }
                    catch (IOException)
                    {
                        throw new UserFixableException(
                            "The PDF from last time is still open. Please close it, then click this button again.");
                    }
                }

                document.ExportAsFixedFormat(
                    OutputFileName: pdfPath,
                    ExportFormat: WdExportFormatPdf,
                    OpenAfterExport: false,
                    OptimizeFor: WdExportOptimizeForPrint,
                    Range: WdExportAllDocument,
                    Item: WdExportDocumentContent,
                    IncludeDocProps: true,
                    KeepIRM: false,
                    CreateBookmarks: WdExportCreateNoBookmarks,
                    DocStructureTags: true,
                    BitmapMissingFonts: true,
                    UseISO19005_1: false);

                if (!File.Exists(pdfPath))
                    throw new UserFixableException(
                        "Word finished without reporting a problem, but no PDF appeared. " +
                        "Please try again, and tell Kyle if it happens twice.");
            }
            catch (COMException ex)
            {
                throw new UserFixableException(
                    "Word could not make the PDF.\n\n" +
                    "Please make sure the newsletter is closed in Word, then try again.\n\n" +
                    "(" + ex.Message + ")");
            }
            finally
            {
                // Orphaned WINWORD.EXE processes are the classic failure here, so
                // every one of these runs regardless of what went wrong above.
                CloseQuietly(document, documents, word);
            }
        }

        /// <summary>
        /// The two plain-object locals on the first lines are load-bearing, not
        /// tidying.
        ///
        /// Anything done with a `dynamic` value — including passing it as an
        /// argument — compiles to a DLR call site, and binding one against a COM
        /// instance builds an InstanceRestriction, which takes a WeakReference
        /// over the RCW. Once Close has destroyed the document and Quit has ended
        /// the WINWORD process, taking that weak reference fails: 0x80010114 for
        /// the document, 0x800706BE for the application. The release therefore
        /// never ran at all, and the bare catch meant nothing ever said so.
        ///
        /// Capturing the references while the objects are still alive makes the
        /// releases ordinary static calls. ReleaseComObject is happy with a
        /// disconnected RCW — it only decrements the wrapper's count and never
        /// calls into the object.
        /// </summary>
        static void CloseQuietly(dynamic document, object documents, dynamic word)
        {
            object documentRef = document;
            object wordRef = word;

            if (documentRef != null)
            {
                try { document.Close(SaveChanges: WdDoNotSaveChanges); } catch { }
                Release(documentRef);
            }

            if (documents != null) Release(documents);

            if (wordRef != null)
            {
                try { word.Quit(SaveChanges: WdDoNotSaveChanges); } catch { }
                Release(wordRef);
            }
        }

        static void Release(object comObject)
        {
            try { Marshal.ReleaseComObject(comObject); }
            catch (ArgumentException) { }       // not an RCW; nothing to release
            catch (COMException) { }
        }
    }
}
