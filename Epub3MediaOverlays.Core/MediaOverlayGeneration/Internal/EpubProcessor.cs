using EpubSharp;
using HtmlAgilityPack;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Models;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal
{
    /// <summary>
    /// Internal processor for EPUB operations.
    /// Handles loading, extracting, rebuilding EPUB files and managing HTML content.
    /// </summary>
    internal static class EpubProcessor
    {
        /// <summary>
        /// Loads an EPUB file and extracts all HTML content as HtmlDocuments.
        /// </summary>
        public static Dictionary<string, HtmlDocument> LoadEpubAndExtractHtml(string epubPath)
        {
            if (!File.Exists(epubPath))
                throw new FileNotFoundException($"EPUB not found: {epubPath}");

            EpubBook book = EpubReader.Read(epubPath);
            var result = new Dictionary<string, HtmlDocument>();

            foreach (EpubTextFile htmlFile in book.Resources.Html)
            {
                var html = htmlFile.TextContent;
                var doc = new HtmlDocument
                {
                    OptionWriteEmptyNodes = true
                };

                doc.LoadHtml(html);
                result[htmlFile.FileName] = doc;
            }

            return result;
        }

        /// <summary>
        /// Extracts all text segments from the provided HTML documents.
        /// Each segment represents a single text node from the DOM.
        /// </summary>
        public static List<HtmlTextSegment> ExtractAllTextSegments(Dictionary<string, HtmlDocument> htmlDocs)
        {
            var segments = new List<HtmlTextSegment>();

            foreach (var (fileName, doc) in htmlDocs)
            {
                var textNodes = doc.DocumentNode
                    .Descendants()
                    .Where(n =>
                        n.NodeType == HtmlNodeType.Text &&
                        !string.IsNullOrWhiteSpace(n.InnerText) &&
                        n.ParentNode.Name != "script" &&
                        n.ParentNode.Name != "style")
                    .ToList();

                foreach (var textNode in textNodes)
                {
                    var parent = textNode.ParentNode;
                    var parentXPath = parent.XPath;

                    var textSiblings = parent.ChildNodes
                        .Where(n => n.NodeType == HtmlNodeType.Text)
                        .ToList();

                    int index = textSiblings.IndexOf(textNode);

                    segments.Add(new HtmlTextSegment
                    {
                        FileName = fileName,
                        ParentXPath = parentXPath,
                        TextNodeIndex = index,
                        OriginalText = textNode.InnerText,
                        EditedText = null
                    });
                }
            }

            return segments;
        }

        /// <summary>
        /// Applies edited text segments back to the HTML documents.
        /// </summary>
        public static void ApplyTextSegmentsToHtmlDocuments(
            Dictionary<string, HtmlDocument> htmlDocs,
            List<HtmlTextSegment> segments)
        {
            foreach (var segment in segments)
            {
                if (!htmlDocs.TryGetValue(segment.FileName, out HtmlDocument doc))
                    continue;

                string parentXPath = segment.ParentXPath;
                if (string.IsNullOrWhiteSpace(parentXPath))
                    continue;

                // Sanitize bad xpaths like "/#document"
                if (parentXPath.StartsWith("/#document"))
                {
                    parentXPath = parentXPath.Replace("/#document", "");
                    if (string.IsNullOrEmpty(parentXPath))
                        parentXPath = "/";
                }

                var parentNode = doc.DocumentNode.SelectSingleNode(parentXPath);
                if (parentNode == null)
                    continue;

                int textIndex = -1;
                for (int i = 0, count = 0; i < parentNode.ChildNodes.Count; i++)
                {
                    var child = parentNode.ChildNodes[i];
                    if (child.NodeType == HtmlNodeType.Text)
                    {
                        if (count == segment.TextNodeIndex)
                        {
                            child.InnerHtml = segment.EditedText;
                            break;
                        }
                        count++;
                    }
                }
            }
        }

        /// <summary>
        /// Rebuilds an EPUB file with updated HTML content, SMIL files, and audio files.
        /// </summary>
        public static void RebuildEpubWithMedia(
            string originalEpubPath,
            Dictionary<string, HtmlDocument> updatedHtmlDocs,
            List<string> smilFiles,
            List<string> audioFiles,
            string outputEpubPath)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "epub_rebuild_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                // STEP 1: Extract EPUB
                ZipFile.ExtractToDirectory(originalEpubPath, tempDir);

                // STEP 2: Locate content.opf
                string opfPath = Directory.GetFiles(tempDir, "*.opf", SearchOption.AllDirectories).FirstOrDefault();
                if (opfPath == null)
                    throw new Exception("OPF file not found.");

                string contentRoot = Path.GetDirectoryName(opfPath);

                // STEP 3: Replace HTML files
                foreach (var kvp in updatedHtmlDocs)
                {
                    string fileNameOnly = Path.GetFileName(kvp.Key);
                    string htmlPath = Directory
                        .EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories)
                        .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileNameOnly, StringComparison.OrdinalIgnoreCase));

                    if (htmlPath != null)
                    {
                        using (var sw = new StringWriter())
                        {
                            kvp.Value.Save(sw);
                            string updatedHtml = sw.ToString();
                            File.WriteAllText(htmlPath, updatedHtml);
                        }
                    }
                }

                // STEP 4: Prepare folders
                string audioDir = Path.Combine(contentRoot, "Audio");
                string mediaOverlaysDir = Path.Combine(contentRoot, "MediaOverlays");

                Directory.CreateDirectory(audioDir);
                Directory.CreateDirectory(mediaOverlaysDir);

                foreach (var smilFile in smilFiles)
                    File.Copy(smilFile, Path.Combine(mediaOverlaysDir, Path.GetFileName(smilFile)), true);

                foreach (var audioFile in audioFiles)
                    File.Copy(audioFile, Path.Combine(audioDir, Path.GetFileName(audioFile)), true);

                // STEP 5: Patch OPF manifest
                var doc = new XmlDocument();
                doc.Load(opfPath);

                var nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("opf", "http://www.idpf.org/2007/opf");

                var manifest = doc.SelectSingleNode("//opf:manifest", nsmgr);
                if (manifest == null)
                    throw new Exception("Manifest not found in OPF.");

                var metadataNode = doc.SelectSingleNode("//opf:metadata", nsmgr);
                if (metadataNode == null)
                {
                    metadataNode = doc.CreateElement("metadata", doc.DocumentElement.NamespaceURI);
                    doc.DocumentElement.InsertBefore(metadataNode, doc.DocumentElement.FirstChild);
                }

                var smilMap = new Dictionary<string, string>();
                var smilDurations = new List<double>();

                foreach (var smilPath in smilFiles)
                {
                    string fileName = Path.GetFileName(smilPath);
                    string smilHref = $"MediaOverlays/{fileName}";

                    var smilDoc = new XmlDocument();
                    smilDoc.Load(smilPath);
                    smilDoc.DocumentElement?.SetAttribute("xmlns:epub", "http://www.idpf.org/2007/ops");

                    XmlNamespaceManager smilNsMgr = new XmlNamespaceManager(smilDoc.NameTable);
                    smilNsMgr.AddNamespace("smil", "http://www.w3.org/ns/SMIL");
                    smilNsMgr.AddNamespace("epub", "http://www.idpf.org/2007/ops");

                    var seqNode = smilDoc.SelectSingleNode("//smil:seq", smilNsMgr) as XmlElement;
                    if (seqNode == null) continue;

                    string smilId = seqNode.GetAttribute("id");
                    string textRef = seqNode.GetAttribute("epub:textref");
                    string htmlFile = Path.GetFileName(textRef);

                    smilMap[htmlFile] = smilId;
                    double durationSeconds = ExtractTotalLengthFromSmilSeconds(smilPath);
                    string smilDuration = ToEpubMetadataTime(durationSeconds);

                    var meta = doc.CreateElement("meta", doc.DocumentElement.NamespaceURI);
                    meta.SetAttribute("property", "media:duration");
                    meta.SetAttribute("refines", $"#{smilId}");
                    meta.InnerText = smilDuration;
                    metadataNode.AppendChild(meta);

                    smilDurations.Add(durationSeconds);

                    var item = doc.CreateElement("item", manifest.NamespaceURI);
                    item.SetAttribute("id", smilId);
                    item.SetAttribute("href", smilHref);
                    item.SetAttribute("media-type", "application/smil+xml");
                    manifest.AppendChild(item);
                }

                int audioIndex = 1;
                foreach (var audio in audioFiles)
                {
                    string fileName = Path.GetFileName(audio);
                    string safeId = $"a{audioIndex++}";

                    var item = doc.CreateElement("item", manifest.NamespaceURI);
                    item.SetAttribute("id", safeId);
                    item.SetAttribute("href", $"Audio/{fileName}");
                    item.SetAttribute("media-type", "audio/mpeg");
                    manifest.AppendChild(item);
                }

                var htmlItems = doc.SelectNodes("//opf:item[@media-type='application/xhtml+xml']", nsmgr);
                foreach (XmlElement item in htmlItems)
                {
                    string href = item.GetAttribute("href");
                    if (smilMap.TryGetValue(Path.GetFileName(href), out var smilId))
                    {
                        item.SetAttribute("media-overlay", smilId);
                    }
                }

                double totalDurationSeconds = smilDurations.Sum();
                string totalDuration = ToEpubMetadataTime(totalDurationSeconds);

                var totalMeta = doc.CreateElement("meta", doc.DocumentElement.NamespaceURI);
                totalMeta.SetAttribute("property", "media:duration");
                totalMeta.InnerText = totalDuration;
                metadataNode.AppendChild(totalMeta);

                doc.Save(opfPath);

                // STEP 6: Repack EPUB
                if (File.Exists(outputEpubPath))
                    File.Delete(outputEpubPath);

                using (var zip = ZipFile.Open(outputEpubPath, ZipArchiveMode.Create))
                {
                    string mimetypePath = Path.Combine(tempDir, "mimetype");
                    zip.CreateEntryFromFile(mimetypePath, "mimetype", CompressionLevel.NoCompression);

                    foreach (string file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                    {
                        string entryName = Path.GetRelativePath(tempDir, file).Replace('\\', '/');
                        if (entryName == "mimetype") continue;

                        zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        /// <summary>
        /// Recombines word segments back into text segments for HTML injection.
        /// </summary>
        public static List<HtmlTextSegment> RecombineWordsIntoTextSegments(List<WordSegment> words)
        {
            var grouped = words
                .GroupBy(w => new { w.FileName, w.ParentXPath, w.TextNodeIndex })
                .Select(g =>
                {
                    string fullText = string.Concat(g.OrderBy(w => w.WordIndexInSegment).Select(w => w.Word));
                    return new HtmlTextSegment
                    {
                        FileName = g.Key.FileName,
                        ParentXPath = g.Key.ParentXPath,
                        TextNodeIndex = g.Key.TextNodeIndex,
                        OriginalText = null,
                        EditedText = fullText
                    };
                });

            return grouped.ToList();
        }

        /// <summary>
        /// Normalizes all audio segments to their full MP3 file lengths.
        /// </summary>
        public static void NormalizeSegmentsToFullMp3Length(List<WordSegment> words)
        {
            var allSegments = words.SelectMany(w => w.LinkedSegments).ToList();
            var fileGroups = allSegments.GroupBy(s => s.FileId);

            foreach (var fileGroup in fileGroups)
            {
                var segments = fileGroup.OrderBy(s => s.Start).ToList();
                if (segments.Count == 0)
                    continue;

                var firstSeg = segments.First();
                var lastSeg = segments.Last();

                double fileLength = lastSeg.FileLength;
                firstSeg.Start = 0;
                lastSeg.End = fileLength;

                foreach (var w in words)
                {
                    for (int i = 0; i < w.LinkedSegments.Count; i++)
                    {
                        var seg = w.LinkedSegments[i];
                        if (seg.FileId == fileGroup.Key)
                        {
                            if (seg.IndexInList == firstSeg.IndexInList)
                                seg.Start = 0;
                            if (seg.IndexInList == lastSeg.IndexInList)
                                seg.End = fileLength;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Loads word segments from a JSON file.
        /// </summary>
        public static List<WordSegment> LoadWordSegments(string path)
        {
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return new List<WordSegment>();

            return JsonSerializer.Deserialize<List<WordSegment>>(json);
        }

        /// <summary>
        /// Saves word segments to a JSON file.
        /// </summary>
        public static void SaveWordSegments(List<WordSegment> words, string path)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            string json = JsonSerializer.Serialize(words, options);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Gets all files of a specific type in a directory.
        /// </summary>
        public static List<string> GetAllFilesOfType(string folderPath, string extension)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

            if (!extension.StartsWith("."))
                extension = "." + extension;

            return new List<string>(
                Directory.GetFiles(folderPath, "*" + extension, SearchOption.AllDirectories)
            );
        }

        private static double ExtractTotalLengthFromSmilSeconds(string smilFilePath)
        {
            var lines = File.ReadLines(smilFilePath);
            var totalLengthLine = lines.FirstOrDefault(line => line.Contains("<!-- TotalLength:"));
            if (totalLengthLine == null) return 0;

            var match = Regex.Match(totalLengthLine, @"<!-- TotalLength:\s*([\d\.,]+)");

            if (match.Success)
            {
                string numberStr = match.Groups[1].Value.Replace(',', '.');
                if (double.TryParse(numberStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                {
                    return seconds;
                }
            }

            return 0;
        }

        private static string ToEpubMetadataTime(double totalSeconds)
        {
            int hours = (int)(totalSeconds / 3600);
            int minutes = (int)((totalSeconds % 3600) / 60);
            int seconds = (int)(totalSeconds % 60);
            double fractional = totalSeconds - Math.Floor(totalSeconds);

            string fractionStr = ((int)(fractional * 100)).ToString("00");

            return $"{hours:00}:{minutes:00}:{seconds:00}.{fractionStr}";
        }
    }
}
