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
        yield return new object[] { "nfse-completa-substituida", DanfeEnvironment.Production, false, true };
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
    // uma 2ª página inteira (feedback #2357949) — e, num caso residual (NFS-e Substituída com
    // referência à nota substituída somada aos demais blocos opcionais), continuou acontecendo
    // mesmo após aquele ajuste (feedback #2358429). A garantia definitiva agora vem do
    // encolhimento automático em DanfePdfGenerator (ComputeScaleToFitOnePage); este teste mede a
    // altura renderizada — com o viewport ajustado à largura real de impressão, não à largura
    // padrão do Puppeteer — contra o piso desse encolhimento, para avisar antes que um caso
    // extremo ultrapasse até essa margem e volte a cair em 2 páginas.
    [Trait("Category", "Integration")]
    [Theory]
    [InlineData("nfse-completa", DanfeEnvironment.Production, false, false)]
    [InlineData("nfse-completa", DanfeEnvironment.Production, true, false)]
    [InlineData("nfse-completa", DanfeEnvironment.Production, false, true)]
    [InlineData("nfse-completa", DanfeEnvironment.Restricted, false, false)]
    [InlineData("nfse-completa-substituida", DanfeEnvironment.Production, false, true)]
    public async System.Threading.Tasks.Task MassaCompleta_ComCanhoto_CabeEmUmaUnicaPaginaA4(
        string fixture, DanfeEnvironment environment, bool isCancelled, bool isReplaced)
    {
        var alturaMaximaComEncolhimento = DanfePdfGenerator.PageContentHeightPx / DanfePdfGenerator.MinScale;

        var nfse = DeserializeFixture(fixture);
        var renderer = new DanfeHtmlRenderer(new DanfeOptions());
        var (html, _) = renderer.Render(nfse, environment, isCancelled, isReplaced);

        await using var pdfGenerator = new DanfePdfGenerator();
        var browser = await pdfGenerator.GetBrowserAsync();
        var page = await browser.NewPageAsync();
        try
        {
            await page.SetViewportAsync(new PuppeteerSharp.ViewPortOptions
            {
                Width = (int)Math.Round(DanfePdfGenerator.PageContentWidthPx),
                Height = (int)Math.Round(DanfePdfGenerator.PageContentHeightPx)
            });
            await page.SetContentAsync(html);
            var height = await page.EvaluateExpressionAsync<double>("document.body.scrollHeight");
            Assert.True(
                height <= alturaMaximaComEncolhimento,
                $"Conteúdo ({height}px) excede até o piso de encolhimento automático " +
                $"({alturaMaximaComEncolhimento:F1}px) — o canhoto ou outro bloco ainda ficaria em uma " +
                "2ª página mesmo com o ajuste automático de escala.");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    // Reprodução direta do cenário residual de #2358429 (bug 4): NFS-e Substituída com todos os
    // blocos opcionais preenchidos + referência à nota substituída — confirma o PDF real gerado
    // pelo pipeline completo (viewport correto + encolhimento automático), não apenas a proxy de
    // scrollHeight acima.
    [Trait("Category", "Integration")]
    [Fact]
    public async System.Threading.Tasks.Task Substituida_ComReferenciaEBlocosOpcionais_GeraPdfValido()
    {
        var nfse = DeserializeFixture("nfse-completa-substituida");
        var renderer = new DanfeHtmlRenderer(new DanfeOptions());
        var (html, _) = renderer.Render(nfse, DanfeEnvironment.Production, false, true);

        await using var pdfGenerator = new DanfePdfGenerator();
        var pdf = await pdfGenerator.GenerateAsync(html);

        Assert.NotEmpty(pdf);
        Assert.Equal((byte)'%', pdf[0]);
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
