namespace AnotherWorld.IssueReporting
{
    /// <summary>Creates a stable file snapshot without stopping the host game's logger.</summary>
    public interface IReportAttachmentSource
    {
        bool TryCreate(long maxSourceBytes, long maxUploadBytes, out ReportAttachment attachment, out string error);
    }
}
