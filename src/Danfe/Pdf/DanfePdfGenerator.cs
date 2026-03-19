using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace Direction.NFSe.Danfe;

public sealed class DanfePdfGenerator : IAsyncDisposable
{
    private IBrowser? _browser;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _poolLock;
    private readonly ConcurrentBag<IPage> _pagePool = new();
    private readonly int _poolSize;

    public DanfePdfGenerator(int poolSize = 0)
    {
        _poolSize = poolSize > 0 ? poolSize : Environment.ProcessorCount;
        _poolLock = new SemaphoreSlim(_poolSize, _poolSize);
    }

    public async Task<IBrowser> GetBrowserAsync()
    {
        if (_browser is { IsConnected: true })
            return _browser;

        await _initLock.WaitAsync();
        try
        {
            if (_browser is { IsConnected: true })
                return _browser;

            var executablePath = Environment.OSVersion.Platform == PlatformID.Unix
                ? "/usr/bin/chromium"
                : null;

            if (executablePath == null)
            {
                var fetcher = new BrowserFetcher();
                await fetcher.DownloadAsync();
            }

            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                ExecutablePath = executablePath,
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-setuid-sandbox",
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                    "--single-process"
                }
            });

            // Pré-aquece o pool criando as páginas no startup
            var tasks = new Task[_poolSize];
            for (var i = 0; i < _poolSize; i++)
                tasks[i] = WarmUpPageAsync();

            await Task.WhenAll(tasks);

            return _browser;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task WarmUpPageAsync()
    {
        var page = await _browser!.NewPageAsync();
        await ConfigurePageAsync(page);
        _pagePool.Add(page);
    }

    private static async Task ConfigurePageAsync(IPage page)
    {
        await page.SetRequestInterceptionAsync(true);
        page.Request += (_, e) =>
        {
            if (e.Request.Url.StartsWith("data:") || e.Request.Url == "about:blank")
                _ = e.Request.ContinueAsync();
            else
                _ = e.Request.AbortAsync();
        };
    }

    public async Task<byte[]> GenerateAsync(string html, CancellationToken ct = default)
    {
        await GetBrowserAsync();
        await _poolLock.WaitAsync(ct);

        // Pega página do pool ou cria uma nova se o pool estiver vazio por algum erro
        if (!_pagePool.TryTake(out var page))
        {
            page = await _browser!.NewPageAsync();
            await ConfigurePageAsync(page);
        }

        try
        {
            await page.SetContentAsync(html, new NavigationOptions
            {
                Timeout = 60_000,
                WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
            });

            return await page.PdfDataAsync(new PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                MarginOptions = new MarginOptions
                {
                    Top = "2mm",
                    Bottom = "2mm",
                    Left = "2mm",
                    Right = "2mm"
                }
            });
        }
        finally
        {
            // Devolve a página ao pool em vez de fechar
            _pagePool.Add(page);
            _poolLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        while (_pagePool.TryTake(out var page))
            await page.CloseAsync();

        if (_browser != null)
            await _browser.CloseAsync();

        _initLock.Dispose();
        _poolLock.Dispose();
    }
}
