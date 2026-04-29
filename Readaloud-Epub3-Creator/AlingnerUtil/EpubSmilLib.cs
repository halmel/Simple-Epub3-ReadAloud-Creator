using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using static Readaloud_Epub3_Creator.AlingnerUtil.EpubSmilLib;
using static Readaloud_Epub3_Creator.EpubUtility;

namespace Readaloud_Epub3_Creator.AlingnerUtil
{
    public static class EpubSmilLib
    {
        [XmlRoot("smil", Namespace = "http://www.w3.org/ns/SMIL")]
        public class SmilDocument
        {
            [XmlAttribute("version")]
            public string Version { get; set; } = "3.0";

            [XmlAttribute("id")]
            public string Id { get; set; }

            [XmlElement("head")]
            public SmilHead Head { get; set; }

            [XmlElement("body")]
            public SmilBody Body { get; set; }

            // Helper to handle the epub namespace prefix during serialization
            [XmlNamespaceDeclarations]
            public XmlSerializerNamespaces Namespaces;

            public SmilDocument()
            {
                Namespaces = new XmlSerializerNamespaces();
                Namespaces.Add("epub", "http://www.idpf.org/2007/ops");
                Body = new SmilBody();
            }

            #region IO Functions

            public static SmilDocument Load(string filePath)
            {
                var serializer = new XmlSerializer(typeof(SmilDocument));
                using (var reader = new StreamReader(filePath))
                {
                    return (SmilDocument)serializer.Deserialize(reader);
                }
            }

            public void Save(string filePath)
            {
                var settings = new XmlWriterSettings { Indent = true };
                var serializer = new XmlSerializer(typeof(SmilDocument));
                using (var writer = XmlWriter.Create(filePath, settings))
                {
                    serializer.Serialize(writer, this, Namespaces);
                }
            }

            #endregion
        }

        public class SmilHead
        {
            [XmlAnyElement] // Allows any custom metadata as per spec
            public XmlElement[] Metadata { get; set; }
        }

        public class SmilBody
        {
            [XmlAttribute("id")]
            public string Id { get; set; }

            [XmlAttribute("type", Namespace = "http://www.idpf.org/2007/ops")]
            public string EpubType { get; set; }

            [XmlAttribute("textref", Namespace = "http://www.idpf.org/2007/ops")]
            public string TextRef { get; set; }

            [XmlElement("seq", typeof(SmilSeq))]
            [XmlElement("par", typeof(SmilPar))]
            public List<object> Children { get; set; } = new List<object>();
        }

        public class SmilSeq
        {
            [XmlAttribute("id")]
            public string Id { get; set; }

            [XmlAttribute("type", Namespace = "http://www.idpf.org/2007/ops")]
            public string EpubType { get; set; }

            [XmlAttribute("textref", Namespace = "http://www.idpf.org/2007/ops")]
            public string TextRef { get; set; }

            [XmlElement("seq", typeof(SmilSeq))]
            [XmlElement("par", typeof(SmilPar))]
            public List<object> Children { get; set; } = new List<object>();
        }

        public class SmilPar
        {
            [XmlAttribute("id")]
            public string Id { get; set; }

            [XmlAttribute("type", Namespace = "http://www.idpf.org/2007/ops")]
            public string EpubType { get; set; }

            [XmlElement("text")]
            public SmilText Text { get; set; }

            [XmlElement("audio")]
            public SmilAudio Audio { get; set; }
        }

        public class SmilText
        {
            [XmlAttribute("id")]
            public string Id { get; set; }

            [XmlAttribute("src")]
            public string Src { get; set; }
        }

        public class SmilAudio
        {
            [XmlAttribute("id")]
            public string Id { get; set; }

            [XmlAttribute("src")]
            public string Src { get; set; }

            [XmlAttribute("clipBegin")]
            public string ClipBegin { get; set; }

