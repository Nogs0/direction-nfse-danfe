using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using HtmlRendererCore.PdfSharp;

namespace Direction.NFSe.Danfe;

public sealed class DanfeService
{
    private readonly DanfeHtmlRenderer _renderer;
    private readonly DanfePdfGenerator _generator;

    public DanfeService(DanfeOptions? options = null, DanfePdfGenerator generator = null)
    {
        options ??= new DanfeOptions();
        _renderer = new DanfeHtmlRenderer(options);
        _generator = generator;
    }

    //public DanfeResult Generate(NFSeSchema nfse, DanfeEnvironment environment, bool isCancelled = false, bool isReplaced = false)
    //{
    //    var (html, warnings) = _renderer.Render(nfse, environment, isCancelled, isReplaced);
    //    var pdfBytes = DanfePdfGenerator.Generate(html);

    //    return new DanfeResult
    //    {
    //        Environment = environment,
    //        Html = html,
    //        PdfBytes = pdfBytes,
    //        Warnings = warnings
    //    };
    //}

    // DanfeService — ajuste para usar async
    public Task<DanfeResult> GenerateAsync(
        string xml,
        DanfeEnvironment environment,
        DanfeStatus status,
        CancellationToken ct = default)
    {
        using var sr = new StringReader(xml);
        var nfse = Deserialize(sr);
        return GenerateAsync(nfse, environment,
            status == DanfeStatus.Cancelada,
            status == DanfeStatus.Substituida,
            ct);
    }

    public async Task<DanfeResult> GenerateAsync(
        NFSeSchema nfse,
        DanfeEnvironment environment,
        bool isCancelled = false,
        bool isReplaced = false,
        CancellationToken ct = default)
    {
        var (html, warnings) = _renderer.Render(nfse, environment, isCancelled, isReplaced);
        var pdfBytes = await _generator.GenerateAsync(html, ct);

        return new DanfeResult
        {
            Environment = environment,
            Html = html,
            PdfBytes = pdfBytes,
            Warnings = warnings
        };
    }

    public DanfeResult Generate(string xml, DanfeEnvironment environment, DanfeStatus status)
        => GenerateAsync(xml, environment, status).GetAwaiter().GetResult();

    public DanfeResult Generate(string xml, DanfeEnvironment environment, bool isCancelled = false, bool isReplaced = false)
    {
        using var sr = new StringReader(xml);
        var nfse = Deserialize(sr);
        return GenerateAsync(nfse, environment, isCancelled, isReplaced).GetAwaiter().GetResult();
    }

    public DanfeResult Generate(Stream xmlStream, DanfeEnvironment environment, bool isCancelled = false)
    {
        using var sr = new StreamReader(xmlStream);
        var nfse = Deserialize(sr);
        return GenerateAsync(nfse, environment, isCancelled).GetAwaiter().GetResult();
    }

    //public DanfeResult Generate(string xml, DanfeEnvironment environment, bool isCancelled = false)
    //{
    //    using var sr = new StringReader(xml);
    //    var nfse = Deserialize(sr);
    //    return Generate(nfse, environment, isCancelled);
    //}

    //public DanfeResult Generate(string xml, DanfeEnvironment environment, bool isCancelled = false, bool isReplaced = false)
    //{
    //    using var sr = new StringReader(xml);
    //    var nfse = Deserialize(sr);
    //    return Generate(nfse, environment, isCancelled, isReplaced);
    //}

    //public DanfeResult Generate(string xml, DanfeEnvironment environment, DanfeStatus status)
    //{
    //    using var sr = new StringReader(xml);
    //    var nfse = Deserialize(sr);
    //    return Generate(nfse, environment, status == DanfeStatus.Cancelada, status == DanfeStatus.Substituida);
    //}

    //public DanfeResult Generate(Stream xmlStream, DanfeEnvironment environment, bool isCancelled = false)
    //{
    //    using var sr = new StreamReader(xmlStream);
    //    var nfse = Deserialize(sr);
    //    return Generate(nfse, environment, isCancelled);
    //}

    private static NFSeSchema Deserialize(TextReader reader)
    {
        var serializer = new XmlSerializer(typeof(NFSeSchema));
        var obj = serializer.Deserialize(reader);
        if (obj is not NFSeSchema nfse)
            throw new InvalidOperationException("Falha ao desserializar NFSeSchema.");

        return nfse;
    }
}
