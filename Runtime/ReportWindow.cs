using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AnotherWorld.IssueReporting
{
    /// <summary>Owns the reusable report form and the lifetime of its file upload.</summary>
    public sealed class ReportWindow : MonoBehaviour
    {
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_InputField _codeInput;
        [SerializeField] private TMP_InputField _descriptionInput;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private Button _sendButton;
        [SerializeField] private Button _closeButton;

        private readonly ReportClient _client = new();
        private Func<ReportConfiguration> _configurationProvider;
        private IReportAttachmentSource _source;
        private Coroutine _sendRoutine;

        private void Awake()
        {
            if (_panel == null || _codeInput == null || _descriptionInput == null
                || _statusText == null || _sendButton == null || _closeButton == null)
            {
                Debug.LogError("ReportWindow requires its panel, inputs, status, and buttons.", this);
                enabled = false;
                return;
            }
            foreach (TMP_Text text in _panel.GetComponentsInChildren<TMP_Text>(true))
                if (text.font == null) text.font = TMP_Settings.defaultFontAsset;
            _panel.SetActive(false);
            _codeInput.contentType = TMP_InputField.ContentType.Password;
            _descriptionInput.contentType = TMP_InputField.ContentType.Standard;
            _descriptionInput.lineType = TMP_InputField.LineType.MultiLineNewline;
            _descriptionInput.characterLimit = ReportClient.MAX_DESCRIPTION_CHARACTERS;
            _sendButton.onClick.AddListener(RequestSend);
            _closeButton.onClick.AddListener(Close);
            _codeInput.onEndEdit.AddListener(SaveCode);
        }

        /// <summary>Supplies host-owned settings and a file snapshot source without package-to-game references.</summary>
        public void Configure(Func<ReportConfiguration> configurationProvider, IReportAttachmentSource source)
        {
            _configurationProvider = configurationProvider;
            _source = source;
        }

        public void Open()
        {
            if (!enabled || _panel == null) return;
            ReportConfiguration configuration = _configurationProvider?.Invoke();
            if (configuration != null && !string.IsNullOrWhiteSpace(configuration.CodePreferenceKey))
                _codeInput.SetTextWithoutNotify(PlayerPrefs.GetString(configuration.CodePreferenceKey, string.Empty));
            _panel.SetActive(true);
        }

        public void Close()
        {
            Cancel();
            if (_panel != null) _panel.SetActive(false);
        }

        /// <summary>Cancels pending work and releases host references during reset.</summary>
        public void Cleanup()
        {
            Close();
            _configurationProvider = null;
            _source = null;
        }

        private void OnDisable() => Cancel();

        private void OnDestroy()
        {
            if (_sendButton != null) _sendButton.onClick.RemoveListener(RequestSend);
            if (_closeButton != null) _closeButton.onClick.RemoveListener(Close);
            if (_codeInput != null) _codeInput.onEndEdit.RemoveListener(SaveCode);
            Cleanup();
        }

        private void RequestSend()
        {
            if (_client.IsSending) return;
            ReportConfiguration configuration = _configurationProvider?.Invoke();
            if (configuration != null) SaveCode(_codeInput.text);
            _sendRoutine = StartCoroutine(_client.Send(configuration, _source,
                _codeInput.text, _descriptionInput.text, SetStatus));
        }

        private void SaveCode(string code)
        {
            ReportConfiguration configuration = _configurationProvider?.Invoke();
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.CodePreferenceKey)) return;
            PlayerPrefs.SetString(configuration.CodePreferenceKey, code.Trim());
            PlayerPrefs.Save();
        }

        private void SetStatus(string message, bool sending)
        {
            _statusText.text = message;
            _sendButton.interactable = !sending;
        }

        private void Cancel()
        {
            bool wasSending = _client.IsSending;
            if (_sendRoutine != null) StopCoroutine(_sendRoutine);
            _sendRoutine = null;
            _client.Cleanup();
            if (wasSending) SetStatus("Upload cancelled. You can send again.", false);
        }
    }
}
