using Direction.NFSe.Danfe;
using Xunit;

namespace Danfe.Tests;

public class TpRetPisCofinsTests
{
    [Theory]
    [InlineData(1, true, true, false)]
    [InlineData(3, true, true, true)]
    [InlineData(4, true, true, false)]
    [InlineData(5, true, false, false)]
    [InlineData(6, false, true, false)]
    [InlineData(7, false, true, true)]
    [InlineData(8, false, false, true)]
    [InlineData(9, true, false, true)]
    public void Classificacao_CodigoSuportado_RetornaTributosRetidos(int codigo, bool pis, bool cofins, bool csll)
    {
        Assert.True(TpRetPisCofins.EhSuportado(codigo));
        Assert.Equal(pis, TpRetPisCofins.RetemPis(codigo));
        Assert.Equal(cofins, TpRetPisCofins.RetemCofins(codigo));
        Assert.Equal(csll, TpRetPisCofins.RetemCsll(codigo));
        Assert.False(string.IsNullOrWhiteSpace(TpRetPisCofins.Descricao(codigo)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(10)]
    public void Classificacao_CodigoNaoSuportado_NaoRetemNada(int? codigo)
    {
        Assert.False(TpRetPisCofins.EhSuportado(codigo));
        Assert.False(TpRetPisCofins.RetemPis(codigo));
        Assert.False(TpRetPisCofins.RetemCofins(codigo));
        Assert.False(TpRetPisCofins.RetemCsll(codigo));
    }

    [Theory]
    [InlineData(0, "PIS/COFINS/CSLL Não Retidos")]
    [InlineData(2, "PIS/COFINS Não Retido")]
    public void Descricao_CodigoSemClassificacao_TemTextoDoLeiaute(int codigo, string esperado)
    {
        Assert.Equal(esperado, TpRetPisCofins.Descricao(codigo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(10)]
    public void Descricao_CodigoForaDoLeiaute_RetornaNull(int? codigo)
    {
        Assert.Null(TpRetPisCofins.Descricao(codigo));
    }

    [Fact]
    public void CodigosSuportados_SaoOsDoLeiauteVigente()
    {
        Assert.Equal(new[] { 1, 3, 4, 5, 6, 7, 8, 9 }, TpRetPisCofins.CodigosSuportados);
    }
}
