using System;
using System.Linq;
using System.Threading.Tasks;
using RevitGeoSuite.FloorPlanExport.Help;
using RevitGeoSuite.FloorPlanExport.Resources;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using SharedLanguage = RevitGeoSuite.SharedUI.Localization.UiLanguage;
using SharedLocalizer = RevitGeoSuite.SharedUI.Localization.UiLocalizer;
using WinForms = System.Windows.Forms;

namespace RevitGeoSuite.FloorPlanExport.UI;

internal sealed class WebHelpViewerDialog : IDisposable
{
    private readonly HelpContentProvider _provider;
    private readonly string _contextLabel;
    private readonly WebShellWindow _window;
    private HelpLanguage _language;
    private HelpTopic _currentTopic;

    public WebHelpViewerDialog(
        HelpContentProvider provider,
        HelpTopic initialTopic,
        HelpLanguage initialLanguage,
        string? contextLabel = null,
        IntPtr ownerHandle = default)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _currentTopic = initialTopic;
        _language = initialLanguage;
        _contextLabel = string.IsNullOrWhiteSpace(contextLabel) ? string.Empty : contextLabel!.Trim();

        SharedLocalizer.Instance.SetLanguage(ToSharedLanguage(initialLanguage));

        _window = new WebShellWindow(new WebShellOptions
        {
            InitialRoute = "/help",
            TitleKey = "GeoPackage / Shapefile export help",
            OwnerHandle = ownerHandle,
            RegisterHandlers = RegisterHandlers,
        });
    }

    public WinForms.DialogResult ShowDialog()
    {
        _ = _window.ShowDialog();
        return WinForms.DialogResult.OK;
    }

    public void Dispose()
    {
        if (!_window.IsClosed)
        {
            _window.Close();
        }
    }

    private void RegisterHandlers(WebRpcBridge bridge, JobManager jobs)
    {
        bridge.RegisterHandler(new GetInitialStateHandler(this));
        bridge.RegisterHandler(new OpenTopicHandler(this));
    }

    private HelpInitialStateResponse BuildInitialState()
    {
        HelpDocument document = _provider.GetDocument(_currentTopic, _language);
        return new HelpInitialStateResponse
        {
            ProductName = ProjectInfo.Name,
            Version = ProjectInfo.VersionTag,
            ContextLabel = _contextLabel,
            Language = ToLanguageString(_language),
            CurrentTopic = _currentTopic.ToString(),
            Topics = _provider.GetTopicList(_language)
                .Select(topic => new HelpTopicOption
                {
                    Topic = topic.ToString(),
                    Label = _provider.GetTopicLabel(topic, _language),
                })
                .ToList(),
            Document = ToPayload(document),
        };
    }

    private HelpDocumentPayload OpenTopic(HelpOpenTopicRequest request)
    {
        _language = FromLanguageString(request.Language);
        _currentTopic = FromTopicString(request.Topic);
        SharedLocalizer.Instance.SetLanguage(ToSharedLanguage(_language));

        return ToPayload(_provider.GetDocument(_currentTopic, _language));
    }

    private HelpDocumentPayload ToPayload(HelpDocument document)
    {
        return new HelpDocumentPayload
        {
            Topic = document.Topic.ToString(),
            Language = ToLanguageString(document.Language),
            Title = document.Title,
            Html = document.Html,
            IsFallback = document.IsFallback,
        };
    }

    private HelpTopic FromTopicString(string? topic)
    {
        return Enum.TryParse(topic, ignoreCase: true, out HelpTopic parsed)
            ? parsed
            : _currentTopic;
    }

    private static HelpLanguage FromLanguageString(string? language)
    {
        return string.Equals(language, "japanese", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(language, "ja", StringComparison.OrdinalIgnoreCase)
            ? HelpLanguage.Japanese
            : HelpLanguage.English;
    }

    private static string ToLanguageString(HelpLanguage language)
    {
        return language == HelpLanguage.Japanese ? "japanese" : "english";
    }

    private static SharedLanguage ToSharedLanguage(HelpLanguage language)
    {
        return language == HelpLanguage.Japanese ? SharedLanguage.Japanese : SharedLanguage.English;
    }

    private sealed class GetInitialStateHandler : IRpcHandler
    {
        private readonly WebHelpViewerDialog _dialog;

        public GetInitialStateHandler(WebHelpViewerDialog dialog) => _dialog = dialog;

        public string Method => "help.getInitialState";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.BuildInitialState());
    }

    private sealed class OpenTopicHandler : IRpcHandler
    {
        private readonly WebHelpViewerDialog _dialog;

        public OpenTopicHandler(WebHelpViewerDialog dialog) => _dialog = dialog;

        public string Method => "help.openTopic";

        public Task<object?> HandleAsync(object? payload) =>
            Task.FromResult<object?>(_dialog.OpenTopic(PayloadReader.Read<HelpOpenTopicRequest>(payload)));
    }

    private static class PayloadReader
    {
        public static T Read<T>(object? payload) where T : new()
        {
            if (payload is Newtonsoft.Json.Linq.JObject jobj)
            {
                return jobj.ToObject<T>() ?? new T();
            }

            if (payload == null)
            {
                return new T();
            }

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json) ?? new T();
        }
    }
}
