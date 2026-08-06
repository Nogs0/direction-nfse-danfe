using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<DanfePdfGenerator> _logger;

    public DanfePdfGenerator(int poolSize = 0, ILogger<DanfePdfGenerator>? logger = null)
    {
        _poolSize = poolSize > 0 ? poolSize : Environment.ProcessorCount;
        _poolLock = new SemaphoreSlim(_poolSize, _poolSize);
        _logger = logger ?? NullLogger<DanfePdfGenerator>.Instance;
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

            // Recursos da execução anterior (se houver) não podem sobreviver ao relaunch.
            await DrenarPoolAsync();

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
                    "--disable-gpu"
                }
            });

            _logger.LogInformation("Navegador Chromium (re)inicializado para geração de DANFSe.");

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

    private async Task DrenarPoolAsync()
    {
        var descartadas = 0;
        while (_pagePool.TryTake(out var antiga))
        {
            await DescartarAsync(antiga);
            descartadas++;
        }

        if (descartadas > 0)
        {
            _logger.LogWarning(
                "Pool de páginas drenado antes da (re)inicialização do navegador: {Quantidade} página(s) órfã(s) descartada(s).",
                descartadas);
        }
    }

    private async Task DescartarAsync(IPage page)
    {
        try
        {
            if (!page.IsClosed)
                await page.CloseAsync();
        }
        catch (Exception ex)
        {
            // A página já pode estar morta (processo do renderer caiu); não há o que fazer além de logar.
            _logger.LogDebug(ex, "Falha ao descartar página de renderização (provavelmente já inutilizada).");
        }
    }

    // Lógica pura de decisão do pool: sem dependência de IPage/IBrowser, testável isoladamente.
    internal bool DeveReciclarPagina(bool sucesso, bool paginaFechada, int quantidadeAtualNoPool)
        => sucesso && !paginaFechada && quantidadeAtualNoPool < _poolSize;

    // Área útil de uma página A4 (297mm) menos as margens superior/inferior de 2mm usadas acima,
    // convertida para px a 96dpi — mesma referência usada para medir document.body.scrollHeight.
    internal const double PageContentHeightPx = (297 - 2 * 2) * 96 / 25.4;

    // Largura útil da mesma página A4 (210mm menos as margens esquerda/direita de 2mm) — o
    // viewport da página precisa ser ajustado a essa largura antes de medir scrollHeight, senão
    // a medição usa a largura padrão do Puppeteer (800px) em vez da largura real de impressão,
    // subestimando a quebra de linha e, com isso, a altura real do conteúdo impresso.
    internal const double PageContentWidthPx = (210 - 2 * 2) * 96 / 25.4;

    // Piso de encolhimento: nunca reduzir a impressão além de ~8%, para não violar os tamanhos
    // mínimos de fonte/campo do Anexo I da NT-008 (regra 4.8.6). Casos que precisassem de mais
    // que isso para caber ficam com o canhoto em página própria, como já aceito em #2357949.
    internal const double MinScale = 0.92;

    // Lógica pura de cálculo de escala: sem dependência de IPage, testável isoladamente.
    internal static double ComputeScaleToFitOnePage(double contentHeightPx)
    {
        if (contentHeightPx <= PageContentHeightPx)
            return 1.0;

        var escalaNecessaria = PageContentHeightPx / contentHeightPx;
        return Math.Max(escalaNecessaria, MinScale);
    }

    private async Task WarmUpPageAsync()
    {
        var page = await _browser!.NewPageAsync();
        await ConfigurePageAsync(page);
        _pagePool.Add(page);
    }

    private static async Task ConfigurePageAsync(IPage page)
    {
        await page.SetViewportAsync(new ViewPortOptions
        {
            Width = (int)Math.Round(PageContentWidthPx),
            Height = (int)Math.Round(PageContentHeightPx)
        });
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

        var sucesso = false;
        try
        {
            await page.SetContentAsync(html, new NavigationOptions
            {
                Timeout = 60_000,
                WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }
            });

            // Regra 4.8.6 exige uma única página A4 mesmo em combinações de blocos opcionais
            // não previstas nos cenários de teste (ex.: NFS-e Substituída com todos os blocos
            // preenchidos, onde diferenças de fonte entre ambientes podem consumir a margem de
            // segurança do template). Em vez de perseguir cada novo caso limite ajustando
            // espaçamento, mede-se a altura renderizada e encolhe-se a impressão (dentro de um
            // piso que preserva os tamanhos mínimos de fonte do Anexo I) o suficiente para caber.
            var scrollHeight = await page.EvaluateExpressionAsync<double>("document.body.scrollHeight");
            var scale = ComputeScaleToFitOnePage(scrollHeight);

            var pdf = await page.PdfDataAsync(new PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                Scale = (decimal)scale,
                MarginOptions = new MarginOptions
                {
                    Top = "2mm",
                    Bottom = "2mm",
                    Left = "2mm",
                    Right = "2mm"
                }
            });

            sucesso = true;
            return pdf;
        }
        finally
        {
            // Página com falha (timeout/erro de conversão/renderer caído) nunca é reaproveitada;
            // e o pool nunca cresce além de _poolSize.
            if (DeveReciclarPagina(sucesso, page.IsClosed, _pagePool.Count))
            {
                _pagePool.Add(page);
            }
            else
            {
                if (!sucesso)
                    _logger.LogWarning("Falha na renderização do DANFSe; página descartada em vez de reaproveitada.");

                await DescartarAsync(page);
            }

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