            [XmlAttribute("clipEnd")]
            public string ClipEnd { get; set; }
        }
    }
    public static class SmilGenerator
    {
        public static void GenerateSmilFiles(List<WordSegment> words, string outputDirectory = "output")
        {
            string audioFolder = "../Audio/";
            Directory.CreateDirectory(outputDirectory);

            // 1. Cleanup old files
            foreach (var oldSmilFile in Directory.GetFiles(outputDirectory, "overlay_*.smil"))
            {
                try { File.Delete(oldSmilFile); } catch { /* log error */ }
            }

            // 2. Group by HTML file
            var groupedByFile = words
                .Where(w => w.LinkedSegments?.Count > 0)
                .GroupBy(w => w.FileName);

            int globalSyncCounter = 0;

            foreach (var fileGroup in groupedByFile)
            {
                string fileName = fileGroup.Key;
                string smilPath = Path.Combine(outputDirectory, $"overlay_{Path.GetFileNameWithoutExtension(fileName)}.smil");

                var smilDoc = new SmilDocument();

                var mainSeq = new SmilSeq
                {
                    Id = $"id_overlay_{Path.GetFileNameWithoutExtension(fileName)}",
                    TextRef = $"../{fileName}",
                    EpubType = "chapter"
                };
                smilDoc.Body.Children.Add(mainSeq);

                double previousEndTime = 0;
                string previousSegmentGroupKey = null;

                var bySegmentSet = fileGroup
                    .GroupBy(w => string.Join(";", w.LinkedSegments.Select(s => $"{s.FileId}_{s.IndexInList}")))
                    .ToList();

                foreach (var segmentGroup in bySegmentSet)
                {
                    string currentSegmentGroupKey = string.Join(";", segmentGroup.SelectMany(w => w.LinkedSegments)
                        .Select(s => s.FileId).Distinct());

                    if (currentSegmentGroupKey != previousSegmentGroupKey)
                    {
                        previousEndTime = 0;
                    }
                    previousSegmentGroupKey = currentSegmentGroupKey;

                    var wordsInSegment = segmentGroup.ToList();
                    var sentenceGroups = wordsInSegment
                        .GroupBy(w => w.SentenceIndex)
                        .OrderBy(g => g.Key)
                        .ToList();

                    var firstSeg = wordsInSegment.SelectMany(w => w.LinkedSegments).OrderBy(s => s.IndexInList).First();
                    var lastSeg = wordsInSegment.SelectMany(w => w.LinkedSegments).OrderBy(s => s.IndexInList).Last();

                    string audioSrc = $"{audioFolder}{firstSeg.FileId}";

                    if (previousEndTime == 0) previousEndTime = firstSeg.Start;

                    double clipBegin = previousEndTime;
                    double clipEnd = 0;

                    double totalChars = sentenceGroups.Sum(g => g.Sum(ws => (ws.Word ?? string.Empty).Length));
                    double overallStart = firstSeg.Start;
                    double overallEnd = lastSeg.End;
                    double totalAvailableSpan = overallEnd - overallStart;

                    foreach (var group in sentenceGroups)
                    {
                        double groupChars = group.Sum(ws => (ws.Word ?? string.Empty).Length);

                        if (sentenceGroups.Count == 1)
                        {
                            clipEnd = lastSeg.End;
                        }
                        else
                        {
                            clipBegin = (clipEnd == 0) ? clipBegin : clipEnd;
                            clipEnd = clipBegin + totalAvailableSpan * (groupChars / totalChars);
                        }

                        // --- VALIDATION & DEBUG BREAKPOINT ---
                        // Check if the FileId is missing or if the duration is zero/invalid
                        if (string.IsNullOrWhiteSpace(firstSeg.FileId) || clipEnd < clipBegin)
                        {
                            Console.WriteLine("!!! DEBUG BREAKPOINT: Empty or Invalid Entry Detected !!!");
                            Console.WriteLine($"Sentence ID: sentence{globalSyncCounter}");
                            Console.WriteLine($"File: {fileName}");
                            Console.WriteLine($"Audio Src: {audioSrc}");
                            Console.WriteLine($"Timing: {clipBegin} to {clipEnd}");

                            //throw new InvalidDataException($"Attempted to generate an empty SMIL entry at sentence{globalSyncCounter}. " +
                            //    "Verify that LinkedSegments contains valid FileIds and non-zero durations.");
                        }
                        // -------------------------------------

                        var par = new SmilPar
                        {
                            Id = $"sentence{globalSyncCounter}",
                            Text = new SmilText
                            {
                                Src = $"../{fileName}#id-sentence{globalSyncCounter}"
                            },
                            Audio = new SmilAudio
                            {
                                Src = audioSrc,
                                ClipBegin = FormatTime(clipBegin),
                                ClipEnd = FormatTime(clipEnd)
                            }
                        };

                        mainSeq.Children.Add(par);
                        globalSyncCounter++;
                    }
                    previousEndTime = clipEnd;
                }

                smilDoc.Save(smilPath);
                Console.WriteLine($"[OK] Generated SMIL: {smilPath}");
            }
        }

        private static string FormatTime(double seconds)
        {
            return $"{seconds:F3}s";
        }
    }
}
