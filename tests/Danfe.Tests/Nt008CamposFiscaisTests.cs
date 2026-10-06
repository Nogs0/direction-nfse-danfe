using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Serialization;
using Direction.NFSe.Danfe;
using Xunit;

namespace Danfe.Tests;

// #2366209 — campos fiscais do DANFS-e conforme a NT 008 v1.02 (itens 4.1 a 4.8).
// Base: fixture sintética com os valores da nota nº 59; cada teste varia só o que exercita.
public class Nt008CamposFiscaisTests
{
    private static readonly XNamespace N = "http://www.sped.fazenda.gov.br/nfse";
    private const string Fixture = "nfse-caso-2366209";

    // ---- Nota nº 59 (aceites dos itens 4.1 a 4.8) ----

    [Fact]
    public void Caso2366209_ReproduzODanfseOficial()
    {
        var texto = RenderTexto();

        Assert.Contains("Descrição Contrib. Sociais - Retidas 3 PIS/COFINS/CSLL Retidos", texto);
        Assert.Contains("Contribuições Sociais - Retidas R$ 465,00", texto);
        Assert.Contains("PIS - Débito Apuração Própria R$ 65,00", texto);
        Assert.Contains("COFINS - Débito Apuração Própria R$ 300,00", texto);
        Assert.Contains("Total das Retenções (ISSQN / Federais) R$ 615,00", texto);
        Assert.Contains("Exclusões e Reduções da Base de Cálculo R$ 665,00", texto);
        Assert.Contains("Base de Cálculo Após Exclusões e Reduções R$ 9.335,00", texto);
        Assert.Contains("Município Incidência / Sigla UF 020101 / 4205407 / Florianópolis / SC", texto);
        Assert.Contains("Finalidade NFS-e regular", texto);
        Assert.Contains("Data e Hora da Emissão da NFS-e 21/08/2026 17:41:37", texto);
        Assert.DoesNotContain("Total Tributação Federal", texto);
        Assert.DoesNotContain("R$ 680,00", texto);
        Assert.DoesNotContain("R$ 980,00", texto);
    }

    // ---- 4.1 Descrição das contribuições sociais retidas ----

