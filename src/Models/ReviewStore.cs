using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SourceGit.Models
{
    public sealed class ReviewStore
    {
        private const int SchemaVersion = 3;
        // Version 2 kept one comment list per snapshot. The reader keeps the last snapshot only.
        // Remove this reader when no repository holds a version 2 `review.xml`.
        private const int SnapshotSchemaVersion = 2;
        private const long MaxXmlCharacters = 128 * 1024 * 1024;
        private const string MetadataFileName = "review.xml";
        private const string DocumentFileName = "review.md";
        private static readonly UTF8Encoding s_encoding = new(false, true);
        private readonly string _root;

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
                if (!IsValidId(id))
                    continue;

                try
                {
                    result.Add(ReadReview(id, ReadXml(Path.Combine(directory, MetadataFileName))));
                }
                catch (Exception exception)
                {
                    LoadErrors.Add($"{directory}: {exception.Message}");
                }
            }

            result.Sort((a, b) => b.CreatedUtc.CompareTo(a.CreatedUtc));
            return result;
        }

        public void Save(Review review)
        {
            ValidateId(review.Id);
            var directory = Path.Combine(_root, review.Id);
            var metadata = FormatXml(CreateXml(review));
            var document = ReviewExport.CreateDocument(review);

            WriteAtomic(Path.Combine(directory, DocumentFileName), document);
            WriteAtomic(Path.Combine(directory, MetadataFileName), metadata);
            review.IsSaved = true;
        }

        // Moves the review out of the active storage. The user deletes the archive by hand.
        public void Archive(Review review)
        {
            ValidateId(review.Id);
            var directory = Path.Combine(_root, review.Id);
            EnsureNoLinks(directory);
            review.IsSaved = false;
            if (!Directory.Exists(directory))
                return;

            var repository = Path.GetDirectoryName(_root)!;
            var destination = Path.Combine(repository, Review.ArchiveDirectoryName, review.Id);
            if (Directory.Exists(destination))
                destination += "_" + Guid.NewGuid().ToString("N");
            EnsureNoLinks(repository, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            EnsureNoLinks(repository, destination);
            Directory.Move(directory, destination);
        }

        private static XElement CreateXml(Review review)
        {
            var root = new XElement("review", new XAttribute("version", SchemaVersion), new XAttribute("id", review.Id),
                new XAttribute("title", review.Title), new XAttribute("source", FormatEnum(review.Source)),
                new XAttribute("base", review.BaseRevision), new XAttribute("target", review.TargetRevision),
                new XAttribute("created", FormatDate(review.CreatedUtc)), CreateText("general", review.GeneralText));
            foreach (var file in review.Files)
            {
                if (!file.HasReviewWork)
                    continue;

                var item = new XElement("file", new XAttribute("path", file.Path), new XAttribute("oldPath", file.OldPath));
                foreach (var comment in file.Comments)
                {
                    if (!comment.HasBody)
                        continue;

                    var anchor = comment.Anchor;
                    item.Add(new XElement("comment", new XAttribute("id", comment.Id),
                        new XAttribute("state", FormatEnum(comment.State)), new XAttribute("created", FormatDate(comment.CreatedUtc)),
                        CreateText("body", comment.Body),
                        new XElement("anchor", new XAttribute("side", FormatEnum(anchor.Side)),
                            new XAttribute("oldStart", anchor.OldStartLine), new XAttribute("oldEnd", anchor.OldEndLine),
                            new XAttribute("newStart", anchor.NewStartLine), new XAttribute("newEnd", anchor.NewEndLine),
                            new XAttribute("attachment", FormatEnum(anchor.Attachment)), new XAttribute("contextStart", anchor.ContextStartLine),
                            CreateText("quote", anchor.Quote), CreateText("context", anchor.ContextText), CreateText("patch", anchor.ContextPatch))));
                }
                root.Add(item);
            }
            return root;
        }

        private static Review ReadReview(string id, XElement root)
        {
            if (ReadAttribute(root, "id") != id)
                throw new InvalidDataException("Review directory and document IDs differ.");

            var review = new Review
            {
                Id = id,
                Title = ReadAttribute(root, "title"),
                GeneralText = ReadText(root.Element("general")),
                IsSaved = true,
            };

            var source = root;
            var files = root.Elements("file");
            if ((int?)root.Attribute("version") == SnapshotSchemaVersion)
            {
                source = FindLastSnapshot(root);
                files = source.Elements("file");
            }

            review.Source = ReadSource(source);
            review.BaseRevision = ReadAttribute(source, "base");
            review.TargetRevision = ReadAttribute(source, "target");
            review.CreatedUtc = ReadDate(source, "created");

            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var xml in files)
            {
                var file = new ReviewFile { Path = ReadAttribute(xml, "path"), OldPath = ReadAttribute(xml, "oldPath") };
                if (!paths.Add(file.Path))
                    throw new InvalidDataException("Duplicate review file path.");

                foreach (var comment in xml.Descendants("comment"))
                {
                    file.Comments.Add(new ReviewComment
                    {
                        Id = ReadAttribute(comment, "id"),
                        Body = ReadText(comment.Element("body")),
                        State = ReadEnum<ReviewCommentState>(comment, "state"),
                        CreatedUtc = ReadDate(comment, "created"),
                        Anchor = ReadAnchor(comment.Element("anchor")),
                    });
                }
                if (file.HasReviewWork)
                    review.Files.Add(file);
            }
            return review;
        }

        private static XElement FindLastSnapshot(XElement root)
        {
            var last = ReadAttribute(root, "lastSnapshot");
            XElement result = null;
            foreach (var snapshot in root.Element("snapshots")?.Elements("snapshot") ?? [])
            {
                result = snapshot;
                if (ReadAttribute(snapshot, "id") == last)
                    break;
            }
            return result ?? throw new InvalidDataException("A saved review must contain a snapshot.");
        }

        private static ReviewSourceKind ReadSource(XElement xml)
        {
            var source = ReadAttribute(xml, "source");
            if (source is "staged" or "unstaged" or "alllocal")
                return ReviewSourceKind.Local;
            return ReadEnum<ReviewSourceKind>(xml, "source");
        }

        private static ReviewAnchor ReadAnchor(XElement xml)
        {
            if (xml == null)
                throw new InvalidDataException("Review comment is missing its anchor.");

            var anchor = new ReviewAnchor
            {
                Side = ReadEnum<ReviewSide>(xml, "side"),
                OldStartLine = (int?)xml.Attribute("oldStart") ?? 0,
                OldEndLine = (int?)xml.Attribute("oldEnd") ?? 0,
                NewStartLine = (int?)xml.Attribute("newStart") ?? 0,
                NewEndLine = (int?)xml.Attribute("newEnd") ?? 0,
                Attachment = ReadEnum<ReviewAttachment>(xml, "attachment"),
                ContextStartLine = (int?)xml.Attribute("contextStart") ?? 0,
                Quote = ReadText(xml.Element("quote")),
                ContextText = ReadText(xml.Element("context")),
                ContextPatch = ReadText(xml.Element("patch")),
            };

            // Version 2 stored the range of the commented side in `start` and `end`.
            var start = (int?)xml.Attribute("start") ?? 0;
            var end = (int?)xml.Attribute("end") ?? 0;
            if (end > 0 && anchor.Side == ReviewSide.Old)
            {
                anchor.OldStartLine = start;
                anchor.OldEndLine = end;
            }
            else if (end > 0 && anchor.Side == ReviewSide.New)
            {
                anchor.NewStartLine = start;
                anchor.NewEndLine = end;
            }
            return anchor;
        }

        private XElement ReadXml(string path)
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
            if (root?.Name != "review" || (version != SchemaVersion && version != SnapshotSchemaVersion))
                throw new InvalidDataException($"Unsupported review document: {path}");
            return root;
        }

        private void WriteAtomic(string path, string content)
        {
            EnsureNoLinks(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            EnsureNoLinks(path);
            var temporary = path + ".pending_" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, content, s_encoding);
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

        private static string FormatEnum<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
        private static string ReadAttribute(XElement xml, string name) => (string)xml.Attribute(name) ?? string.Empty;
        private static string FormatDate(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        private static DateTime ReadDate(XElement xml, string name) => DateTime.Parse(ReadAttribute(xml, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        private static T ReadEnum<T>(XElement xml, string name) where T : struct, Enum
        {
            if (!Enum.TryParse<T>(ReadAttribute(xml, name), true, out var value) || !Enum.IsDefined(value))
                throw new InvalidDataException($"Invalid review {name}.");
            return value;
        }

        private static void ValidateId(string id)
        {
            if (!IsValidId(id))
                throw new InvalidDataException("Invalid review storage ID.");
        }

        private static bool IsValidId(string id)
        {
            if (id.Length != 32)
                return false;
            foreach (var c in id)
                if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                    return false;
            return true;
        }
    }
}
