using System;
using DinkToPdf;
using DinkToPdf.Contracts;
namespace Direction.NFSe.Danfe;

public static class DanfePdfGenerator
{
    private static readonly IConverter _converter = new SynchronizedConverter(new PdfTools());

    public static byte[] Generate(string html)
    {
        try
        {
            var doc = new HtmlToPdfDocument()
            {
                GlobalSettings = {
                    ColorMode = ColorMode.Color,
                    Orientation = Orientation.Portrait,
                    PaperSize = PaperKind.A4,
                    Margins = new MarginSettings { Top = 2, Bottom = 2, Left = 2, Right = 2 },
                    DPI = 300
                },
                Objects = {
                    new ObjectSettings() {
                        PagesCount = true,
                        HtmlContent = html,
                        WebSettings = {
                            DefaultEncoding = "utf-8",
                            EnableIntelligentShrinking = false
                        },
                    }
                }
            };

            return _converter.Convert(doc);
        }
        catch (TypeInitializationException ex)
        {
            Console.WriteLine($"ERRO NO TIPO: {ex.Message}");
            if (ex.InnerException != null)
            {
                // ESSA é a mensagem que importa
                Console.WriteLine($"ERRO REAL (INNER): {ex.InnerException.Message}");
                Console.WriteLine($"STACK TRACE: {ex.InnerException.StackTrace}");
            }
            throw;
        }
    }
}
