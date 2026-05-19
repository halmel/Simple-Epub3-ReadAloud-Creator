namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Models
{
    /// <summary>
    /// Represents a gap in audio synchronization where text words lack corresponding audio fragments.
    /// Used internally to fill alignment gaps between synchronized segments.
    /// </summary>
    public class AudioLinkGap
    {
        public int StartSegmentIndex { get; set; }
        public int EndSegmentIndex { get; set; }
        public List<WordSegment> AffectedWords { get; set; } = new();
        public bool IsGap { get; set; }
    }
}
