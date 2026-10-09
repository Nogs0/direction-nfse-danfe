using System.Collections.Generic;
using System.Linq;

namespace Direction.NFSe.Danfe;

/// <summary>
/// Classificação de <c>tribFed/piscofins/tpRetPisCofins</c> conforme a tabela vigente do leiaute
/// nacional (NT 008 v1.02). Fonte única: consumidores (ex.: conversor da NFS-e Nacional) devem
/// delegar a esta classe em vez de manter tabela própria.
/// </summary>
public static class TpRetPisCofins
{
    private sealed record Regra(bool Pis, bool Cofins, bool Csll);

    // Classificação usada no cálculo (conversor da NFS-e Nacional). 0 e 2 ficam fora: não têm composição
    // de retenção e são tratados explicitamente pelos consumidores.
    private static readonly Dictionary<int, Regra> Regras = new Dictionary<int, Regra>
    {
        [1] = new(true, true, false),
        [3] = new(true, true, true),
        [4] = new(true, true, false),
        [5] = new(true, false, false),
        [6] = new(false, true, false),
        [7] = new(false, true, true),
        [8] = new(false, false, true),
        [9] = new(true, false, true),
    };

    // Texto das dez opções do leiaute vigente (Anexo I da NT 008 v1.02), como no DANFS-e oficial.
    private static readonly Dictionary<int, string> Descricoes = new Dictionary<int, string>
    {
        [0] = "PIS/COFINS/CSLL Não Retidos",
        [1] = "PIS/COFINS Retido",
        [2] = "PIS/COFINS Não Retido",
        [3] = "PIS/COFINS/CSLL Retidos",
        [4] = "PIS/COFINS Retidos, CSLL Não Retido",
        [5] = "PIS Retido, COFINS/CSLL Não Retido",
        [6] = "COFINS Retido, PIS/CSLL Não Retido",
        [7] = "PIS Não Retido, COFINS/CSLL Retidos",
        [8] = "PIS/COFINS Não Retidos, CSLL Retido",
        [9] = "COFINS Não Retido, PIS/CSLL Retidos",
    };

    /// <summary>Códigos suportados pela tabela vigente, em ordem crescente.</summary>
    public static IReadOnlyList<int> CodigosSuportados { get; } = Regras.Keys.OrderBy(k => k).ToArray();

    public static bool EhSuportado(int? codigo) => codigo.HasValue && Regras.ContainsKey(codigo.Value);

    public static bool RetemPis(int? codigo) => Obter(codigo)?.Pis ?? false;

    public static bool RetemCofins(int? codigo) => Obter(codigo)?.Cofins ?? false;

    public static bool RetemCsll(int? codigo) => Obter(codigo)?.Csll ?? false;

    /// <summary>
    /// Descrição da opção no leiaute (ex.: "PIS/COFINS/CSLL Retidos" para o código 3), ou <c>null</c>
    /// quando o código não existe no leiaute. Cobre os dez códigos (0 a 9), inclusive 0 e 2, que não
    /// têm classificação de retenção.
    /// </summary>
    public static string? Descricao(int? codigo) =>
        codigo.HasValue && Descricoes.TryGetValue(codigo.Value, out var descricao) ? descricao : null;

    private static Regra? Obter(int? codigo) =>
        codigo.HasValue && Regras.TryGetValue(codigo.Value, out var regra) ? regra : null;
}
