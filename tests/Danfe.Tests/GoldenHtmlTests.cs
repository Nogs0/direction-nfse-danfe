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

    // Regra 4.8.6 exige uma única página A4 mesmo com todos os blocos opcionais e descrição
    // extensa preenchidos; o canhoto (habilitado por padrão) chegou a empurrar o conteúdo para
    // uma 2ª página inteira (feedback #2357949). Mede a altura renderizada contra a área útil de
    // uma página A4 (297mm menos 2mm de margem em cada lado, a 96dpi) em vez de inspecionar os
    // bytes do PDF gerado (Chromium usa xref/object streams comprimidos, não é trivial de parsear).
    [Trait("Category", "Integration")]
    [Theory]
    [InlineData(DanfeEnvironment.Production, false, false)]
    [InlineData(DanfeEnvironment.Production, true, false)]
    [InlineData(DanfeEnvironment.Production, false, true)]
    [InlineData(DanfeEnvironment.Restricted, false, false)]
    public async System.Threading.Tasks.Task MassaCompleta_ComCanhoto_CabeEmUmaUnicaPaginaA4(
        DanfeEnvironment environment, bool isCancelled, bool isReplaced)
    {
        const double AlturaUtilA4Px = (297 - 2 * 2) * 96 / 25.4;

        var nfse = DeserializeFixture("nfse-completa");
        var renderer = new DanfeHtmlRenderer(new DanfeOptions());
        var (html, _) = renderer.Render(nfse, environment, isCancelled, isReplaced);

        await using var pdfGenerator = new DanfePdfGenerator();
        var browser = await pdfGenerator.GetBrowserAsync();
        var page = await browser.NewPageAsync();
        try
        {
            await page.SetContentAsync(html);
            var height = await page.EvaluateExpressionAsync<double>("document.body.scrollHeight");
            Assert.True(
                height <= AlturaUtilA4Px,
                $"Conteúdo ({height}px) excede a área útil de uma página A4 ({AlturaUtilA4Px:F1}px) — " +
                "o canhoto ou outro bloco pode estar transbordando para uma 2ª página.");
        }
        finally
        {
            await page.CloseAsync();
        }
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
