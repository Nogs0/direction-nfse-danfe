using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Direction.NFSe.Danfe;

public sealed class DanfeHtmlRenderer
{
    private const string TransparentPixelBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";

    private readonly string _templatePath;
    private readonly DanfeOptions _options;
    private readonly string _templateContent;
    private static int _municipiosInitialized = 0;

    public DanfeHtmlRenderer(DanfeOptions options)
    {
        _options = options ?? new DanfeOptions();

        var basePath = _options.BasePath ?? AppContext.BaseDirectory;
        _templatePath = _options.TemplatePath ?? Path.Combine(basePath, "Assets", "Templates", "Danfe.html");
        _templateContent = File.ReadAllText(_templatePath, Encoding.UTF8);

        // Inicializa municípios uma única vez, thread-safe
        if (_options.AutoInitializeMunicipios)
        {
            InitializeMunicipiosOnce(basePath);
        }
    }
    private void InitializeMunicipiosOnce(string basePath)
    {
        // Interlocked garante que só uma thread inicializa
        if (Interlocked.CompareExchange(ref _municipiosInitialized, 1, 0) == 0)
        {
            var estados = _options.EstadosCsvPath ?? Path.Combine(basePath, "Assets", "estados.csv");
            var municipios = _options.MunicipiosCsvPath ?? Path.Combine(basePath, "Assets", "municipios.csv");
            MunicipiosIbge.Initialize(estados, municipios);
        }
    }

