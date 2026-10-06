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
    private sealed record Regra(bool Pis, bool Cofins, bool Csll, string Descricao);

    private static readonly Dictionary<int, Regra> Regras = new Dictionary<int, Regra>
    {
        [1] = new(true, true, false, "PIS/COFINS Retidos"),
        [3] = new(true, true, true, "PIS/COFINS/CSLL Retidos"),
        [4] = new(true, true, false, "PIS/COFINS Retidos, CSLL Não Retido"),
        [5] = new(true, false, false, "PIS Retido, COFINS/CSLL Não Retidos"),
        [6] = new(false, true, false, "COFINS Retido, PIS/CSLL Não Retidos"),
        [7] = new(false, true, true, "COFINS/CSLL Retidos, PIS Não Retido"),
        [8] = new(false, false, true, "CSLL Retido, PIS/COFINS Não Retidos"),
        [9] = new(true, false, true, "PIS/CSLL Retidos, COFINS Não Retido"),
    };

    /// <summary>Códigos suportados pela tabela vigente, em ordem crescente.</summary>
    public static IReadOnlyList<int> CodigosSuportados { get; } = Regras.Keys.OrderBy(k => k).ToArray();

    public static bool EhSuportado(int? codigo) => codigo.HasValue && Regras.ContainsKey(codigo.Value);

    public static bool RetemPis(int? codigo) => Obter(codigo)?.Pis ?? false;

    public static bool RetemCofins(int? codigo) => Obter(codigo)?.Cofins ?? false;

    public static bool RetemCsll(int? codigo) => Obter(codigo)?.Csll ?? false;

    /// <summary>
    /// Descrição da opção no leiaute (ex.: "PIS/COFINS/CSLL Retidos" para o código 3), ou <c>null</c>
    /// quando o código não é suportado.
    /// </summary>
    public static string? Descricao(int? codigo) => Obter(codigo)?.Descricao;

    private static Regra? Obter(int? codigo) =>
        codigo.HasValue && Regras.TryGetValue(codigo.Value, out var regra) ? regra : null;
}