    [Theory]
    [InlineData(1, "1 PIS/COFINS Retidos")]
    [InlineData(3, "3 PIS/COFINS/CSLL Retidos")]
    [InlineData(4, "4 PIS/COFINS Retidos, CSLL Não Retido")]
    [InlineData(5, "5 PIS Retido, COFINS/CSLL Não Retidos")]
    [InlineData(6, "6 COFINS Retido, PIS/CSLL Não Retidos")]
    [InlineData(7, "7 COFINS/CSLL Retidos, PIS Não Retido")]
    [InlineData(8, "8 CSLL Retido, PIS/COFINS Não Retidos")]
    [InlineData(9, "9 PIS/CSLL Retidos, COFINS Não Retido")]
    public void DescricaoContribSociais_CodigoSuportado_UsaTabelaVigente(int codigo, string esperado)
    {
        var texto = RenderTexto(x => Dps(x, "tribFed", "piscofins", "tpRetPisCofins").Value = codigo.ToString());

        Assert.Contains($"Descrição Contrib. Sociais - Retidas {esperado}", texto);
        Assert.Contains("Contribuições Sociais - Retidas R$ 465,00", texto); // vRetCSLL impresso como informado, qualquer código
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2")]
    public void DescricaoContribSociais_CodigoForaDaTabelaVigente_ExibeTraco(string codigo)
    {
        var texto = RenderTexto(x => Dps(x, "tribFed", "piscofins", "tpRetPisCofins").Value = codigo);

        Assert.Contains("Descrição Contrib. Sociais - Retidas -", texto);
        Assert.DoesNotContain("PIS Retido/COFINS Não Retido", texto);
    }

    // ---- 4.3 Total das retenções ----

    [Fact]
    public void TotalRetencoes_ComponentesAlterados_ReproduzVTotalRet()
    {
        var texto = RenderTexto(x =>
        {
            Dps(x, "tribFed", "vRetIRRF").Value = "999.99";
            Dps(x, "tribFed", "vRetCSLL").Value = "1.00";
            Dps(x, "tribFed", "piscofins", "vPis").Value = "7.00";
        });

        Assert.Contains("Total das Retenções (ISSQN / Federais) R$ 615,00", texto);
    }

    [Fact]
    public void TotalRetencoes_VTotalRetAusente_ExibeTraco()
    {
        var texto = RenderTexto(x => x.Root!.Element(N + "infNFSe")!.Element(N + "valores")!.Element(N + "vTotalRet")!.Remove());

        Assert.Contains("Total das Retenções (ISSQN / Federais) -", texto);
    }

    // ---- 4.4 Exclusões e reduções da base de cálculo ----

    [Theory]
    [InlineData("vDescIncond", "R$ 675,00")]     // +10 sobre 665
    [InlineData("vCalcReeRepRes", "R$ 675,00")]
    [InlineData("vISSQN", "R$ 375,00")]          // 300 -> 10
    [InlineData("vPis", "R$ 610,00")]            // 65 -> 10
    [InlineData("vCofins", "R$ 375,00")]         // 300 -> 10
    public void ExclusoesReducoes_CadaParcela_EntraNaSoma(string parcela, string esperado)
    {
        var texto = RenderTexto(x => ElementoDaParcela(x, parcela).Value = "10.00");

        Assert.Contains($"Exclusões e Reduções da Base de Cálculo {esperado}", texto);
        Assert.Contains("Base de Cálculo Após Exclusões e Reduções R$ 9.335,00", texto);
    }

    [Fact]
    public void ExclusoesReducoes_ParcelasAusentes_ContamComoZero()
    {
        var texto = RenderTexto(x =>
        {
            ElementoDaParcela(x, "vPis").Remove();
            ElementoDaParcela(x, "vCofins").Remove();
        });

        Assert.Contains("Exclusões e Reduções da Base de Cálculo R$ 300,00", texto);
    }

    // ---- 4.5 Indicador de operação ----

    [Fact]
    public void IndicadorOperacao_PresenteNaNfse_TemPrecedenciaSobreADps()
    {
        var texto = RenderTexto(x => InfNfseIbsCbs(x).AddFirst(new XElement(N + "cIndOp", "030101")));

        Assert.Contains("Município Incidência / Sigla UF 030101 / 4205407 / Florianópolis / SC", texto);
    }

    [Fact]
    public void IndicadorOperacao_AusenteEmAmbos_ExibeComponentesDeLocalidade()
    {
        var texto = RenderTexto(x => x.Descendants(N + "cIndOp").Remove());

        Assert.Contains("Município Incidência / Sigla UF - / 4205407 / Florianópolis / SC", texto);
    }

    // ---- 4.6 Finalidade ----

    [Theory]
    [InlineData("0", "NFS-e regular")]
    [InlineData("1", "-")] // enumeração antiga (1 a 4) não pertence ao leiaute vigente
    [InlineData("4", "-")]
    public void Finalidade_UsaEnumeracaoVigente(string finNFSe, string esperado)
    {
        var texto = RenderTexto(x => DpsIbsCbs(x).Element(N + "finNFSe")!.Value = finNFSe);

        Assert.Contains($"Finalidade {esperado} ", texto);
    }

    [Fact]
    public void Finalidade_Ausente_ExibeTraco()
    {
        var texto = RenderTexto(x => DpsIbsCbs(x).Element(N + "finNFSe")!.Remove());

        Assert.Contains("Finalidade - ", texto);
    }

    // ---- 4.7 Data e hora ----

    [Theory]
    [InlineData("2026-08-21T17:41:37-03:00", "21/08/2026 17:41:37")]
    [InlineData("2026-08-21T17:41:37-05:00", "21/08/2026 17:41:37")]
    [InlineData("2026-08-21T23:59:59+02:00", "21/08/2026 23:59:59")]
    [InlineData("2026-08-21T00:00:01Z", "21/08/2026 00:00:01")]
    public void DataHora_PreservaRelogioDoOffsetInformado(string valorXml, string esperado)
    {
        var texto = RenderTexto(x =>
        {
            x.Root!.Element(N + "infNFSe")!.Element(N + "dhProc")!.Value = valorXml;
            x.Descendants(N + "dhEmi").Single().Value = valorXml;
        });

        Assert.Contains($"Data e Hora da Emissão da NFS-e {esperado}", texto);
        Assert.Contains($"Data e Hora da Emissão da DPS {esperado}", texto);
    }

    // ---- 4.8 Total Tributação Federal removido ----

    [Theory]
    [InlineData("nfse-completa")]
    [InlineData("nfse-minima")]
    [InlineData(Fixture)]
    public void TotalTributacaoFederal_NaoEhExibido(string fixture)
    {
        Assert.DoesNotContain("Total Tributação Federal", RenderTexto(fixture: fixture));
    }

    [Fact]
    public void TemplateCustomizado_ComFedTotalLegado_RemovePlaceholderComWarning()
    {
        var templatePadrao = Path.Combine(AppContext.BaseDirectory, "Assets", "Templates", "Danfe.html");
        var templateLegado = Path.Combine(Path.GetTempPath(), $"danfe-legado-{Guid.NewGuid():N}.html");
        File.WriteAllText(templateLegado, File.ReadAllText(templatePadrao)
            + "<td>Total Tributação Federal {{FED_TOTAL}}</td><td>{{TOKEN_DO_CONSUMIDOR}}</td>");
        try
        {
            var (html, warnings) = new DanfeHtmlRenderer(new DanfeOptions { TemplatePath = templateLegado })
                .Render(Desserializar(XDocument.Load(CaminhoFixture(Fixture))), DanfeEnvironment.Production);

            Assert.DoesNotContain("{{FED_TOTAL}}", html);
            Assert.Contains(warnings, w => w.Message.Contains("{{FED_TOTAL}}"));
            Assert.Contains("{{TOKEN_DO_CONSUMIDOR}}", html); // placeholders próprios do consumidor são preservados
        }
        finally
        {
            File.Delete(templateLegado);
        }
    }

    [Fact]
    public void DadosDoXml_ComTextoEmFormatoDePlaceholder_SaoPreservados()
    {
        // {{TOTAL_RETENCOES}} é um placeholder real do template: não pode ser substituído dentro do dado do XML.
        var texto = RenderTexto(x => x.Descendants(N + "xDescServ").Single().Value = "ver {{TOTAL_RETENCOES}} e {{OUTRO}}");

        Assert.Contains("ver {{TOTAL_RETENCOES}} e {{OUTRO}}", texto);
    }

    [Theory]
    [InlineData("Florianópolis - SC")]
    [InlineData("Florianópolis-SC")]
    [InlineData("Florianópolis/SC")]
    [InlineData("Florianópolis (SC)")]
    [InlineData("Florianópolis - sc")]
    public void IndicadorOperacao_XLocalidadeJaComUf_NaoRepeteUf(string xLocalidadeIncid)
    {
        var texto = RenderTexto(x => InfNfseIbsCbs(x).Element(N + "xLocalidadeIncid")!.Value = xLocalidadeIncid);

        Assert.Contains("Sigla UF 020101 / 4205407 / Florianópolis / SC", texto);
    }

    [Fact]
    public void Finalidade_NotaSemGrupoIbsCbs_NaoEmiteWarning()
    {
        var doc = XDocument.Load(CaminhoFixture("nfse-minima"));

        var (_, warnings) = new DanfeHtmlRenderer(new DanfeOptions()).Render(Desserializar(doc), DanfeEnvironment.Production);

        Assert.DoesNotContain(warnings, w => w.Message.Contains("finNFSe"));
    }

    [Fact]
    public void DataHora_FormatoInvalido_EmiteWarningDeFormato()
    {
        var doc = XDocument.Load(CaminhoFixture(Fixture));
        doc.Root!.Element(N + "infNFSe")!.Element(N + "dhProc")!.Value = "21/08/2026 17:41:37";

        var (_, warnings) = new DanfeHtmlRenderer(new DanfeOptions()).Render(Desserializar(doc), DanfeEnvironment.Production);

        Assert.Contains(warnings, w => w.Message.Contains("dhProc '21/08/2026 17:41:37' não reconhecido como data/hora"));
        Assert.DoesNotContain(warnings, w => w.Code == "NFSE_FIELD_MISSING" && w.Path == "infNFSe.dhProc");
    }

    [Fact]
    public void TotalRetencoes_VTotalRetAusente_NaoEmiteWarning()
    {
        var doc = XDocument.Load(CaminhoFixture(Fixture));
        doc.Root!.Element(N + "infNFSe")!.Element(N + "valores")!.Element(N + "vTotalRet")!.Remove();

        var (_, warnings) = new DanfeHtmlRenderer(new DanfeOptions()).Render(Desserializar(doc), DanfeEnvironment.Production);

        Assert.DoesNotContain(warnings, w => w.Path == "infNFSe.valores.vTotalRet");
    }

    [Fact]
    public void LocalIncidencia_CodigoComEspacos_NaoInterrompeORender()
    {
        var texto = RenderTexto(x => x.Root!.Element(N + "infNFSe")!.Element(N + "cLocIncid")!.Value = " 4205407 ");

        Assert.Contains("Município de Incidência do ISSQN Florianópolis - SC", texto);
    }

    [Fact]
    public void Finalidade_ForaDaEnumeracaoVigente_EmiteWarning()
    {
        var doc = XDocument.Load(CaminhoFixture(Fixture));
        DpsIbsCbs(doc).Element(N + "finNFSe")!.Value = "1";

        var (_, warnings) = new DanfeHtmlRenderer(new DanfeOptions()).Render(Desserializar(doc), DanfeEnvironment.Production);

        Assert.Contains(warnings, w => w.Message.Contains("finNFSe '1'"));
    }

    [Fact]
    public void DataHora_Ausente_EmiteUmUnicoWarning()
    {
        var doc = XDocument.Load(CaminhoFixture(Fixture));
        doc.Root!.Element(N + "infNFSe")!.Element(N + "dhProc")!.Remove();

        var (_, warnings) = new DanfeHtmlRenderer(new DanfeOptions()).Render(Desserializar(doc), DanfeEnvironment.Production);

        Assert.Single(warnings, w => w.Path == "infNFSe.dhProc");
    }

    [Fact]
    public void IndicadorOperacao_LocalidadeDesconhecida_EmiteWarnings()
    {
        var doc = XDocument.Load(CaminhoFixture(Fixture));
        InfNfseIbsCbs(doc).Element(N + "cLocalidadeIncid")!.Value = "9999999";
        doc.Descendants(N + "cIndOp").Remove();

        var (_, warnings) = new DanfeHtmlRenderer(new DanfeOptions()).Render(Desserializar(doc), DanfeEnvironment.Production);

        Assert.Contains(warnings, w => w.Code == "MUNICIPIO_NOT_FOUND" && w.Path == "infNFSe.IBSCBS.cLocalidadeIncid");
        Assert.Contains(warnings, w => w.Message.Contains("cIndOp"));
    }

    // ---- infraestrutura ----

    private static string CaminhoFixture(string fixture) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture + ".xml");