    public (string Html, IReadOnlyList<DanfeWarning> Warnings) Render(NFSeSchema nfse, DanfeEnvironment environment, bool isCancelled = false, bool isReplaced = false)
    {
        if (nfse == null) throw new ArgumentNullException(nameof(nfse));
        if (nfse.infNFSe == null) throw new ArgumentException("NFSe.infNFSe não pode ser nulo", nameof(nfse));

        var warnings = new DanfeWarningCollector();

        var isProd = environment == DanfeEnvironment.Production;

        var validade = isProd ? "" : "NFS-e SEM VALIDADE JURÍDICA";
        var template = _templateContent;

        // Root shortcuts (evita repetir cadeia e facilita paths)
        var inf = nfse.infNFSe;
        var dps = inf.DPS;
        var infDps = dps?.InfDPS;
        var valores = inf.valores;

        // Campos básicos (se algum deles for crítico e estiver nulo, melhor lançar)
        if (dps == null) throw new ArgumentException("NFSe.infNFSe.DPS não pode ser nulo", nameof(nfse));
        if (infDps == null) throw new ArgumentException("NFSe.infNFSe.DPS.InfDPS não pode ser nulo", nameof(nfse));
        if (string.IsNullOrWhiteSpace(inf.Id)) throw new ArgumentException("NFSe.infNFSe.Id não pode ser nulo/vazio", nameof(nfse));

        string numeroNfse = inf.nNFSe.ToString();
        string numeroDps = infDps.nDPS.ToString();
        string serieDps = infDps.serie.ToString();

        DateTime? competencia = Helper.TryParseDate(infDps.dCompet);
        if (!competencia.HasValue) warnings.FieldMissing("dCompet", "infNFSe.DPS.InfDPS.dCompet", "-");

        // NT-008 4.4.3: a data/hora deve reproduzir a informação do XML, sem redução/acréscimo de fuso.
        DateTime? dhEmissaoNfs = inf.dhProc;
        if (!dhEmissaoNfs.HasValue) warnings.FieldMissing("dhProc", "infNFSe.dhProc", "-");

        DateTime? dhEmissaoDps = Helper.TryParseDateTime(infDps.dhEmi);
        if (!dhEmissaoDps.HasValue) warnings.FieldMissing("dhEmi", "infNFSe.DPS.InfDPS.dhEmi", "-");

        decimal vServico = infDps.valores?.vServPrest?.vServ ?? 0m;
        if (infDps.valores?.vServPrest?.vServ == null) warnings.FieldMissing("vServPrest.vServ", "infNFSe.DPS.InfDPS.valores.vServPrest.vServ", "0,00");

        decimal vDescCond = infDps.valores?.vDescCondIncond?.vDescCond ?? 0M;
        decimal vDescIncond = infDps.valores?.vDescCondIncond?.vDescIncond ?? 0M;

        string chaveAcesso = inf.Id!.Substring(3);
        if (string.IsNullOrWhiteSpace(chaveAcesso)) warnings.FieldMissing("chaveAcesso", "infNFSe.Id", string.Empty);

        var ptBR = new CultureInfo("pt-BR");

        // Tributação (se ficar vazio, warning)
        var cTribNac = infDps.serv?.cServ?.cTribNac;
        var xTribNac = inf.xTribNac;

        var descricaoTributoNacional = $"{Regex.Replace(cTribNac ?? string.Empty, @"(\d{2})(\d{2})(\d{2})", "$1.$2.$3")} - {xTribNac}";

        if (string.IsNullOrWhiteSpace(cTribNac) && string.IsNullOrWhiteSpace(xTribNac)) warnings.FieldMissing("cTribNac/xTribNac", "infNFSe.DPS.InfDPS.serv.cServ.cTribNac | infNFSe.xTribNac", "-");

        var cTribMun = infDps.serv?.cServ?.cTribMun;
        var xTribMun = inf.xTribMun;

        var descricaoTributoMunicipal = string.IsNullOrWhiteSpace(cTribMun) ? (xTribMun ?? string.Empty) : $"{cTribMun} - {xTribMun}";

        if (string.IsNullOrWhiteSpace(descricaoTributoMunicipal)) warnings.FieldMissing("cTribMun/xTribMun", "infNFSe.DPS.InfDPS.serv.cServ.cTribMun | infNFSe.xTribMun", "-");

        // QRCode (NT-008 2.4.3: dimensões mínimas 1,52cm x 1,52cm)
        string url = $"https://www.{(isProd ? "" : "producaorestrita.")}nfse.gov.br/ConsultaPublica/?tpc=1&chave={chaveAcesso}";
        var bytes = Helper.GetQrCode(url);
        var imgQrCodeSrc = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";

        // Municípios (auto init)
        if (_options.AutoInitializeMunicipios)
        {
            var basePath = _options.BasePath ?? AppContext.BaseDirectory;
            var estados = _options.EstadosCsvPath ?? Path.Combine(basePath, "Assets", "estados.csv");
            var municipios = _options.MunicipiosCsvPath ?? Path.Combine(basePath, "Assets", "municipios.csv");
            MunicipiosIbge.Initialize(estados, municipios);
        }

        // Município prestador
        var cLocPrest = infDps.serv?.locPrest?.cLocPrestacao;
        var municipioPrestador = cLocPrest != null ? MunicipiosIbge.GetMunicipio(cLocPrest) : null;
        if (cLocPrest == null)
            warnings.FieldMissing("cLocPrestacao", "infNFSe.DPS.InfDPS.serv.locPrest.cLocPrestacao", "-");
        else if (municipioPrestador == null)
            warnings.MunicipioNotFound("infNFSe.DPS.InfDPS.serv.locPrest.cLocPrestacao");

        var cLocIncid = inf.cLocIncid;
        var municpioISSQN = cLocIncid != null ? MunicipiosIbge.GetMunicipio(Int32.Parse(cLocIncid)) : null;
        if (cLocPrest == null)
            warnings.FieldMissing("cLocIncid", "infNFSe.cLocIncid", "-");
        else if (municpioISSQN == null)
            warnings.MunicipioNotFound("infNFSe.cLocIncid");

        // Município emitente (cabeçalho)
        var municipioEmitente = DanfeFallback.OrDash($"{inf.xLocEmi} - {inf.emit?.enderNac?.UF}", warnings, "Município Emitente", "infNFSe.xLocEmi | infNFSe.emit.enderNac.UF");

        // Logo oficial da NFS-e (cabeçalho)
        var logoNfse = _options.LogoNFSePath != null ? Helper.GetLogo(_options.LogoNFSePath) : Helper.GetLogo(Path.Combine(AppContext.BaseDirectory, "Assets", "Logos", "nfse.png"));

        // Caminhos/valores auxiliares
        int? tpRetIssqn = infDps.valores?.trib?.tribMun?.tpRetISSQN;
        if (tpRetIssqn == null || tpRetIssqn.Value == 0) //verificação necessária pois alguns xml simplesmente não preenchem o BM
            tpRetIssqn = infDps.valores?.trib?.tribMun?.BM?.tpRetISSQN;
        int? opSimpNac = infDps.prest?.regTrib?.opSimpNac;
        int? tpRetPisCofins = infDps.valores?.trib?.tribFed?.piscofins?.tpRetPisCofins;

        decimal? vAliqAplic = valores?.pAliqAplic;
        decimal? vIssqn = valores?.vISSQN;
        decimal? vLiq = valores?.vLiq;
        decimal? vIRRF = infDps.valores?.trib?.tribFed?.vRetIRRF;
        decimal? vCOFINS = infDps.valores?.trib?.tribFed?.piscofins?.vCofins;
        decimal? vPIS = infDps.valores?.trib?.tribFed?.piscofins?.vPis;
        decimal? vCP = infDps.valores?.trib?.tribFed?.vRetCP;
        decimal? vCSLL = infDps.valores?.trib?.tribFed?.vRetCSLL;
        string? outInf = inf.valores?.xOutInf;

        decimal? vTotTribFed = infDps.valores?.trib?.totTrib?.vTotTrib?.vTotTribFed;
        if (vTotTribFed == null || (vTotTribFed.HasValue && vTotTribFed.Value == 0M)) //nem sempre o objeto totalizador é informado no xml
            vTotTribFed = (vIRRF ?? 0M) + (vPIS ?? 0M) + (vCOFINS ?? 0M) + (vCP ?? 0M) + (vCSLL ?? 0M);

        decimal vTotalRetFed = (vIRRF ?? 0M) + (vCP ?? 0M) + (vCSLL ?? 0M);

        decimal vRetPisCofins = 0M;
        switch (tpRetPisCofins)
        {
            case 1: // 1 - PIS/COFINS Retido
                vRetPisCofins = (vPIS ?? 0M) + (vCOFINS ?? 0M);
                break;
            case 2: // 2 - PIS / COFINS Não Retido
            default:
                vRetPisCofins = 0M;
                break;
            case 3: // 3 - PIS Retido / COFINS Não Retido
                vRetPisCofins = (vPIS ?? 0M);
                break;
            case 4: // 4 - PIS Não Retido/ COFINS Retido;
                vRetPisCofins = (vCOFINS ?? 0M);
                break;
        }

        // Verifica se a NFSe está cancelada/substituída (marca d'água, NT-008 2.5.1/2.5.2)
        string canceladaDiv = isCancelled ? BuildMarcaDagua("CANCELADA") : string.Empty;
        if (canceladaDiv == string.Empty)
            warnings.GenericWarning("CanceladaDiv vazia, status da nota não é cancelada");

        string substituidaDiv = (isReplaced && !isCancelled) ? BuildMarcaDagua("SUBSTITUÍDA") : string.Empty;
        if (substituidaDiv == string.Empty)
            warnings.GenericWarning("SubstituidaDiv vazia, status da nota não é substituida");

        // Bloco Tomador/Adquirente (NT-008 Nota 2)
        var toma = infDps.toma;
        var tomadorIdentificado = toma != null && (
            !string.IsNullOrWhiteSpace(toma.CNPJ) ||
            !string.IsNullOrWhiteSpace(toma.CPF) ||
            !string.IsNullOrWhiteSpace(toma.NIF) ||
            !string.IsNullOrWhiteSpace(toma.xNome));

        string blocoTomador = tomadorIdentificado
            ? BuildTomadorBloco(toma!, warnings, ptBR)
            : BuildParticipanteNaoIdentificadoBloco("TOMADOR/ADQUIRENTE", "TOMADOR/ADQUIRENTE DA OPERAÇÃO NÃO IDENTIFICADO NA NFS-e");

        // Bloco Destinatário da Operação (NT-008 Nota 2 e Nota 3)
        var dest = infDps.IBSCBS?.dest;
        var destinatarioIdentificado = dest != null && (
            !string.IsNullOrWhiteSpace(dest.CNPJ) ||
            !string.IsNullOrWhiteSpace(dest.CPF) ||
            !string.IsNullOrWhiteSpace(dest.NIF) ||
            !string.IsNullOrWhiteSpace(dest.xNome));

        var destinatarioEhOProprioTomador = destinatarioIdentificado && tomadorIdentificado && DocumentosIguais(
            dest!.CNPJ, dest.CPF, dest.NIF,
            toma?.CNPJ, toma?.CPF, toma?.NIF);

        string blocoDestinatario;
        if (!destinatarioIdentificado)
            blocoDestinatario = BuildParticipanteNaoIdentificadoBloco("DESTINATÁRIO DA OPERAÇÃO", "DESTINATÁRIO DA OPERAÇÃO NÃO IDENTIFICADO NA NFS-e");
        else if (destinatarioEhOProprioTomador)
            blocoDestinatario = BuildParticipanteNaoIdentificadoBloco("DESTINATÁRIO DA OPERAÇÃO", "O DESTINATÁRIO É O PRÓPRIO TOMADOR/ADQUIRENTE DA OPERAÇÃO");
        else
            blocoDestinatario = BuildDestinatarioBloco(dest!, warnings);

        // Bloco Intermediário da Operação (NT-008 Nota 2)
        var interm = infDps.interm;
        var intermediarioIdentificado = interm != null && (
            !string.IsNullOrWhiteSpace(interm.CNPJ) ||
            !string.IsNullOrWhiteSpace(interm.CPF) ||
            !string.IsNullOrWhiteSpace(interm.NIF) ||
            !string.IsNullOrWhiteSpace(interm.xNome));

        string blocoIntermediario = intermediarioIdentificado
            ? BuildIntermediarioBloco(interm!, warnings)
            : BuildParticipanteNaoIdentificadoBloco("INTERMEDIÁRIO DA OPERAÇÃO", "INTERMEDIÁRIO DA OPERAÇÃO NÃO IDENTIFICADO NA NFS-e");

        // Bloco Tributação Municipal (ISSQN) (NT-008 Nota 4)
        int? tribISSQN = infDps.valores?.trib?.tribMun?.tribISSQN;
        var operacaoTributavelPorIssqn = tribISSQN == 1;

        string blocoTribMunicipal = operacaoTributavelPorIssqn
            ? BuildTribMunicipalBloco(infDps, valores, municpioISSQN, tpRetIssqn, vAliqAplic, vIssqn, vServico, ptBR, warnings)
            : BuildBlocoNaoAplicavel("TRIBUTAÇÃO MUNICIPAL (ISSQN)", "TRIBUTAÇÃO MUNICIPAL (ISSQN) - OPERAÇÃO NÃO SUJEITA AO ISSQN");

        // Bloco Tributação IBS/CBS (suprimido por completo quando o grupo não existir, regra 4.7.5)
        var ibscbsDeclarado = infDps.IBSCBS?.valores?.trib?.gIBSCBS;
        var ibscbsApurado = inf.IBSCBS;
        var possuiIbsCbs = ibscbsDeclarado != null || ibscbsApurado != null;

        string blocoIbsCbs = possuiIbsCbs
            ? BuildIbsCbsBloco(infDps, inf, ptBR, warnings)
            : string.Empty;

        // Totais de IBS/CBS (regra 4.7.4) — sempre reproduzidos do XML, nunca recalculados
        decimal? vIBSTot = ibscbsApurado?.totCIBS?.gIBS?.vIBSTot;
        decimal? vCBSTot = ibscbsApurado?.totCIBS?.gCBS?.vCBS;
        decimal? vTotNF = ibscbsApurado?.totCIBS?.vTotNF;
        decimal? vTotalIbsCbs = (vIBSTot.HasValue || vCBSTot.HasValue) ? (vIBSTot ?? 0M) + (vCBSTot ?? 0M) : (decimal?)null;

        // Informações complementares (regra 4.8.1/4.8.2 — inclui totais aproximados dos tributos, Nota 10)
        var infComplementares = BuildInfComplementares(infDps, inf, ptBR);

        // Canhoto (opcional, NT-008 Nota 11 / regra 4.8.5)
        string blocoCanhoto = _options.ExibirCanhoto
            ? BuildCanhotoBloco(numeroNfse, chaveAcesso)
            : string.Empty;

        // Monta mapa de placeholders (agora com warnings)
        var map = new Dictionary<string, string>
        {
            // Cancelada / Substituída
            ["{{NFSE_CANCELADA_DIV}}"] = canceladaDiv,
            ["{{NFSE_SUBSTITUIDA_DIV}}"] = substituidaDiv,
            // Fonts
            ["{{FONT_FAMILY}}"] = _options.FontFamily ?? "Verdana, Helvetica, sans-serif;",
            ["{{FONT_SIZE}}"] = _options.FontSize ?? "10px;",
            ["{{FONT_SIZE_HEADER}}"] = _options.FontSize ?? "12px;",
            ["{{FONT_SIZE_QRCODE}}"] = _options.FontSize ?? "10px;",

            // Cabeçalho (DANFSe v2.0 — NT-008 Anexo I)
            ["{{NFSE_LOGO}}"] = logoNfse ?? TransparentPixelBase64,
            ["{{VALIDADE_JURIDICA}}"] = validade,
            ["{{CAB_MUNICIPIO}}"] = municipioEmitente,
            ["{{CAB_AMBIENTE_GERADOR}}"] = GetDescricaoAmbienteGerador(inf.ambGer),
            ["{{CAB_TIPO_AMBIENTE}}"] = isProd ? "Produção" : "Produção Restrita (Homologação)",
            ["{{CHAVE_ACESSO}}"] = DanfeFallback.OrDash(chaveAcesso, warnings, fieldName: "chaveAcesso", path: "infNFSe.Id"),

            // QrCode
            ["{{QRCODE_SRC}}"] = imgQrCodeSrc,

            // Dados NFSe / DPS
            ["{{NUMERO_NFSE}}"] = numeroNfse,
            ["{{NUMERO_DPS}}"] = DanfeFallback.OrDash(numeroDps, warnings, "nDPS", "infNFSe.DPS.InfDPS.nDPS"),
            ["{{SERIE_DPS}}"] = DanfeFallback.OrDash(serieDps, warnings, "serie", "infNFSe.DPS.InfDPS.serie"),
            ["{{COMPETENCIA}}"] = competencia?.ToString("dd/MM/yyyy") ?? DanfeFallback.OrDash(null, warnings, "dCompet", "infNFSe.DPS.InfDPS.dCompet"),
            ["{{DATA_HORA_EMISSAO}}"] = dhEmissaoNfs?.ToString("dd/MM/yyyy HH:mm:ss") ?? DanfeFallback.OrDash(null, warnings, "dhProc", "infNFSe.dhProc"),
            ["{{DATA_HORA_EMISSAO_DPS}}"] = dhEmissaoDps?.ToString("dd/MM/yyyy HH:mm:ss") ?? DanfeFallback.OrDash(null, warnings, "dhEmi", "infNFSe.DPS.InfDPS.dhEmi"),
            ["{{EMITENTE_NFSE}}"] = GetDescricaoEmitente(infDps.tpEmit),
            ["{{SITUACAO_NFSE}}"] = GetDescricaoSituacao(isCancelled, isReplaced),
            ["{{FINALIDADE_NFSE}}"] = GetDescricaoFinalidade(inf.IBSCBS?.finNFSe ?? infDps.IBSCBS?.finNFSe),

            // Prestador
            ["{{PREST_CNPJ}}"] = !string.IsNullOrEmpty(infDps.prest?.CPF) ? DanfeFallback.OrDash(Helper.FormatCpf(infDps.prest?.CPF), warnings, fieldName: "CNPJ Prestador", path: "infNFSe.DPS.InfDPS.prest.CPF")
                : DanfeFallback.OrDash(Helper.FormatCnpj(infDps.prest?.CNPJ), warnings, fieldName: "CNPJ Prestador", path: "infNFSe.DPS.InfDPS.prest.CNPJ"),
            ["{{PREST_IM}}"] = DanfeFallback.OrDash(infDps.prest?.IM, warnings, "IM Prestador", "infNFSe.DPS.InfDPS.prest.IM"),
            ["{{PREST_RAZAO}}"] = DanfeFallback.OrDash(inf.emit?.xNome, warnings, "xNome Prestador", "infNFSe.emit.xNome"),
            ["{{PREST_ENDERECO}}"] = DanfeFallback.OrDash(Helper.BuildEndereco(inf.emit?.enderNac), warnings, "Endereço Prestador", "infNFSe.emit.enderNac"),
            ["{{PREST_MUNICIPIO}}"] = municipioEmitente,
            ["{{PREST_CEP}}"] = DanfeFallback.OrDash(Helper.FormatCep(inf.emit?.enderNac?.CEP), warnings, "CEP Prestador", "infNFSe.emit.enderNac.CEP"),
            ["{{PREST_FONE}}"] = DanfeFallback.OrDash(Helper.FormatTelefone(infDps.prest?.fone), warnings, "Fone Prestador", "infNFSe.DPS.InfDPS.prest.fone"),
            ["{{PREST_EMAIL}}"] = DanfeFallback.OrDash(infDps.prest?.email, warnings, "Email Prestador", "infNFSe.DPS.InfDPS.prest.email"),
            ["{{PREST_SIMPLES}}"] = GetDescricaoPrestadorSimples(infDps.prest?.regTrib?.opSimpNac),
            ["{{PREST_REGIME_SN}}"] = GetDescricaoRegimeSimples(infDps.prest?.regTrib?.regApTribSN),

            // Blocos suprimíveis de participantes (Tomador / Destinatário / Intermediário)
            ["{{BLOCO_TOMADOR}}"] = blocoTomador,
            ["{{BLOCO_DESTINATARIO}}"] = blocoDestinatario,
            ["{{BLOCO_INTERMEDIARIO}}"] = blocoIntermediario,

            // Serviço
            ["{{SERV_CTRIBNAC}}"] = DanfeFallback.OrDash(descricaoTributoNacional, warnings, "Descrição Tributo Nacional", "infNFSe.DPS.InfDPS.serv.cServ.cTribNac | infNFSe.xTribNac").Limit(80),
            ["{{SERV_CTRIBMUN}}"] = DanfeFallback.OrDash(descricaoTributoMunicipal, warnings, "Descrição Tributo Municipal", "infNFSe.DPS.InfDPS.serv.cServ.cTribMun | infNFSe.xTribMun").Limit(80),
            ["{{SERV_NBS}}"] = DanfeFallback.OrDash(infDps.serv?.cServ?.cNBS.ToString(), warnings, "cNBS", "infNFSe.DPS.InfDPS.serv.cServ.cNBS"),
            ["{{SERV_DESC_HTML}}"] = Helper.BuildDescricaoServicoHtml(infDps.serv?.cServ?.xDescServ),
            ["{{SERV_LOCAL}}"] = DanfeFallback.OrDash(municipioPrestador?.NomeComUf, warnings, "Município Prestação", "MunicipiosIbge.GetMunicipio(cLocPrestacao).NomeComUf"),
            ["{{SERV_PAIS}}"] = DanfeFallback.OrDash(infDps.serv?.locPrest?.cPaisPrestacao, warnings, "País da Prestação", "infNFSe.DPS.InfDPS.serv.locPrest.cPaisPrestacao"),

            // Bloco suprimível de Tributação Municipal (ISSQN)
            ["{{BLOCO_TRIB_MUNICIPAL}}"] = blocoTribMunicipal,

            // Tributação Federal (exceto CBS)
            ["{{FED_IRRF}}"] = DanfeFallback.OrCurrency(vIRRF, ptBR, warnings, "vIRRF", "infDps.valores.trib.tribFed.vRetIRRF"),
            ["{{FED_PIS}}"] = DanfeFallback.OrCurrency(vPIS, ptBR, warnings, "vPIS", "infNFSe.valores.trib.tribFed.piscofins.vPis"),
            ["{{FED_COFINS}}"] = DanfeFallback.OrCurrency(vCOFINS, ptBR, warnings, "vCOFINS", "infDps.valores.trib.tribFed.piscofins.vCofins"),
            ["{{FED_CSLL}}"] = DanfeFallback.OrCurrency(vCSLL, ptBR, warnings, "vCSLL", "infDps.valores.trib.tribFed.vRetCSLL"),
            ["{{FED_CP}}"] = DanfeFallback.OrCurrency(vCP, ptBR, warnings, "vCP", "infDps.valores.trib.tribFed.vRetCP"),
            ["{{FED_RET_PISCOFINS}}"] = GetDescricaoTipoRetencaoPisCofins(infDps.valores?.trib?.tribFed?.piscofins?.tpRetPisCofins),
            ["{{FED_TOTAL}}"] = DanfeFallback.OrCurrency(vTotTribFed, ptBR, warnings, "vTotTribFed", "infDps.valores.trib.totTrib.vTotTrib.vTotTribFed"),

            // Bloco suprimível de Tributação IBS/CBS
            ["{{BLOCO_IBSCBS}}"] = blocoIbsCbs,

            // Valores / Valor Total da NFS-e
            ["{{VALOR_SERVICO}}"] = vServico.ToString("C", ptBR),
            ["{{VALOR_LIQUIDO}}"] = DanfeFallback.OrCurrency(vLiq, ptBR, warnings, "vLiq", "infNFSe.valores.vLiq"),
            ["{{DESC_COND}}"] = vDescCond != 0 ? vDescCond.ToString("C", ptBR) : "R$",
            ["{{DESC_INCOND}}"] = vDescIncond != 0 ? vDescIncond.ToString("C", ptBR) : "R$",
            ["{{TOTAL_RETENCOES}}"] = (tpRetIssqn == 2 ? (vIssqn ?? 0M) : 0M) is var totalRet && (totalRet + vTotalRetFed + vRetPisCofins) != 0
                ? (totalRet + vTotalRetFed + vRetPisCofins).ToString("C", ptBR)
                : "-",
            ["{{TOTAL_IBSCBS}}"] = vTotalIbsCbs.HasValue ? vTotalIbsCbs.Value.ToString("C", ptBR) : "-",
            ["{{VALOR_LIQUIDO_IBSCBS}}"] = vTotNF.HasValue ? vTotNF.Value.ToString("C", ptBR) : "-",

            // Informações complementares (inclui Totais Aproximados dos Tributos — Nota 10, obrigatório)
            ["{{INF_COMPLEMENTARES}}"] = infComplementares,

            // Canhoto (opcional)
            ["{{BLOCO_CANHOTO}}"] = blocoCanhoto,
        };

        // Aplica os replaces
        foreach (var kv in map)
        {
            bool isRawHtml =
                kv.Key == "{{SERV_DESC_HTML}}" ||
                kv.Key == "{{INF_COMPLEMENTARES}}" ||
                kv.Key == "{{NFSE_CANCELADA_DIV}}" ||
                kv.Key == "{{NFSE_SUBSTITUIDA_DIV}}" ||
                kv.Key == "{{BLOCO_TOMADOR}}" ||
                kv.Key == "{{BLOCO_DESTINATARIO}}" ||
                kv.Key == "{{BLOCO_INTERMEDIARIO}}" ||
                kv.Key == "{{BLOCO_TRIB_MUNICIPAL}}" ||
                kv.Key == "{{BLOCO_IBSCBS}}" ||
                kv.Key == "{{BLOCO_CANHOTO}}";

            string value = isRawHtml ? kv.Value : Helper.HtmlEncode(kv.Value);
            template = template.Replace(kv.Key, value ?? string.Empty);
        }

        // Detecta placeholders não resolvidos (opcional, mas recomendado)
        foreach (var placeholder in map.Keys)
        {
            if (template.Contains(placeholder))
            {
                warnings.TemplatePlaceholderEmpty(placeholder);
                template = template.Replace(placeholder, string.Empty);
            }
        }

        return (template, warnings.Warnings);
    }

