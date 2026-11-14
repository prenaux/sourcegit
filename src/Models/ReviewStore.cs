using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace SourceGit.Models
{
    public sealed class ReviewStore
    {
        private const int SchemaVersion = 1;
        private const long MaxXmlCharacters = 128 * 1024 * 1024;
        private static readonly UTF8Encoding s_encoding = new(false, true);
        private readonly string _root;
        private readonly SemaphoreSlim _writeLock = new(1);

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

                    var review = new Review
                    {
                        Id = id,
                        Title = ReadAttribute(root, "title"),
                        GeneralText = ReadText(root.Element("general")),
                        LastSnapshotId = ReadAttribute(root, "lastSnapshot"),
                        SavedVersion = 0,
                    };
                    foreach (var item in root.Element("snapshots")?.Elements("snapshot") ?? [])
                    {
                        var snapshotId = ReadAttribute(item, "id");
                        ValidateId(snapshotId, 64);
                        review.Snapshots.Add(new ReviewSnapshot
                        {
                            Id = snapshotId,
                            CreatedUtc = ReadDate(item, "created"),
                            Source = ReadEnum<ReviewSourceKind>(item, "source"),
                            BaseRevision = ReadAttribute(item, "base"),
                            TargetRevision = ReadAttribute(item, "target"),
                            SourceFingerprint = ReadAttribute(item, "fingerprint"),
                        });
                    }
                    if (review.Snapshots.Count == 0)
                        throw new InvalidDataException("A saved review must contain a snapshot.");
                    result.Add(review);
                }
                catch (Exception exception)
                {
                    LoadErrors.Add($"{directory}: {exception.Message}");
                }
            }
            result.Sort((a, b) => GetLatestDate(b).CompareTo(GetLatestDate(a)));
            return result;
        }

        public void LoadSnapshot(Review review, ReviewSnapshot snapshot)
        {
            if (snapshot.IsLoaded)
                return;

            var directory = GetSnapshotDirectory(review.Id, snapshot.Id);
            var root = ReadXml(Path.Combine(directory, "snapshot.xml"), "snapshot");
            if (ReadAttribute(root, "id") != snapshot.Id)
                throw new InvalidDataException("Snapshot directory and document IDs differ.");

            var files = new List<ReviewFile>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in root.Elements("file"))
            {
                var path = ReadAttribute(entry, "path");
                if (!paths.Add(path))
                    throw new InvalidDataException("Duplicate review file path.");

                var xml = ReadXml(Path.Combine(directory, Review.ComputeHash(path) + ".xml"), "file");
                if (ReadAttribute(xml, "path") != path)
                    throw new InvalidDataException("Review file path does not match its manifest.");

                var file = new ReviewFile
                {
                    Path = path,
                    OldPath = ReadAttribute(xml, "oldPath"),
                    Patch = ReadText(xml.Element("patch")),
                    OldContent = ReadAttribute(xml, "oldContent"),
                    NewContent = ReadAttribute(xml, "newContent"),
                    IsBinary = (bool?)xml.Attribute("binary") ?? false,
                    IsOutsideDiff = (bool?)xml.Attribute("outsideDiff") ?? false,
                    Progress = ReadEnum<ReviewFileProgress>(xml, "progress"),
                    SavedVersion = 0,
                };
                foreach (var comment in xml.Element("comments")?.Elements("comment") ?? [])
                {
                    file.Comments.Add(new ReviewComment
                    {
                        Id = ReadAttribute(comment, "id"),
                        Body = ReadText(comment.Element("body")),
                        State = ReadEnum<ReviewCommentState>(comment, "state"),
                        CreatedUtc = ReadDate(comment, "created"),
                        OriginSnapshot = ReadAttribute(comment, "originSnapshot"),
                        OriginPath = ReadAttribute(comment, "originPath"),
                        Anchor = ReadAnchor(comment.Element("anchor")),
                        OriginAnchor = ReadAnchor(comment.Element("originAnchor")),
                    });
                }
                files.Add(file);
            }
            snapshot.Files = files;
            if (snapshot.ComputeContentId() != snapshot.Id)
                throw new InvalidDataException("Captured review code no longer matches its snapshot ID.");
            snapshot.IsLoaded = true;
        }

        public string ReadContent(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return string.Empty;

            ValidateId(hash, 64);
            var path = Path.Combine(_root, "_content", hash + ".txt");
            EnsureNoLinks(path);
            var text = s_encoding.GetString(File.ReadAllBytes(path));
            if (Review.ComputeHash(text) != hash)
                throw new InvalidDataException($"Captured review content has changed: {path}");
            return text;
        }

        public async Task SaveAsync(Review review)
        {
            ValidateId(review.Id, 32);
            var directory = Path.Combine(_root, review.Id);
            var writes = new List<(string Path, string Text)>();
            var versions = new List<(ReviewFile File, long Version)>();
            var reviewVersion = review.ChangeVersion;
            foreach (var snapshot in review.Snapshots)
            {
                if (!snapshot.IsLoaded)
                    continue;

                if (snapshot.ComputeContentId() != snapshot.Id)
                    throw new InvalidDataException("Captured review code no longer matches its snapshot ID.");
                var snapshotDirectory = GetSnapshotDirectory(review.Id, snapshot.Id);
                var manifest = Path.Combine(snapshotDirectory, "snapshot.xml");
                var isNew = !File.Exists(manifest);
                if (isNew)
                {
                    foreach (var content in snapshot.Contents)
                    {
                        ValidateId(content.Key, 64);
                        if (Review.ComputeHash(content.Value) != content.Key)
                            throw new InvalidDataException("Captured review content does not match its hash.");

                        var contentPath = Path.Combine(_root, "_content", content.Key + ".txt");
                        if (!File.Exists(contentPath))
                            writes.Add((contentPath, content.Value));
                    }
                }

                foreach (var file in snapshot.Files)
                {
                    if (!isNew && file.SavedVersion == file.ChangeVersion)
                        continue;

                    var version = file.ChangeVersion;
                    var xml = new XElement("file", CreateVersion(),
                        new XAttribute("path", file.Path), new XAttribute("oldPath", file.OldPath),
                        new XAttribute("oldContent", file.OldContent), new XAttribute("newContent", file.NewContent),
                        new XAttribute("binary", file.IsBinary), new XAttribute("outsideDiff", file.IsOutsideDiff),
                        new XAttribute("progress", file.Progress.ToString().ToLowerInvariant()),
                        CreateText("patch", file.Patch));
                    var comments = new XElement("comments");
                    foreach (var comment in file.Comments)
                    {
                        comments.Add(new XElement("comment", new XAttribute("id", comment.Id),
                            new XAttribute("state", comment.State.ToString().ToLowerInvariant()),
                            new XAttribute("created", FormatDate(comment.CreatedUtc)),
                            new XAttribute("originSnapshot", comment.OriginSnapshot), new XAttribute("originPath", comment.OriginPath),
                            CreateText("body", comment.Body), CreateAnchor("anchor", comment.Anchor),
                            CreateAnchor("originAnchor", comment.OriginAnchor)));
                    }
                    xml.Add(comments);
                    writes.Add((Path.Combine(snapshotDirectory, Review.ComputeHash(file.Path) + ".xml"), FormatXml(xml)));
                    versions.Add((file, version));
                }

                if (isNew)
                {
                    var xml = new XElement("snapshot", CreateVersion(), new XAttribute("id", snapshot.Id));
                    foreach (var file in snapshot.Files)
                        xml.Add(new XElement("file", new XAttribute("path", file.Path)));
                    writes.Add((manifest, FormatXml(xml)));
                }
            }

            var root = new XElement("review", CreateVersion(), new XAttribute("id", review.Id),
                new XAttribute("title", review.Title), new XAttribute("lastSnapshot", review.LastSnapshotId),
                CreateText("general", review.GeneralText));
            var snapshots = new XElement("snapshots");
            foreach (var snapshot in review.Snapshots)
            {
                snapshots.Add(new XElement("snapshot", new XAttribute("id", snapshot.Id),
                    new XAttribute("created", FormatDate(snapshot.CreatedUtc)),
                    new XAttribute("source", snapshot.Source.ToString().ToLowerInvariant()),
                    new XAttribute("base", snapshot.BaseRevision), new XAttribute("target", snapshot.TargetRevision),
                    new XAttribute("fingerprint", snapshot.SourceFingerprint)));
            }
            root.Add(snapshots);
            writes.Add((Path.Combine(directory, "review.xml"), FormatXml(root)));

            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (var write in writes)
                    await WriteAtomicAsync(write.Path, write.Text).ConfigureAwait(false);
                foreach (var version in versions)
                    version.File.SavedVersion = version.Version;
                review.SavedVersion = reviewVersion;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private string GetSnapshotDirectory(string reviewId, string snapshotId)
        {
            ValidateId(reviewId, 32);
            ValidateId(snapshotId, 64);
            return Path.Combine(_root, reviewId, "snapshots", snapshotId);
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
            if (root?.Name != name || (int?)root.Attribute("version") != SchemaVersion)
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

        private void EnsureNoLinks(string path)
        {
            var relative = Path.GetRelativePath(_root, path);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
                throw new InvalidDataException("Review path is outside _reviews.");

            var current = _root;
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
                new XAttribute("attachment", anchor.Attachment.ToString().ToLowerInvariant()), CreateText("quote", anchor.Quote));
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
                Quote = ReadText(xml.Element("quote")),
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