    private static NFSeSchema Desserializar(XDocument doc)
    {
        using var reader = doc.CreateReader();
        return (NFSeSchema)new XmlSerializer(typeof(NFSeSchema)).Deserialize(reader)!;
    }

    private static string RenderTexto(Action<XDocument>? mutar = null, string fixture = Fixture)
    {
        var doc = XDocument.Load(CaminhoFixture(fixture));
        mutar?.Invoke(doc);

        var (html, _) = new DanfeHtmlRenderer(new DanfeOptions()).Render(Desserializar(doc), DanfeEnvironment.Production);

        var semTags = Regex.Replace(html, "<[^>]+>", " ");
        var texto = WebUtility.HtmlDecode(semTags).Replace(' ', ' ');
        return Regex.Replace(texto, @"\s+", " ");
    }

    private static XElement Dps(XDocument x, params string[] caminhoAPartirDeTrib)
    {
        var atual = x.Descendants(N + "infDPS").Single().Element(N + "valores")!.Element(N + "trib")!;
        foreach (var nome in caminhoAPartirDeTrib)
            atual = atual.Element(N + nome)!;
        return atual;
    }

    private static XElement DpsIbsCbs(XDocument x) => x.Descendants(N + "infDPS").Single().Element(N + "IBSCBS")!;

    private static XElement InfNfseIbsCbs(XDocument x) => x.Root!.Element(N + "infNFSe")!.Element(N + "IBSCBS")!;

    private static XElement ElementoDaParcela(XDocument x, string parcela) => parcela switch
    {
        "vDescIncond" => x.Descendants(N + "vDescIncond").Single(),
        "vCalcReeRepRes" => InfNfseIbsCbs(x).Element(N + "valores")!.Element(N + "vCalcReeRepRes")!,
        "vISSQN" => x.Root!.Element(N + "infNFSe")!.Element(N + "valores")!.Element(N + "vISSQN")!,
        "vPis" => Dps(x, "tribFed", "piscofins", "vPis"),
        "vCofins" => Dps(x, "tribFed", "piscofins", "vCofins"),
        _ => throw new ArgumentOutOfRangeException(nameof(parcela)),
    };
}