    // Marca d'água diagonal de CANCELADA/SUBSTITUÍDA (NT-008 2.5.1/2.5.2: mínimo 50pt, Arial, cinza K35).
    private static string BuildMarcaDagua(string texto) =>
        $@"<div style=""
              position:absolute;
              top:50%;
              left:50%;
              display:inline-block;
              -webkit-transform: translate(-50%, -50%) rotate(-30deg);
              transform: translate(-50%, -50%) rotate(-30deg);
              -webkit-transform-origin: 50% 50%;
              transform-origin: 50% 50%;
              font-family: Arial, sans-serif;
              font-size:96px;
              font-weight:800;
              color: rgba(140,140,140,0.55);
              text-transform:uppercase;
              z-index:-9999;
              pointer-events:none;
              white-space:nowrap;"">
                          {texto}
              </div>";

    // Bloco "X NÃO IDENTIFICADO NA NFS-e" / "O DESTINATÁRIO É O PRÓPRIO TOMADOR..." (NT-008 Notas 2 e 3).
    private static string BuildParticipanteNaoIdentificadoBloco(string titulo, string mensagem) =>
        $@"<div class=""section"" style=""border-bottom: none; text-align: center; font-size: 13px; font-weight: bold; padding: 4px 6px;"">
            <span>{Helper.HtmlEncode(mensagem)}</span>
          </div>";

