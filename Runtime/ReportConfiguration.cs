namespace AnotherWorld.IssueReporting
{
    /// <summary>Host-owned transport settings supplied when a report is sent.</summary>
    public sealed class ReportConfiguration
    {
        public string Endpoint { get; }
        public string CodePreferenceKey { get; }
        public int TimeoutSeconds { get; }
        public long MaxSourceBytes { get; }
        public long MaxUploadBytes { get; }

        public ReportConfiguration(string endpoint, string codePreferenceKey,
            int timeoutSeconds, long maxSourceBytes, long maxUploadBytes)
        {
            Endpoint = endpoint;
            CodePreferenceKey = codePreferenceKey;
            TimeoutSeconds = timeoutSeconds;
            MaxSourceBytes = maxSourceBytes;
            MaxUploadBytes = maxUploadBytes;
        }
    }
}
