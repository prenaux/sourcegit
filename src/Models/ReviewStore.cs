using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace SourceGit.Models
{
    public sealed class ReviewStore
    {
        private const int SchemaVersion = 2;
        private const int LegacySchemaVersion = 1;
        private const long MaxXmlCharacters = 128 * 1024 * 1024;
        private static readonly UTF8Encoding s_encoding = new(false, true);
        private readonly string _root;
        private readonly SemaphoreSlim _writeLock = new(1);
        private readonly HashSet<string> _legacyReviews = [];

        public List<string> LoadErrors { get; } = [];

        public ReviewStore(string repositoryPath)
        {
            _root = Path.GetFullPath(Path.Combine(repositoryPath, Review.DirectoryName));
        }

        public List<Review> LoadReviews()
        {
            LoadErrors.Clear();
            var result = new List<Review>();
            if (!Directory.Exists(_root))
                return result;
            EnsureNoLinks(_root);
            foreach (var directory in Directory.EnumerateDirectories(_root))
            {
                var id = Path.GetFileName(directory);
                if (!IsValidId(id, 32))
                    continue;
                try
                {
                    var root = ReadXml(Path.Combine(directory, "review.xml"), "review");
                    if (ReadAttribute(root, "id") != id)
                        throw new InvalidDataException("Review directory and document IDs differ.");
                    var legacy = (int?)root.Attribute("version") == LegacySchemaVersion;
                    var review = new Review
                    {
                        Id = id,
                        Title = ReadAttribute(root, "title"),
                        GeneralText = ReadText(root.Element("general")),
                        HasGeneralEdits = (bool?)root.Attribute("generalEdited") ?? true,
                        LastSnapshotId = ReadAttribute(root, "lastSnapshot"),
                        SavedVersion = 0,
                    };
                    foreach (var item in root.Element("snapshots")?.Elements("snapshot") ?? [])
                    {
                        var snapshot = new ReviewSnapshot
                        {
                            Id = ReadAttribute(item, "id"),
                            CreatedUtc = ReadDate(item, "created"),
                            Source = ReadEnum<ReviewSourceKind>(item, "source"),
                            BaseRevision = ReadAttribute(item, "base"),
                            TargetRevision = ReadAttribute(item, "target"),
                            SourceFingerprint = ReadAttribute(item, "fingerprint"),
                            Subject = ReadAttribute(item, "subject"),
                        };
                        ValidateId(snapshot.Id, 64);
                        if (!legacy)
                        {
                            var paths = new HashSet<string>(StringComparer.Ordinal);
                            foreach (var xml in item.Elements("file"))
                            {
                                var file = ReadFile(xml);
                                if (!paths.Add(file.Path))
                                    throw new InvalidDataException("Duplicate review file path.");
                                if (ReadAttribute(xml, "captureHash") != ComputeCaptureHash(file))
                                    throw new InvalidDataException("Recorded review context has changed.");
                                snapshot.Files.Add(file);
                            }
                            snapshot.IsLoaded = true;
                        }
                        review.Snapshots.Add(snapshot);
                    }
                    if (review.Snapshots.Count == 0)
                        throw new InvalidDataException("A saved review must contain a snapshot.");
                    if (legacy)
                    {
                        _legacyReviews.Add(id);
                        foreach (var snapshot in review.Snapshots)
                            LoadSnapshot(review, snapshot);
                        SaveAsync(review).GetAwaiter().GetResult();
                    }
                    result.Add(review);
                }
                catch (Exception exception)
                {
                    LoadErrors.Add($"{directory}: {exception.Message}");
                }
            }
            if (LoadErrors.Count == 0 && _legacyReviews.Count == 0)
                ArchiveGeneratedPath(Path.Combine(_root, "_content"));
            result.Sort((a, b) => GetLatestDate(b).CompareTo(GetLatestDate(a)));
            return result;
        }

        public void LoadSnapshot(Review review, ReviewSnapshot snapshot)
        {
            if (snapshot.IsLoaded)
                return;
            ValidateId(review.Id, 32);
            ValidateId(snapshot.Id, 64);
            var directory = Path.Combine(_root, review.Id, "snapshots", snapshot.Id);
            var root = ReadXml(Path.Combine(directory, "snapshot.xml"), "snapshot");
            if (ReadAttribute(root, "id") != snapshot.Id)
                throw new InvalidDataException("Snapshot directory and document IDs differ.");
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in root.Elements("file"))
            {
                var path = ReadAttribute(entry, "path");
                if (!paths.Add(path))
                    throw new InvalidDataException("Duplicate review file path.");
                var xml = ReadXml(Path.Combine(directory, Review.ComputeHash(path) + ".xml"), "file");
                if (ReadAttribute(xml, "path") != path)
                    throw new InvalidDataException("Review file path does not match its manifest.");
                var file = ReadFile(xml);
                if (!file.HasReviewWork)
                    continue;
                foreach (var comment in file.Comments)
                {
                    PrepareAnchor(snapshot, file, comment.Anchor);
                    if (comment.OriginSnapshot == snapshot.Id)
                        PrepareAnchor(snapshot, file, comment.OriginAnchor);
                }
                snapshot.Files.Add(file);
            }
            snapshot.IsLoaded = true;
        }

        public string ReadContent(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return null;
            ValidateId(hash, 64);
            var path = Path.Combine(_root, "_content", hash + ".txt");
            EnsureNoLinks(path);
            if (!File.Exists(path))
                return null;
            var text = s_encoding.GetString(File.ReadAllBytes(path));
            if (Review.ComputeHash(text) != hash)
                throw new InvalidDataException($"Captured review content has changed: {path}");
            return text;
        }

        public async Task SaveAsync(Review review)
        {
            ValidateId(review.Id, 32);
            var directory = Path.Combine(_root, review.Id);
            var root = new XElement("review", CreateVersion(), new XAttribute("id", review.Id),
                new XAttribute("title", review.Title), new XAttribute("lastSnapshot", review.LastSnapshotId),
                new XAttribute("generalEdited", review.HasGeneralEdits), CreateText("general", review.GeneralText));
            var snapshots = new XElement("snapshots");
            var versions = new List<(ReviewFile File, long Version)>();
            var reviewVersion = review.ChangeVersion;
            foreach (var snapshot in review.Snapshots)
            {
                if (!snapshot.IsLoaded)
                    LoadSnapshot(review, snapshot);
                if (snapshot.Id != review.LastSnapshotId && !snapshot.Files.Exists(x => x.HasReviewWork))
                    continue;
                var xml = new XElement("snapshot", new XAttribute("id", snapshot.Id),
                    new XAttribute("created", FormatDate(snapshot.CreatedUtc)),
                    new XAttribute("source", snapshot.Source.ToString().ToLowerInvariant()),
                    new XAttribute("base", snapshot.BaseRevision), new XAttribute("target", snapshot.TargetRevision),
                    new XAttribute("fingerprint", snapshot.SourceFingerprint), new XAttribute("subject", snapshot.Subject));
                foreach (var file in snapshot.Files)
                {
                    if (!file.HasReviewWork)
                        continue;
                    var comments = file.Comments.FindAll(x => !string.IsNullOrWhiteSpace(x.Body));
                    foreach (var comment in comments)
                    {
                        PrepareAnchor(snapshot, file, comment.Anchor);
                        if (comment.OriginAnchor.ContextPatch.Length == 0)
                        {
                            var original = review.Snapshots.Find(x => x.Id == comment.OriginSnapshot);
                            var originalFile = original?.Files.Find(x => x.Path == comment.OriginPath || x.OldPath == comment.OriginPath);
                            if (originalFile != null)
                                PrepareAnchor(original, originalFile, comment.OriginAnchor);
                        }
                    }
                    var patch = ReviewPatch.SelectHunks(file.Patch, comments.Select(x => x.Anchor));
                    var saved = new ReviewFile
                    {
                        Path = file.Path,
                        OldPath = file.OldPath,
                        Patch = patch,
                        OldContent = file.OldContent,
                        NewContent = file.NewContent,
                        IsBinary = file.IsBinary,
                        IsOutsideDiff = file.IsOutsideDiff,
                    };
                    var item = new XElement("file", new XAttribute("path", file.Path), new XAttribute("oldPath", file.OldPath),
                        new XAttribute("oldContent", file.OldContent), new XAttribute("newContent", file.NewContent),
                        new XAttribute("identity", file.SourceIdentity), new XAttribute("binary", file.IsBinary),
                        new XAttribute("outsideDiff", file.IsOutsideDiff), new XAttribute("captureHash", ComputeCaptureHash(saved)),
                        new XAttribute("progress", file.Progress.ToString().ToLowerInvariant()), CreateText("patch", patch));
                    var notes = new XElement("comments");
                    foreach (var comment in comments)
                        notes.Add(new XElement("comment", new XAttribute("id", comment.Id),
                            new XAttribute("state", comment.State.ToString().ToLowerInvariant()),
                            new XAttribute("created", FormatDate(comment.CreatedUtc)),
                            new XAttribute("originSnapshot", comment.OriginSnapshot), new XAttribute("originPath", comment.OriginPath),
                            CreateText("body", comment.Body), CreateAnchor("anchor", comment.Anchor), CreateAnchor("originAnchor", comment.OriginAnchor)));
                    item.Add(notes);
                    xml.Add(item);
                    versions.Add((file, file.ChangeVersion));
                }
                snapshots.Add(xml);
            }
            root.Add(snapshots);
            var metadata = FormatXml(root);
            var current = review.Snapshots.Find(x => x.Id == review.LastSnapshotId) ?? review.Snapshots[^1];
            var markdown = ReviewExport.CreateDocument(review, current);
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await WriteAtomicAsync(Path.Combine(directory, "review.md"), markdown).ConfigureAwait(false);
                await WriteAtomicAsync(Path.Combine(directory, "review.xml"), metadata).ConfigureAwait(false);
                foreach (var version in versions)
                    version.File.SavedVersion = version.Version;
                review.SavedVersion = reviewVersion;
                if (_legacyReviews.Remove(review.Id))
                    ArchiveGeneratedPath(Path.Combine(directory, "snapshots"));
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private void PrepareAnchor(ReviewSnapshot snapshot, ReviewFile file, ReviewAnchor anchor)
        {
            if (anchor.Side == ReviewSide.File || anchor.ContextPatch.Length > 0)
                return;
            var hash = anchor.Side == ReviewSide.Old ? file.OldContent : file.NewContent;
            var source = snapshot.Contents.TryGetValue(hash, out var text) ? text : ReadContent(hash);
            ReviewPatch.CaptureAnchorContext(anchor, file.Patch, source);
        }

        private static ReviewFile ReadFile(XElement xml)
        {
            var file = new ReviewFile
            {
                Path = ReadAttribute(xml, "path"),
                OldPath = ReadAttribute(xml, "oldPath"),
                Patch = ReadText(xml.Element("patch")),
                OldContent = ReadAttribute(xml, "oldContent"),
                NewContent = ReadAttribute(xml, "newContent"),
                SourceIdentity = ReadAttribute(xml, "identity"),
                IsBinary = (bool?)xml.Attribute("binary") ?? false,
                IsOutsideDiff = (bool?)xml.Attribute("outsideDiff") ?? false,
                Progress = ReadEnum<ReviewFileProgress>(xml, "progress"),
                SavedVersion = 0,
            };
            foreach (var comment in xml.Element("comments")?.Elements("comment") ?? [])
            {
                var state = ReadAttribute(comment, "state");
                var workflow = (int?)xml.Attribute("version") == LegacySchemaVersion ? state switch
                {
                    "toreview" => ReviewCommentState.Comment,
                    "todo" => ReviewCommentState.ToFix,
                    "resolved" => ReviewCommentState.Fixed,
                    _ => ReadEnum<ReviewCommentState>(comment, "state"),
                } : ReadEnum<ReviewCommentState>(comment, "state");
                file.Comments.Add(new ReviewComment
                {
                    Id = ReadAttribute(comment, "id"),
                    Body = ReadText(comment.Element("body")),
                    State = workflow,
                    CreatedUtc = ReadDate(comment, "created"),
                    OriginSnapshot = ReadAttribute(comment, "originSnapshot"),
                    OriginPath = ReadAttribute(comment, "originPath"),
                    Anchor = ReadAnchor(comment.Element("anchor")),
                    OriginAnchor = ReadAnchor(comment.Element("originAnchor")),
                });
            }
            return file;
        }

        private static string ComputeCaptureHash(ReviewFile file) => Review.ComputeHash(
            file.Path + "\0" + file.OldPath + "\0" + file.OldContent + "\0" + file.NewContent + "\0" + file.IsBinary + "\0" + file.Patch);

        private void ArchiveGeneratedPath(string path)
        {
            EnsureNoLinks(path);
            if (!Directory.Exists(path))
                return;
            var archiveRoot = Path.Combine(Path.GetDirectoryName(_root)!, Review.ArchiveDirectoryName);
            var destination = Path.Combine(archiveRoot, Path.GetRelativePath(_root, path));
            if (Directory.Exists(destination))
                destination += "_" + Guid.NewGuid().ToString("N");
            EnsureNoLinks(Path.GetDirectoryName(_root)!, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            EnsureNoLinks(Path.GetDirectoryName(_root)!, destination);
            Directory.Move(path, destination);
        }

        private XElement ReadXml(string path, string name)
        {
            EnsureNoLinks(path);
            using var reader = XmlReader.Create(path, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxXmlCharacters,
            });
            var root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
            var version = (int?)root?.Attribute("version");
            if (root?.Name != name || (version != SchemaVersion && version != LegacySchemaVersion))
                throw new InvalidDataException($"Unsupported review document: {path}");
            return root;
        }

        private async Task WriteAtomicAsync(string path, string content)
        {
            EnsureNoLinks(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            EnsureNoLinks(path);
            var temporary = path + ".pending_" + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(temporary, content, s_encoding).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }

        private void EnsureNoLinks(string path) => EnsureNoLinks(_root, path);

        private static void EnsureNoLinks(string root, string path)
        {
            var relative = Path.GetRelativePath(root, path);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new InvalidDataException("Review path is outside its storage directory.");
            var current = root;
            if (new DirectoryInfo(current).LinkTarget != null)
                throw new InvalidDataException("Review storage cannot use symbolic links.");
            foreach (var part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (info.LinkTarget != null)
                    throw new InvalidDataException("Review storage cannot use symbolic links.");
            }
        }

        private static XElement CreateAnchor(string name, ReviewAnchor anchor)
        {
            return new XElement(name, new XAttribute("side", anchor.Side.ToString().ToLowerInvariant()),
                new XAttribute("start", anchor.StartLine), new XAttribute("end", anchor.EndLine),
                new XAttribute("oldStart", anchor.OldStartLine), new XAttribute("oldEnd", anchor.OldEndLine),
                new XAttribute("newStart", anchor.NewStartLine), new XAttribute("newEnd", anchor.NewEndLine),
                new XAttribute("attachment", anchor.Attachment.ToString().ToLowerInvariant()),
                new XAttribute("contextStart", anchor.ContextStartLine), new XAttribute("canMap", anchor.CanAutoMap),
                CreateText("quote", anchor.Quote), CreateText("context", anchor.ContextText), CreateText("patch", anchor.ContextPatch));
        }

        private static ReviewAnchor ReadAnchor(XElement xml)
        {
            if (xml == null)
                throw new InvalidDataException("Review comment is missing its anchor.");
            return new ReviewAnchor
            {
                Side = ReadEnum<ReviewSide>(xml, "side"),
                StartLine = (int?)xml.Attribute("start") ?? 0,
                EndLine = (int?)xml.Attribute("end") ?? 0,
                OldStartLine = (int?)xml.Attribute("oldStart") ?? 0,
                OldEndLine = (int?)xml.Attribute("oldEnd") ?? 0,
                NewStartLine = (int?)xml.Attribute("newStart") ?? 0,
                NewEndLine = (int?)xml.Attribute("newEnd") ?? 0,
                Attachment = ReadEnum<ReviewAttachment>(xml, "attachment"),
                ContextStartLine = (int?)xml.Attribute("contextStart") ?? 0,
                CanAutoMap = (bool?)xml.Attribute("canMap") ?? false,
                Quote = ReadText(xml.Element("quote")),
                ContextText = ReadText(xml.Element("context")),
                ContextPatch = ReadText(xml.Element("patch")),
            };
        }

        private static XElement CreateText(string name, string text)
        {
            var xml = new XElement(name, new XAttribute(XNamespace.Xml + "space", "preserve"));
            try
            {
                XmlConvert.VerifyXmlChars(text);
                xml.Value = text;
            }
            catch (XmlException)
            {
                xml.SetAttributeValue("encoding", "base64");
                xml.Value = Convert.ToBase64String(s_encoding.GetBytes(text));
            }
            return xml;
        }

        private static string ReadText(XElement xml)
        {
            if (xml == null)
                return string.Empty;
            return (string)xml.Attribute("encoding") == "base64" ? s_encoding.GetString(Convert.FromBase64String(xml.Value)) : xml.Value;
        }

        private static string FormatXml(XElement root)
        {
            var builder = new StringBuilder();
            using (var writer = XmlWriter.Create(builder, new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Entitize,
            }))
                root.Save(writer);
            return builder.Append('\n').ToString();
        }

        private static XAttribute CreateVersion() => new("version", SchemaVersion);
        private static string ReadAttribute(XElement xml, string name) => (string)xml.Attribute(name) ?? string.Empty;
        private static string FormatDate(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        private static DateTime ReadDate(XElement xml, string name) => DateTime.Parse(ReadAttribute(xml, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        private static DateTime GetLatestDate(Review review) => review.Snapshots.Count == 0 ? DateTime.MinValue : review.Snapshots[^1].CreatedUtc;

        private static T ReadEnum<T>(XElement xml, string name) where T : struct, Enum
        {
            if (!Enum.TryParse<T>(ReadAttribute(xml, name), true, out var value) || !Enum.IsDefined(value))
                throw new InvalidDataException($"Invalid review {name}.");
            return value;
        }

        private static void ValidateId(string id, int length)
        {
            if (!IsValidId(id, length))
                throw new InvalidDataException("Invalid review storage ID.");
        }

        private static bool IsValidId(string id, int length)
        {
            if (id.Length != length)
                return false;
            foreach (var c in id)
                if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                    return false;
            return true;
        }
    }
}
