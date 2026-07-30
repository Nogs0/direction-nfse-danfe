using System;
using System.Threading.Tasks;
using Direction.NFSe.Danfe;
using Xunit;

namespace Danfe.Tests;

public class DanfePdfGeneratorTests
{
    private const string HtmlSimples = "<html><body>teste</body></html>";

    [Fact]
    public void DeveReciclarPagina_QuandoSucessoENaoFechadaEPoolComEspaco_RetornaTrue()
    {
        var generator = new DanfePdfGenerator(poolSize: 2);

        Assert.True(generator.DeveReciclarPagina(sucesso: true, paginaFechada: false, quantidadeAtualNoPool: 0));
    }

    [Fact]
    public void DeveReciclarPagina_QuandoFalhaNaRenderizacao_RetornaFalse()
    {
        var generator = new DanfePdfGenerator(poolSize: 2);

        Assert.False(generator.DeveReciclarPagina(sucesso: false, paginaFechada: false, quantidadeAtualNoPool: 0));
    }

    [Fact]
    public void DeveReciclarPagina_QuandoPaginaJaFechada_RetornaFalse()
    {
        var generator = new DanfePdfGenerator(poolSize: 2);

        Assert.False(generator.DeveReciclarPagina(sucesso: true, paginaFechada: true, quantidadeAtualNoPool: 0));
    }

    [Fact]
    public void DeveReciclarPagina_QuandoPoolNoLimite_RetornaFalse()
    {
        var generator = new DanfePdfGenerator(poolSize: 2);

        Assert.False(generator.DeveReciclarPagina(sucesso: true, paginaFechada: false, quantidadeAtualNoPool: 2));
    }

    [Fact]
    public void DeveReciclarPagina_QuandoPoolAbaixoDoLimite_RetornaTrue()
    {
        var generator = new DanfePdfGenerator(poolSize: 3);

        Assert.True(generator.DeveReciclarPagina(sucesso: true, paginaFechada: false, quantidadeAtualNoPool: 1));
    }

    // Cobre o cenário de aceite do item 4.3: derrubar o navegador interno durante a operação e
    // confirmar que, após a recuperação automática, a geração volta a funcionar sem falha
    // residual (o pool de páginas órfãs do processo anterior precisa ser drenado no relaunch).
    [Trait("Category", "Integration")]
    [Fact]
    public async Task GenerateAsync_QuandoNavegadorInternoCai_RecuperaNaProximaChamada()
    {
        await using var generator = new DanfePdfGenerator(poolSize: 1);

        var primeiroPdf = await generator.GenerateAsync(HtmlSimples);
        Assert.True(primeiroPdf.Length > 0);

        var browser = await generator.GetBrowserAsync();
        await browser.CloseAsync();

        var segundoPdf = await generator.GenerateAsync(HtmlSimples);

        Assert.True(segundoPdf.Length > 0);
        Assert.Equal((byte)'%', segundoPdf[0]);
    }
}
