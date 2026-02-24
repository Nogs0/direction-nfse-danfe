using System;
using System.IO;
using System.Xml.Serialization;

namespace Direction.NFSe.Danfe;

public sealed class DanfeService
{
    private readonly DanfeHtmlRenderer _renderer;

    public DanfeService(DanfeOptions? options = null)
    {
        options ??= new DanfeOptions();
        _renderer = new DanfeHtmlRenderer(options);
    }

    public DanfeResult Generate(NFSeSchema nfse, DanfeEnvironment environment, bool isCancelled = false, bool isReplaced = false)
    {
        var (html, warnings) = _renderer.Render(nfse, environment, isCancelled, isReplaced);
        var pdfBytes = DanfePdfGenerator.Generate(html);

        return new DanfeResult
        {
            Environment = environment,
            Html = html,
            PdfBytes = pdfBytes,
            Warnings = warnings
        };
    }

    public DanfeResult Generate(string xml, DanfeEnvironment environment, bool isCancelled = false)
    {
        using var sr = new StringReader(xml);
        var nfse = Deserialize(sr);
        return Generate(nfse, environment, isCancelled);
    }

    public DanfeResult Generate(string xml, DanfeEnvironment environment, DanfeStatus status)
    {
        using var sr = new StringReader(xml);
        var nfse = Deserialize(sr);
        return Generate(nfse, environment, status == DanfeStatus.Cancelada, status == DanfeStatus.Substituida);
    }

    public DanfeResult Generate(Stream xmlStream, DanfeEnvironment environment, bool isCancelled = false)
    {
        using var sr = new StreamReader(xmlStream);
        var nfse = Deserialize(sr);
        return Generate(nfse, environment, isCancelled);
    }

    private static NFSeSchema Deserialize(TextReader reader)
    {
        var serializer = new XmlSerializer(typeof(NFSeSchema));
        var obj = serializer.Deserialize(reader);
        if (obj is not NFSeSchema nfse)
            throw new InvalidOperationException("Falha ao desserializar NFSeSchema.");

        return nfse;
    }
}
