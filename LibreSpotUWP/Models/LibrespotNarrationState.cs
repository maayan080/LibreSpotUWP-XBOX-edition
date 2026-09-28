namespace LibreSpotUWP.Models
{
    public sealed class LibrespotNarrationState
    {
        public string TrackUri { get; set; }
        public ulong PlayRequestId { get; set; }
        public long SessionGeneration { get; set; }
        public bool IsActive { get; set; }
        public uint DurationMs { get; set; }
        public string Text { get; set; }
    }
}
