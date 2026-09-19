namespace AnotherWorld.IssueReporting
{
    /// <summary>A bounded, ready-to-upload snapshot supplied by the host game.</summary>
    public readonly struct ReportAttachment
    {
        public byte[] Bytes { get; }
        public string FileName { get; }
        public string ContentType { get; }
        public string Context { get; }

        public ReportAttachment(byte[] bytes, string fileName, string contentType, string context = null)
        {
            Bytes = bytes;
            FileName = fileName;
            ContentType = contentType;
            Context = context;
        }
    }
}