    // Bloco de substituição quando o bloco não se aplica à operação (ex.: ISSQN não incidente — NT-008 Nota 4).
    private static string BuildBlocoNaoAplicavel(string titulo, string mensagem) =>
        $@"<div class=""section"" style=""border-bottom: none; text-align: center; font-size: 13px; font-weight: bold; padding: 4px 6px;"">
            <span>{Helper.HtmlEncode(mensagem)}</span>
          </div>";

    private static bool DocumentosIguais(string? cnpjA, string? cpfA, string? nifA, string? cnpjB, string? cpfB, string? nifB)
    {
        if (!string.IsNullOrWhiteSpace(cnpjA) && !string.IsNullOrWhiteSpace(cnpjB))
            return string.Equals(cnpjA, cnpjB, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(cpfA) && !string.IsNullOrWhiteSpace(cpfB))
            return string.Equals(cpfA, cpfB, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(nifA) && !string.IsNullOrWhiteSpace(nifB))
            return string.Equals(nifA, nifB, StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static string BuildTomadorBloco(Tomador toma, DanfeWarningCollector warnings, CultureInfo ptBR)
    {
        var cnpjCpfNif = !string.IsNullOrEmpty(toma.CPF) ? Helper.FormatCpf(toma.CPF)
            : !string.IsNullOrEmpty(toma.NIF) ? toma.NIF
            : Helper.FormatCnpj(toma.CNPJ);

        return BuildParticipanteBloco(
            "TOMADOR/ADQUIRENTE DA OPERAÇÃO",
            DanfeFallback.OrDash(cnpjCpfNif, warnings, "CNPJ/CPF/NIF Tomador", "infNFSe.DPS.InfDPS.toma"),
            DanfeFallback.OrDash(toma.IM),
            DanfeFallback.OrDash(Helper.FormatTelefone(toma.fone)),
            DanfeFallback.OrDash(toma.xNome, warnings, "xNome Tomador", "infNFSe.DPS.InfDPS.toma.xNome"),
            DanfeFallback.OrDash(Helper.BuildEndereco(toma.end), warnings, "Endereço Tomador", "infNFSe.DPS.InfDPS.toma.end"),
            ResolveMunicipioComUf(toma.end, warnings, "infNFSe.DPS.InfDPS.toma.end"),
            ResolveCepOuCEndPost(toma.end),
            DanfeFallback.OrDash(toma.email));
    }

    private static string BuildDestinatarioBloco(dest destinatario, DanfeWarningCollector warnings)
    {
        var cnpjCpfNif = !string.IsNullOrEmpty(destinatario.CPF) ? Helper.FormatCpf(destinatario.CPF)
            : !string.IsNullOrEmpty(destinatario.NIF) ? destinatario.NIF
            : Helper.FormatCnpj(destinatario.CNPJ);

        return BuildParticipanteBloco(
            "DESTINATÁRIO DA OPERAÇÃO",
            DanfeFallback.OrDash(cnpjCpfNif, warnings, "CNPJ/CPF/NIF Destinatário", "infNFSe.DPS.InfDPS.IBSCBS.dest"),
            null,
            DanfeFallback.OrDash(Helper.FormatTelefone(destinatario.fone)),
            DanfeFallback.OrDash(destinatario.xNome, warnings, "xNome Destinatário", "infNFSe.DPS.InfDPS.IBSCBS.dest.xNome"),
            DanfeFallback.OrDash(Helper.BuildEndereco(destinatario.end), warnings, "Endereço Destinatário", "infNFSe.DPS.InfDPS.IBSCBS.dest.end"),
            ResolveMunicipioComUf(destinatario.end, warnings, "infNFSe.DPS.InfDPS.IBSCBS.dest.end"),
            ResolveCepOuCEndPost(destinatario.end),
            DanfeFallback.OrDash(destinatario.email));
    }

    private static string BuildIntermediarioBloco(Intermediario intermediario, DanfeWarningCollector warnings)
    {
        var cnpjCpfNif = !string.IsNullOrEmpty(intermediario.CPF) ? Helper.FormatCpf(intermediario.CPF)
            : !string.IsNullOrEmpty(intermediario.NIF) ? intermediario.NIF
            : Helper.FormatCnpj(intermediario.CNPJ);

        return BuildParticipanteBloco(
            "INTERMEDIÁRIO DA OPERAÇÃO",
            DanfeFallback.OrDash(cnpjCpfNif, warnings, "CNPJ/CPF/NIF Intermediário", "infNFSe.DPS.InfDPS.interm"),
            DanfeFallback.OrDash(intermediario.IM),
            DanfeFallback.OrDash(Helper.FormatTelefone(intermediario.fone)),
            DanfeFallback.OrDash(intermediario.xNome, warnings, "xNome Intermediário", "infNFSe.DPS.InfDPS.interm.xNome"),
            DanfeFallback.OrDash(Helper.BuildEndereco(intermediario.end), warnings, "Endereço Intermediário", "infNFSe.DPS.InfDPS.interm.end"),
            ResolveMunicipioComUf(intermediario.end, warnings, "infNFSe.DPS.InfDPS.interm.end"),
            ResolveCepOuCEndPost(intermediario.end),
            DanfeFallback.OrDash(intermediario.email));
    }

    private static string BuildParticipanteBloco(
        string titulo, string cnpjCpfNif, string? im, string fone, string nome, string endereco, string municipioUf, string cep, string email)
    {
        var imLinha = im != null
            ? $@"<td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Indicador Municipal (Inscrição)</span><br />{im}</td>"
            : @"<td style=""vertical-align: top; width: 25%""></td>";

        return $@"
          <div class=""section"">
            <div class=""section-content"" style=""padding: 1px 6px 6px 6px"">
              <table style=""width: 100%; border-collapse: collapse"">
                <tr>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-size: 12px; font-weight: bold"">{titulo}</span></td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">CNPJ / CPF / NIF</span><br />{cnpjCpfNif}</td>
                  {imLinha}
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Telefone</span><br />{fone}</td>
                </tr>
              </table>
              <table style=""width: 100%; border-collapse: collapse; margin-top: 5px"">
                <tr>
                  <td style=""vertical-align: top; width: 50%""><span class=""label"" style=""font-weight: bold"">Nome / Nome Empresarial</span><br />{nome}</td>
                  <td style=""vertical-align: top; width: 50%""><span class=""label"" style=""font-weight: bold"">E-mail</span><br />{email}</td>
                </tr>
              </table>
              <table style=""width: 100%; border-collapse: collapse; margin-top: 5px"">
                <tr>
                  <td style=""vertical-align: top; width: 50%""><span class=""label"" style=""font-weight: bold"">*Endereço</span><br />{endereco}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Município / Sigla UF</span><br />{municipioUf}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Código IBGE / CEP</span><br />{cep}</td>
                </tr>
              </table>
            </div>
          </div>
          <div style=""border-top: 1px solid #000; margin: 0 5px""></div>";
    }

    private static string ResolveMunicipioComUf(EndSimples? end, DanfeWarningCollector warnings, string path)
    {
        if (end?.endNac != null)
            return ResolveMunicipioNomeComUf(end.endNac.cMun, warnings, path + ".endNac.cMun");
        if (end?.endExt != null)
            return DanfeFallback.OrDash($"{end.endExt.xCidade} - {end.endExt.xEstProvReg}", warnings, "Cidade/Estado exterior", path + ".endExt");
        warnings.FieldMissing("endNac/endExt", path, "-");
        return "-";
    }

    private static string ResolveCepOuCEndPost(EndSimples? end)
    {
        if (end?.endNac != null)
            return Helper.FormatCep(end.endNac.CEP);
        if (end?.endExt != null)
            return DanfeFallback.OrDash(end.endExt.cEndPost);
        return "-";
    }

    private static string BuildTribMunicipalBloco(
        InfDPS infDps, ValoresNfse? valores, MunicipiosIbge.Municipio? municpioISSQN,
        int? tpRetIssqn, decimal? vAliqAplic, decimal? vIssqn, decimal vServico, CultureInfo ptBR, DanfeWarningCollector warnings)
    {
        var tribMun = infDps.valores?.trib?.tribMun;

        var issTributacao = HtmlHelperEncode(GetDescricaoTributacao(tribMun?.tribISSQN));
        var issPais = HtmlHelperEncode(DanfeFallback.OrDash(infDps.toma?.end?.endExt?.cPais, warnings, "País Resultado da Prestação do Serviço", "infNFSe.DPS.InfDPS.toma.end.endExt.cPais"));
        var issMunInc = HtmlHelperEncode(DanfeFallback.OrDash(municpioISSQN?.NomeComUf, warnings, "Município Incidência", "MunicipiosIbge.GetMunicipio(cLocIncid).NomeComUf"));
        var issRegime = HtmlHelperEncode(GetDescricaoRegimeEspecial(infDps.prest?.regTrib?.regEspTrib));
        var issOperacao = HtmlHelperEncode(GetDescricaoTipoImunidade(tribMun?.tpImunidade));
        var issSuspensao = HtmlHelperEncode(GetDescricaoTipoSuspensaoISSQN(tribMun?.exigSusp?.tpSusp));
        var issProcesso = HtmlHelperEncode(DanfeFallback.OrDash(tribMun?.exigSusp?.nProcesso, warnings, "Número Processo Suspensão", "infNFSe.DPS.InfDPS.valores.trib.tribMun.exigSusp.nProcesso"));
        var issBeneficio = HtmlHelperEncode(DanfeFallback.OrDash(tribMun?.BM?.nBM.ToString(), warnings, "Benefício Municipal", "infNFSe.DPS.InfDPS.valores.trib.tribMun.BM.nBM"));
        var issDescIncond = HtmlHelperEncode(DanfeFallback.OrCurrency(infDps.valores?.vDescCondIncond?.vDescIncond, ptBR, warnings, "vDescIncond", "infNFSe.valores.vDescCondIncond.vDescIncond"));
        var issDeducoes = HtmlHelperEncode(DanfeFallback.OrCurrency(infDps.valores?.vDedRed?.vDR, ptBR, warnings, "vDR", "infNFSe.valores.vDedRed.vDR"));
        var issCalculo = HtmlHelperEncode(DanfeFallback.OrCurrency(tribMun?.BM?.vRedBCBM, ptBR, warnings, "vRedBCBM", "infNFSe.valores.trib.tribMun.BM.vRedBCBM"));
        var issBc = HtmlHelperEncode(vServico.ToString("C", ptBR));
        var issAliq = HtmlHelperEncode(DanfeFallback.OrPercent(vAliqAplic, ptBR, warnings, "pAliqAplic", "infNFSe.valores.pAliqAplic"));
        var issRetencao = HtmlHelperEncode(GetDescricaoRetencao(tpRetIssqn));
        var issApurado = HtmlHelperEncode(DanfeFallback.OrCurrency(vIssqn, ptBR, warnings, "vISSQN", "infNFSe.valores.vISSQN"));

        return $@"
          <div class=""section"">
            <div class=""section-title"" style=""font-size: 12px; font-weight: bold; padding-left: 6px"">TRIBUTAÇÃO MUNICIPAL (ISSQN)</div>
            <div class=""section-content"" style=""padding: 1px 6px 6px 6px"">
              <table style=""width: 100%; border-collapse: collapse"">
                <tr>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Tipo de Tributação do ISSQN</span><br />{issTributacao}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">País Resultado da Prestação do Serviço</span><br />{issPais}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Município de Incidência do ISSQN</span><br />{issMunInc}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Regime Especial de Tributação</span><br />{issRegime}</td>
                </tr>
              </table>
              <table style=""width: 100%; border-collapse: collapse; margin-top: 5px"">
                <tr>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Tipo de Imunidade</span><br />{issOperacao}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Suspensão da Exigibilidade do ISSQN</span><br />{issSuspensao}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Número Processo Suspensão</span><br />{issProcesso}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Benefício Municipal</span><br />{issBeneficio}</td>
                </tr>
              </table>
              <table style=""width: 100%; border-collapse: collapse; margin-top: 5px"">
                <tr>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Desconto Incondicionado</span><br />{issDescIncond}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Total Deduções/Reduções</span><br />{issDeducoes}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Cálculo do BM</span><br />{issCalculo}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">BC ISSQN</span><br />{issBc}</td>
                </tr>
              </table>
              <table style=""width: 100%; border-collapse: collapse; margin-top: 5px"">
                <tr>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Alíquota Aplicada</span><br />{issAliq}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Retenção do ISSQN</span><br />{issRetencao}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">ISSQN Apurado</span><br />{issApurado}</td>
                  <td style=""vertical-align: top; width: 25%""></td>
                </tr>
              </table>
            </div>
          </div>
          <div style=""border-top: 1px solid #000; margin: 0 5px""></div>";
    }

    private static string BuildIbsCbsBloco(InfDPS infDps, InfNFSe inf, CultureInfo ptBR, DanfeWarningCollector warnings)
    {
        var gIBSCBS = infDps.IBSCBS?.valores?.trib?.gIBSCBS;
        var apurado = inf.IBSCBS;
        var valoresApurados = apurado?.valores;

        var cstCClassTrib = HtmlHelperEncode(DanfeFallback.OrDash(
            !string.IsNullOrWhiteSpace(gIBSCBS?.CST) || !string.IsNullOrWhiteSpace(gIBSCBS?.cClassTrib)
                ? $"{gIBSCBS?.CST} / {gIBSCBS?.cClassTrib}"
                : null,
            warnings, "CST/cClassTrib", "infNFSe.DPS.InfDPS.valores.trib.gIBSCBS"));

        var indicadorOperacao = HtmlHelperEncode(DanfeFallback.OrDash(apurado?.cIndOp, warnings, "Indicador de Operação", "infNFSe.IBSCBS.cIndOp"));
        var municipioIncidencia = HtmlHelperEncode(DanfeFallback.OrDash(
            !string.IsNullOrWhiteSpace(apurado?.xLocalidadeIncid) ? apurado!.xLocalidadeIncid : null,
            warnings, "Município Incidência IBS/CBS", "infNFSe.IBSCBS.cLocalidadeIncid/xLocalidadeIncid"));

        var exclusoesReducoes = HtmlHelperEncode(DanfeFallback.OrCurrency(valoresApurados?.vCalcReeRepRes, ptBR, warnings, "vCalcReeRepRes", "infNFSe.IBSCBS.valores.vCalcReeRepRes"));
        var baseCalculo = HtmlHelperEncode(DanfeFallback.OrCurrency(valoresApurados?.vBC, ptBR, warnings, "vBC", "infNFSe.IBSCBS.valores.vBC"));

        var redAliquotas = HtmlHelperEncode(FormatTresPercentuais(
            valoresApurados?.uf?.pRedAliqUF, valoresApurados?.mun?.pRedAliqMun, valoresApurados?.fed?.pRedAliqCBS, ptBR));
        var aliqIbsUfMun = HtmlHelperEncode(FormatDoisPercentuais(valoresApurados?.uf?.pIBSUF, valoresApurados?.mun?.pIBSMun, ptBR));
        var aliqEfetivaMun = HtmlHelperEncode(DanfeFallback.OrPercent(valoresApurados?.mun?.pAliqEfetMun, ptBR, warnings, "pAliqEfetMun", "infNFSe.IBSCBS.valores.mun.pAliqEfetMun"));
        var valorApuradoMun = HtmlHelperEncode(DanfeFallback.OrCurrency(apurado?.totCIBS?.gIBS?.gIBSMunTot?.vIBSMun, ptBR, warnings, "vIBSMun", "infNFSe.IBSCBS.totCIBS.gIBS.gIBSMunTot.vIBSMun"));
        var aliqEfetivaUf = HtmlHelperEncode(DanfeFallback.OrPercent(valoresApurados?.uf?.pAliqEfetUF, ptBR, warnings, "pAliqEfetUF", "infNFSe.IBSCBS.valores.uf.pAliqEfetUF"));
        var valorApuradoUf = HtmlHelperEncode(DanfeFallback.OrCurrency(apurado?.totCIBS?.gIBS?.gIBSUFTot?.vIBSUF, ptBR, warnings, "vIBSUF", "infNFSe.IBSCBS.totCIBS.gIBS.gIBSUFTot.vIBSUF"));
        var valorTotalIbs = HtmlHelperEncode(DanfeFallback.OrCurrency(apurado?.totCIBS?.gIBS?.vIBSTot, ptBR, warnings, "vIBSTot", "infNFSe.IBSCBS.totCIBS.gIBS.vIBSTot"));
        var aliqCbs = HtmlHelperEncode(DanfeFallback.OrPercent(valoresApurados?.fed?.pCBS, ptBR, warnings, "pCBS", "infNFSe.IBSCBS.valores.fed.pCBS"));
        var aliqEfetivaCbs = HtmlHelperEncode(DanfeFallback.OrPercent(valoresApurados?.fed?.pAliqEfetCBS, ptBR, warnings, "pAliqEfetCBS", "infNFSe.IBSCBS.valores.fed.pAliqEfetCBS"));
        var valorTotalCbs = HtmlHelperEncode(DanfeFallback.OrCurrency(apurado?.totCIBS?.gCBS?.vCBS, ptBR, warnings, "vCBS", "infNFSe.IBSCBS.totCIBS.gCBS.vCBS"));

        return $@"
          <div class=""section"">
            <div class=""section-title"" style=""font-size: 12px; font-weight: bold; padding-left: 6px"">TRIBUTAÇÃO IBS / CBS</div>
            <div class=""section-content"" style=""padding: 1px 6px 6px 6px"">
              <table style=""width: 100%; border-collapse: collapse"">
                <tr>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">CST / cClassTrib</span><br />{cstCClassTrib}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Indicador de Operação</span><br />{indicadorOperacao}</td>
                  <td style=""vertical-align: top; width: 50%""><span class=""label"" style=""font-weight: bold"">Código IBGE Incidência / Município Incidência / Sigla UF</span><br />{municipioIncidencia}</td>
                </tr>
              </table>
              <table style=""width: 100%; border-collapse: collapse; margin-top: 5px"">
                <tr>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Exclusões e Reduções da Base de Cálculo</span><br />{exclusoesReducoes}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Base de Cálculo Após Exclusões e Reduções</span><br />{baseCalculo}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Red. Alíquota IBS UF / IBS Mun / CBS</span><br />{redAliquotas}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Alíquota IBS UF / IBS Mun</span><br />{aliqIbsUfMun}</td>
                </tr>
              </table>
              <table style=""width: 100%; border-collapse: collapse; margin-top: 5px"">
                <tr>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Alíq. Efetiva Municipal - IBS</span><br />{aliqEfetivaMun}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Valor Apurado Municipal - IBS</span><br />{valorApuradoMun}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Alíq. Efetiva Estadual - IBS</span><br />{aliqEfetivaUf}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Valor Apurado Estadual - IBS</span><br />{valorApuradoUf}</td>
                </tr>
              </table>
              <table style=""width: 100%; border-collapse: collapse; margin-top: 5px"">
                <tr>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Valor Total Apurado - IBS</span><br />{valorTotalIbs}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Alíquota - CBS</span><br />{aliqCbs}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Alíquota Efetiva - CBS</span><br />{aliqEfetivaCbs}</td>
                  <td style=""vertical-align: top; width: 25%""><span class=""label"" style=""font-weight: bold"">Valor Total Apurado - CBS</span><br />{valorTotalCbs}</td>
                </tr>
              </table>
            </div>
          </div>
          <div style=""border-top: 1px solid #000; margin: 0 5px""></div>";
    }

    private static string FormatDoisPercentuais(decimal? a, decimal? b, CultureInfo ptBR)
    {
        if (!a.HasValue && !b.HasValue) return "-";
        return $"{a?.ToString("N2", ptBR) ?? "-"}% / {b?.ToString("N2", ptBR) ?? "-"}%";
    }

    private static string FormatTresPercentuais(decimal? a, decimal? b, decimal? c, CultureInfo ptBR)
    {
        if (!a.HasValue && !b.HasValue && !c.HasValue) return "-";
        return $"{a?.ToString("N2", ptBR) ?? "-"}% / {b?.ToString("N2", ptBR) ?? "-"}% / {c?.ToString("N2", ptBR) ?? "-"}%";
    }

    private static string HtmlHelperEncode(string value) => Helper.HtmlEncode(value);

    // Canhoto opcional (NT-008 Nota 11) — bloco de ciência do recebimento.
    private static string BuildCanhotoBloco(string numeroNfse, string chaveAcesso) => $@"
      <div style=""border-top: 1px solid #000; margin: 0 5px""></div>
      <div class=""section"" style=""border-bottom: none;"">
        <div class=""section-title"" style=""font-size: 12px; font-weight: bold; padding-left: 6px"">CANHOTO</div>
        <div class=""section-content"" style=""padding: 1px 6px 6px 6px"">
          <table style=""width: 100%; border-collapse: collapse"">
            <tr>
              <td style=""vertical-align: top; width: 33%""><span class=""label"" style=""font-weight: bold"">Data Cientificação</span><br />&nbsp;</td>
              <td style=""vertical-align: top; width: 34%""><span class=""label"" style=""font-weight: bold"">Identificação e Assinatura</span><br />&nbsp;</td>
              <td style=""vertical-align: top; width: 33%"">
                <span class=""label"" style=""font-weight: bold"">Nº NFS-e / Chave NFS-e</span><br />
                {Helper.HtmlEncode(numeroNfse)} / {Helper.HtmlEncode(chaveAcesso)}
              </td>
            </tr>
          </table>
        </div>
      </div>";

    // Informações complementares — ordem e rótulos conforme NT-008 §2.4.5 (Informações Complementares)
    // e Nota 10 (totais aproximados dos tributos, obrigatório em toda NFS-e).
    private static string BuildInfComplementares(InfDPS infDps, InfNFSe inf, CultureInfo ptBR)
    {
        var segmentos = new List<string>();

        if (!string.IsNullOrWhiteSpace(infDps.serv?.infoCompl?.xInfComp))
            segmentos.Add($"<b>Inf. Cont.:</b> {Helper.HtmlEncode(infDps.serv!.infoCompl!.xInfComp!)}");

        if (infDps.subst != null)
            segmentos.Add($"<b>NFS-e Subst.:</b> {Helper.HtmlEncode(infDps.subst.chSubstda ?? string.Empty)}");

        if (!string.IsNullOrWhiteSpace(infDps.serv?.infoCompl?.docRef))
            segmentos.Add($"<b>Doc. Ref.:</b> {Helper.HtmlEncode(infDps.serv!.infoCompl!.docRef!)}");

        if (infDps.serv?.obra != null)
            segmentos.Add($"<b>Cod. Obra:</b> {Helper.HtmlEncode(infDps.serv.obra.cObra ?? "-")}");

        if (infDps.IBSCBS?.imovel != null)
            segmentos.Add($"<b>Insc. Imob.:</b> {Helper.HtmlEncode(infDps.IBSCBS.imovel.inscImobFisc ?? "-")}");

        if (infDps.serv?.atvEvento != null)
            segmentos.Add($"<b>Cod. Evt.:</b> {Helper.HtmlEncode(infDps.serv.atvEvento.idAtvEvt ?? "-")}");

        if (!string.IsNullOrWhiteSpace(infDps.serv?.infoCompl?.idDocTec))
            segmentos.Add($"<b>Doc. Tec.:</b> {Helper.HtmlEncode(infDps.serv!.infoCompl!.idDocTec!)}");

        if (!string.IsNullOrWhiteSpace(infDps.serv?.infoCompl?.xPed))
            segmentos.Add($"<b>Núm. Ped.:</b> {Helper.HtmlEncode(infDps.serv!.infoCompl!.xPed!)}");

        if (!string.IsNullOrWhiteSpace(infDps.serv?.infoCompl?.xItemPed))
            segmentos.Add($"<b>Item Ped.:</b> {Helper.HtmlEncode(infDps.serv!.infoCompl!.xItemPed!)}");

        if (!string.IsNullOrWhiteSpace(inf.valores?.xOutInf))
            segmentos.Add($"<b>Inf. A. T. Mun.:</b> {Helper.HtmlEncode(inf.valores!.xOutInf!)}");

        // Nota 10: totais aproximados dos tributos — obrigatório, monetário OU percentual (nunca ambos, nunca recalculado).
        segmentos.Add(BuildTotaisAproximadosTributos(infDps, ptBR));

        return string.Join(" | ", segmentos);
    }

    private static string BuildTotaisAproximadosTributos(InfDPS infDps, CultureInfo ptBR)
    {
        var totTrib = infDps.valores?.trib?.totTrib;

        string fed, est, mun;
        if (totTrib?.vTotTrib != null)
        {
            fed = totTrib.vTotTrib.vTotTribFed.ToString("C", ptBR);
            est = totTrib.vTotTrib.vTotTribEst.ToString("C", ptBR);
            mun = totTrib.vTotTrib.vTotTribMun.ToString("C", ptBR);
        }
        else if (totTrib?.pTotTrib != null)
        {
            fed = $"{totTrib.pTotTrib.pTotTribFed.ToString("N2", ptBR)}%";
            est = $"{totTrib.pTotTrib.pTotTribEst.ToString("N2", ptBR)}%";
            mun = $"{totTrib.pTotTrib.pTotTribMun.ToString("N2", ptBR)}%";
        }
        else
        {
            fed = est = mun = "-";
        }

        return $"<b>Totais Aproximados dos Tributos cfe. Lei nº 12.741/2012:</b> Federais: {fed}; Estaduais: {est}; Municipais: {mun}";
    }

    // Helper local: resolve município do tomador sem explodir e com warning
    private static string ResolveMunicipioNomeComUf(int? cMun, DanfeWarningCollector warnings, string path)
    {
        if (!cMun.HasValue)
        {
            warnings.FieldMissing("cMun", path, "-");
            return "-";
        }

        var mun = MunicipiosIbge.GetMunicipio(cMun.Value);
        if (mun == null)
        {
            warnings.MunicipioNotFound(path);
            return "-";
        }

        return DanfeFallback.OrDash(mun.NomeComUf, warnings, "NomeComUf", path);
    }

    private static string GetDescricaoRetencao(int? tpRetISSQN)
    {
        switch (tpRetISSQN)
        {
            case 1:
                return "Não Retido";
            case 2:
                return "Retido pelo Tomador";
            case 3:
                return "Retido pelo Intermediario";
            default:
                return "-";
        }
    }

    private static string GetDescricaoTipoRetencaoPisCofins(int? tpRetPisCofins)
    {
        /*
           Tipo de retenção ao do PIS/COFINS:

            1 - PIS/COFINS Retido;
            2 - PIS/COFINS Não Retido;
            3 - PIS Retido/COFINS Não Retido;
            4 - PIS Não Retido/COFINS Retido;
         */
        switch (tpRetPisCofins)
        {
            case 1:
                return "PIS/COFINS Retido";
            case 2:
                return "PIS/COFINS Não Retido";
            case 3:
                return "PIS Retido/COFINS Não Retido";
            case 4:
                return "PIS Não Retido/COFINS Retido";
            default:
                return "-";
        }
    }

    private static string GetDescricaoTributacao(int? tribISSQN)
    {
        switch (tribISSQN)
        {
            case 1:
                return "Operação Tributável";
            case 2:
                return "Imunidade";
            case 3:
                return "Exportação de serviço";
            case 4:
                return "Não Incidência";
            default:
                return "";
        }
    }

    private static string GetDescricaoEmitente(int tpEmis)
    {
        switch (tpEmis)
        {
            case 1:
                return "Prestador do Serviço";
            case 2:
                return "Tomador do Serviço";
            case 3:
                return "Intermediário";
            default:
                return "-";
        }
    }

    private static string GetDescricaoPrestadorSimples(int? opSimpNac)
    {
        switch (opSimpNac)
        {
            case 1:
                return "Não Optante";
            case 2:
                return "Optante - Microempreendedor Individual(MEI)";
            case 3:
                return "Optante - Microempresa ou Empresa de Pequeno Porte (ME/EPP)";
            default:
                return "-";
        }
    }
    private static string GetDescricaoRegimeSimples(int? regApTribSN)
    {
        /*
          Opção para que o contribuinte optante pelo Simples Nacional ME/EPP (opSimpNac = 3) possa indicar, ao emitir o documento fiscal, em qual regime de apuração os tributos federais e municipal estão inseridos, caso tenha ultrapassado algum sublimite ou limite definido para o Simples Nacional.
            1 – Regime de apuração dos tributos federais e municipal pelo SN;
            2 – Regime de apuração dos tributos federais pelo SN e ISSQN  por fora do SN conforme respectiva legislação municipal do tributo;
            3 – Regime de apuração dos tributos federais e municipal por fora do SN conforme respectivas legilações federal e municipal de cada tributo;
         */
        switch (regApTribSN)
        {
            case 1:
                return "Regime de apuração dos tributos federais e municipal pelo Simples Nacional";
            case 2:
                return "Regime de apuração dos tributos federais pelo SN e ISSQN  por fora do SN conforme respectiva legislação municipal do tributo";
            case 3:
                return "Regime de apuração dos tributos federais e municipal por fora do SN conforme respectivas legilações federal e municipal de cada tributo";
            default:
                return "-";
        }
    }
    private static string GetDescricaoRegimeEspecial(int? regEspTrib)
    {
        /*
           Tipos de Regimes Especiais de Tributação:
            0 - Nenhum;
            1 - Ato Cooperado (Cooperativa);
            2 - Estimativa;
            3 - Microempresa Municipal;
            4 - Notário ou Registrador;
            5 - Profissional Autônomo;
            6 - Sociedade de Profissionais;
         */
        switch (regEspTrib)
        {
            case 0:
                return "Nenhum";
            case 1:
                return "Ato Cooperado (Cooperativa)";
            case 2:
                return "Estimativa";
            case 3:
                return "Microempresa Municipal";
            case 4:
                return "Notário ou Registrador";
            case 5:
                return "Profissional Autônomo";
            case 6:
                return "Sociedade de Profissionais";
            default:
                return "-";
        }
    }

    private static string GetDescricaoTipoImunidade(int? tpImunidade)
    {
        /*
           Tipos de Imunidades municipais:
            0 - Imunidade (tipo não informado na nota de origem);
            1 - Patrimônio, renda ou serviços, uns dos outros (CF88, Art 150, VI, a);
            2 - Entidades religiosas e templos de qualquer culto, inclusive suas organizações assistenciais e beneficentes (CF88, Art 150, VI, b);
            3 - Patrimônio, renda ou serviços dos partidos políticos, inclusive suas fundações, das entidades sindicais dos trabalhadores, das instituições de educação e de assistência social, sem fins lucrativos, atendidos os requisitos da lei (CF88, Art 150, VI, c);
            4 - Livros, jornais, periódicos e o papel destinado a sua impressão (CF88, Art 150, VI, d);
            5 - Fonogramas e videofonogramas musicais produzidos no Brasil contendo obras musicais ou literomusicais de autores brasileiros e/ou obras em geral interpretadas por artistas brasileiros bem como os suportes materiais ou arquivos digitais que os contenham, salvo na etapa de replicação industrial de mídias ópticas de leitura a laser.   (CF88, Art 150, VI, e);
         */
        switch (tpImunidade)
        {
            case 0:
                return "Nenhum";
            case 1:
                return "Patrimônio, renda ou serviços, uns dos outros";
            case 2:
                return "Entidades religiosas e templos de qualquer culto";
            case 3:
                return "Patrimônio, renda ou serviços dos partidos políticos";
            case 4:
                return "Livros, jornais, periódicos e o papel destinado a sua impressão";
            case 5:
                return "Fonogramas e videofonogramas musicais produzidos no Brasil";
            default:
                return "-";
        }
    }

    private static string GetDescricaoTipoSuspensaoISSQN(int? tpSusp)
    {
        /*
           Opção para Exigibilidade Suspensa:

            1 - Exigibilidade do ISSQN Suspensa por Decisão Judicial;
            2 - Exigibilidade do ISSQN Suspensa por Processo Administrativo;
         */
        switch (tpSusp)
        {
            case 0:
                return "Não";
            case 1:
                return "Suspensa por Decisão Judicial";
            case 2:
                return "Suspensa por Processo Administrativo";
            default:
                return "-";
        }
    }

    private static string GetDescricaoSituacao(bool isCancelled, bool isReplaced)
    {
        if (isCancelled) return "Cancelada";
        if (isReplaced) return "Substituída";
        return "Regular";
    }

    private static string GetDescricaoFinalidade(long? finNFSe)
    {
        /*
           Finalidade da emissão da NFS-e (leiaute nacional):
            1 - NFS-e normal;
            2 - NFS-e complementar;
            3 - NFS-e de ajuste;
            4 - NFS-e de Decisão Judicial ou Administrativa.
         */
        switch (finNFSe)
        {
            case 1:
                return "NFS-e Normal";
            case 2:
                return "NFS-e Complementar";
            case 3:
                return "NFS-e de Ajuste";
            case 4:
                return "NFS-e de Decisão Judicial ou Administrativa";
            default:
                return "-";
        }
    }

    // NOTA: descrição best-effort com base em documentação pública do Sistema Nacional NFS-e;
    // confirmar contra o XSD/manual oficial da DPS antes de publicar em produção.
    private static string GetDescricaoAmbienteGerador(long ambGer)
    {
        switch (ambGer)
        {
            case 1:
                return "Sistema Próprio do Município";
            case 2:
                return "Sistema Nacional (Sefin Nacional)";
            case 3:
                return "Ambiente Nacional (ADN)";
            default:
                return "-";
        }
    }
}
