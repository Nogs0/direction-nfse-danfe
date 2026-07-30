using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Direction.NFSe.Danfe;
using Xunit;

namespace Danfe.Tests;

public class GoldenHtmlTests
{
    public static IEnumerable<object[]> Cenarios()
    {
        // Cenários cobrindo os itens de aceite 4.4-4.8/5.1: regular, cancelada, substituída,
        // produção restrita, participantes opcionais ausentes, destinatário == tomador,
        // ISSQN não incidente, IBS/CBS completo, informações complementares extensas.
        yield return new object[] { "nfse-completa", DanfeEnvironment.Production, false, false };
        yield return new object[] { "nfse-completa", DanfeEnvironment.Production, true, false };
        yield return new object[] { "nfse-completa", DanfeEnvironment.Production, false, true };
        yield return new object[] { "nfse-completa", DanfeEnvironment.Restricted, false, false };
        yield return new object[] { "nfse-minima", DanfeEnvironment.Production, false, false };
        yield return new object[] { "nfse-destinatario-igual-tomador", DanfeEnvironment.Production, false, false };
    }

    [Theory]
    [MemberData(nameof(Cenarios))]
    public void Html_snapshot_should_match(string fixture, DanfeEnvironment environment, bool isCancelled, bool isReplaced)
    {
        var nfse = DeserializeFixture(fixture);

        var renderer = new DanfeHtmlRenderer(new DanfeOptions());
        var (html, _) = renderer.Render(nfse, environment, isCancelled, isReplaced);

        var normalized = HtmlNormalization.Normalize(html);

        var situacao = isCancelled ? "cancelada" : isReplaced ? "substituida" : "regular";
        var scenarioName = $"{fixture}_{environment}_{situacao}";
        var approvedPath = Path.Combine(AppContext.BaseDirectory, "Approved", scenarioName + ".approved.html");

        if (!File.Exists(approvedPath))
        {
            // Primeira execução para este cenário: grava o snapshot para revisão humana (git diff)
            // em vez de falhar. O arquivo deve ser commitado após revisão manual do HTML gerado.
            Directory.CreateDirectory(Path.GetDirectoryName(approvedPath)!);
            File.WriteAllText(approvedPath, normalized);
        }

        var approved = File.ReadAllText(approvedPath);
        Assert.Equal(approved, normalized);
    }

    // Sanity check básico do PDF (README: "validação básica do PDF"). Depende de Chromium real.
    [Trait("Category", "Integration")]
    [Fact]
    public async System.Threading.Tasks.Task Pdf_sanity_check()
    {
        var nfse = DeserializeFixture("nfse-completa");

        await using var pdfGenerator = new DanfePdfGenerator();
        var service = new DanfeService(new DanfeOptions(), pdfGenerator);

        var result = await service.GenerateAsync(nfse, DanfeEnvironment.Production);

        Assert.NotEmpty(result.PdfBytes);
        Assert.Equal((byte)'%', result.PdfBytes[0]);
        Assert.Equal((byte)'P', result.PdfBytes[1]);
        Assert.Equal((byte)'D', result.PdfBytes[2]);
        Assert.Equal((byte)'F', result.PdfBytes[3]);
    }

    private static NFSeSchema DeserializeFixture(string fixture)
    {
        var xmlPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture + ".xml");
        var xml = File.ReadAllText(xmlPath);

        var serializer = new XmlSerializer(typeof(NFSeSchema));
        using var sr = new StringReader(xml);
        return (NFSeSchema)serializer.Deserialize(sr)!;
    }
}
