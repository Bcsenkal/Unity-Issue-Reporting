using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace AnotherWorld.IssueReporting
{
    /// <summary>Uploads one bounded attachment to a configured relay and owns the pending request.</summary>
    public sealed class ReportClient
    {
        public const int MAX_DESCRIPTION_CHARACTERS = 500;
        private const int MAX_ENCODED_METADATA_CHARACTERS = 768;
        private const int MAX_ENCODED_DESCRIPTION_CHARACTERS = 6000;
        private UnityWebRequest _request;
        public bool IsSending { get; private set; }

        public IEnumerator Send(ReportConfiguration configuration, IReportAttachmentSource source,
            string accessCode, string description, Action<string, bool> showStatus)
        {
            if (IsSending) yield break;
            if (configuration == null || !IsValidEndpoint(configuration.Endpoint))
            {
                showStatus("The report endpoint is not configured.", false);
                yield break;
            }
            if (source == null)
            {
                showStatus("No report file source is configured.", false);
                yield break;
            }
            accessCode = accessCode?.Trim();
            if (string.IsNullOrWhiteSpace(accessCode))
            {
                showStatus("Enter the upload code first.", false);
                yield break;
            }
            foreach (char character in accessCode)
            {
                if ((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9') || character == '-' || character == '_') continue;
                showStatus("The upload code contains invalid characters.", false);
                yield break;
            }
            description = description?.Trim();
            if (string.IsNullOrWhiteSpace(description) || description.Length > MAX_DESCRIPTION_CHARACTERS)
            {
                showStatus("Describe the issue in 1 to 500 characters.", false);
                yield break;
            }
            if (!source.TryCreate(configuration.MaxSourceBytes, configuration.MaxUploadBytes,
                    out ReportAttachment attachment, out string error))
            {
                showStatus(string.IsNullOrWhiteSpace(error) ? "Could not prepare the report file." : error, false);
                yield break;
            }
            if (attachment.Bytes == null || attachment.Bytes.Length == 0
                || attachment.Bytes.LongLength > configuration.MaxUploadBytes
                || string.IsNullOrWhiteSpace(attachment.FileName)
                || string.IsNullOrWhiteSpace(attachment.ContentType))
            {
                showStatus("The report file is missing or exceeds its size limit.", false);
                yield break;
            }
            if (!IsSupportedAttachment(attachment)
                || Uri.EscapeDataString(attachment.FileName).Length > MAX_ENCODED_METADATA_CHARACTERS
                || Uri.EscapeDataString(attachment.Context ?? string.Empty).Length > MAX_ENCODED_METADATA_CHARACTERS
                || Uri.EscapeDataString(description).Length > MAX_ENCODED_DESCRIPTION_CHARACTERS)
            {
                showStatus("The report file name or details are not supported by the relay.", false);
                yield break;
            }

            IsSending = true;
            showStatus("Sending report...", true);
            try
            {
                _request = new UnityWebRequest(configuration.Endpoint.Trim(), UnityWebRequest.kHttpVerbPOST);
                _request.uploadHandler = new UploadHandlerRaw(attachment.Bytes);
                _request.downloadHandler = new DownloadHandlerBuffer();
                _request.timeout = Math.Max(1, configuration.TimeoutSeconds);
                _request.redirectLimit = 0;
                _request.SetRequestHeader("Content-Type", attachment.ContentType);
                _request.SetRequestHeader("Authorization", "Bearer " + accessCode);
                _request.SetRequestHeader("X-Report-Filename", Uri.EscapeDataString(attachment.FileName));
                _request.SetRequestHeader("X-Report-Context", Uri.EscapeDataString(attachment.Context ?? string.Empty));
                _request.SetRequestHeader("X-Report-Details", Uri.EscapeDataString(description));
                _request.SetRequestHeader("X-Game-Version", Uri.EscapeDataString(Application.version));
                _request.SetRequestHeader("X-Game-Platform", Application.platform.ToString());
                // Legacy /logs relays may still read these names while hosts migrate to /reports.
                _request.SetRequestHeader("X-Log-Filename", Uri.EscapeDataString(attachment.FileName));
                _request.SetRequestHeader("X-Log-Level", Uri.EscapeDataString(attachment.Context ?? string.Empty));
                yield return _request.SendWebRequest();

                bool delivered = _request.result == UnityWebRequest.Result.Success
                                 && ResponseSucceeded(_request.downloadHandler.text);
                showStatus(delivered ? "Report sent." : FailureMessage(_request.responseCode), false);
            }
            finally
            {
                _request?.Dispose();
                _request = null;
                IsSending = false;
            }
        }

        /// <summary>Aborts and releases a pending request when the host resets or closes.</summary>
        public void Cleanup()
        {
            _request?.Abort();
            _request?.Dispose();
            _request = null;
            IsSending = false;
        }

        private static bool IsValidEndpoint(string endpoint)
        {
            if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out Uri uri)
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)) return false;
            if (uri.Scheme == Uri.UriSchemeHttps) return true;
#if UNITY_EDITOR
            return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
#else
            return false;
#endif
        }

        private static bool IsSupportedAttachment(ReportAttachment attachment)
        {
            string name = attachment.FileName;
            foreach (char character in name)
                if (!((character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9') || character == '_' || character == '-'
                    || character == '.' || character == ' ')) return false;
            return attachment.ContentType switch
            {
                "application/gzip" => name.EndsWith(".gz", StringComparison.Ordinal),
                "text/plain" => name.EndsWith(".txt", StringComparison.Ordinal)
                                || name.EndsWith(".log", StringComparison.Ordinal),
                "application/json" => name.EndsWith(".json", StringComparison.Ordinal),
                "application/zip" => name.EndsWith(".zip", StringComparison.Ordinal),
                _ => false
            };
        }

        private static bool ResponseSucceeded(string response)
        {
            try { return JsonUtility.FromJson<RelayResponse>(response)?.ok == true; }
            catch (ArgumentException) { return false; }
        }

        private static string FailureMessage(long status)
        {
            return status switch
            {
                401 or 403 => "Upload code rejected. Check with your team.",
                413 => "The report file is too large for the relay.",
                429 => "Too many reports. Please try again later.",
                502 or 503 => "The report relay needs attention.",
                _ => "Delivery could not be confirmed. Check your connection and try again."
            };
        }

        [Serializable]
        private sealed class RelayResponse
        {
            public bool ok;
        }
    }
}
