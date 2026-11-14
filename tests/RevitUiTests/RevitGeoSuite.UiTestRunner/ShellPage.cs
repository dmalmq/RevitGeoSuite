using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// RevitGeoSuite's web shell inside Revit, reached through WebView2's DevTools protocol. Clicks and
/// reads go through the real page; <see cref="RpcAsync"/> asks the C# side the same questions the page
/// asks, so a check can compare what the page shows with what Revit reports.
/// </summary>
internal sealed class ShellPage : IAsyncDisposable
{
    public const string Host = "ui.revitgeosuite.local";

    private readonly IPlaywright playwright;
    private readonly IBrowser browser;

    private ShellPage(IPlaywright playwright, IBrowser browser, IPage page)
    {
        this.playwright = playwright;
        this.browser = browser;
        Page = page;
    }

    public IPage Page { get; }

    public static async Task<ShellPage> AttachAsync(int port, TimeSpan timeout)
    {
        IPlaywright playwright = await Playwright.CreateAsync();
        IBrowser browser = await playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{port}");
        DateTime end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            IPage? page = browser.Contexts.SelectMany(context => context.Pages).FirstOrDefault(candidate => candidate.Url.Contains(Host, StringComparison.OrdinalIgnoreCase));
            if (page != null)
            {
                page.SetDefaultTimeout(30000);
                return new ShellPage(playwright, browser, page);
            }

            await Task.Delay(250);
        }

        await browser.CloseAsync();
        playwright.Dispose();
        throw new InvalidOperationException($"No RevitGeoSuite page ({Host}) on DevTools port {port}.");
    }

    /// <summary>Sends one bridge request from inside the page and returns the C# handler's reply.</summary>
    public async Task<JsonNode?> RpcAsync(string method, object? payload = null)
    {
        const string script = @"([method, payload]) => new Promise((resolve, reject) => {
            const id = 'uitest_' + Math.random().toString(36).slice(2);
            const onMessage = (event) => {
                const envelope = event.data;
                if (!envelope || envelope.kind !== 'res' || envelope.id !== id) return;
                window.chrome.webview.removeEventListener('message', onMessage);
                if (envelope.error) reject(new Error(envelope.error.code + ': ' + envelope.error.message));
                else resolve(JSON.stringify(envelope.payload ?? null));
            };
            window.chrome.webview.addEventListener('message', onMessage);
            window.chrome.webview.postMessage({ kind: 'req', id, method, payload });
        })";
        string json = await Page.EvaluateAsync<string>(script, new object?[] { method, payload ?? new { } });
        return JsonNode.Parse(json);
    }

    /// <summary>
    /// Starts a bridge job (a request that replies with a jobId) from inside the page and waits for its
    /// job.completed result. Throws with the handler's message on job.failed, an error reply or the timeout.
    /// </summary>
    public async Task<JsonNode?> JobAsync(string method, object payload, TimeSpan timeout)
    {
        const string script = @"([method, payload, timeoutMs]) => new Promise((resolve, reject) => {
            const id = 'uitest_' + Math.random().toString(36).slice(2);
            let jobId = null;
            const early = [];
            const done = (fn, value) => { clearTimeout(timer); window.chrome.webview.removeEventListener('message', onMessage); fn(value); };
            const settle = (envelope) => {
                if (envelope.payload?.jobId !== jobId) return;
                if (envelope.method === 'job.completed') done(resolve, JSON.stringify(envelope.payload.result ?? null));
                if (envelope.method === 'job.failed') done(reject, new Error('job failed: ' + envelope.payload.error));
            };
            const onMessage = (event) => {
                const envelope = event.data;
                if (!envelope) return;
                if (envelope.kind === 'res' && envelope.id === id) {
                    if (envelope.error) return done(reject, new Error(envelope.error.code + ': ' + envelope.error.message));
                    if (!envelope.payload?.jobId) return done(reject, new Error('no job: ' + JSON.stringify(envelope.payload)));
                    jobId = envelope.payload.jobId;
                    early.forEach(settle);
                } else if (envelope.kind === 'evt' && (envelope.method === 'job.completed' || envelope.method === 'job.failed')) {
                    if (jobId === null) early.push(envelope); else settle(envelope);
                }
            };
            const timer = setTimeout(() => done(reject, new Error('no job.completed within ' + timeoutMs + ' ms')), timeoutMs);
            window.chrome.webview.addEventListener('message', onMessage);
            window.chrome.webview.postMessage({ kind: 'req', id, method, payload });
        })";
        string json = await Page.EvaluateAsync<string>(script, new object?[] { method, payload, (long)timeout.TotalMilliseconds });
        return JsonNode.Parse(json);
    }

    public async Task<T> RpcAsync<T>(string method, object? payload = null)
    {
        JsonNode? node = await RpcAsync(method, payload);
        return node.Deserialize<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"{method} returned nothing.");
    }

    /// <summary>Switches the shell to English (the checks match English text) and returns the language it had.</summary>
    public async Task<string> UseEnglishAsync()
    {
        JsonNode? strings = await RpcAsync("localization.getAll");
        string before = strings?["language"]?.GetValue<string>() ?? "english";
        if (!before.Equals("english", StringComparison.OrdinalIgnoreCase))
        {
            await RpcAsync("localization.setLanguage", new { language = "english" });
            await Page.ReloadAsync();
        }

        return before;
    }

    public async Task RestoreLanguageAsync(string language)
    {
        if (!language.Equals("english", StringComparison.OrdinalIgnoreCase))
        {
            await RpcAsync("localization.setLanguage", new { language });
            await Page.ReloadAsync();
        }
    }

    public async Task GoToAsync(string route)
    {
        await Page.EvaluateAsync("route => { window.location.hash = '#' + route }", route);
    }

    public ILocator Button(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    public ILocator Button(Regex name) => Page.GetByRole(AriaRole.Button, new() { NameRegex = name });

    public ILocator Text(string text) => Page.GetByText(text, new() { Exact = false });

    public ILocator Text(Regex text) => Page.GetByText(text);

    public ILocator Alerts => Page.GetByRole(AriaRole.Alert);

    public async Task<bool> ShowsAsync(ILocator locator, int timeoutMs = 30000)
    {
        try
        {
            await locator.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public async Task<string> AlertTextAsync()
    {
        IReadOnlyList<string> texts = await Alerts.AllInnerTextsAsync();
        return string.Join(" | ", texts.Select(text => text.Trim()).Where(text => text.Length > 0));
    }

    public async Task ScreenshotAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await Page.ScreenshotAsync(new() { Path = path, FullPage = true });
    }

    public async ValueTask DisposeAsync()
    {
        // Disconnects only: closing a browser attached over CDP leaves Revit's WebView2 running.
        await browser.CloseAsync();
        playwright.Dispose();
    }
}
