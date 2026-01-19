using System;
using System.IO;
using System.Reflection.Metadata;
using HtmlRendererCore.PdfSharp;
using PdfSharpCore;
using PdfSharpCore.Pdf;
namespace Direction.NFSe.Danfe;

public static class DanfePdfGenerator
{
    public static byte[] Generate(string html)
    {
        Byte[] res = null;
        PdfDocument pdf = new PdfDocument();
        var config = new PdfGenerateConfig
        {
            PageSize = PageSize.A4,
            MarginTop = 2,
            MarginBottom = 2,
            MarginLeft = 2,
            MarginRight = 2
        };

        PdfGenerator.AddPdfPages(
            pdf,
            html,
            config
        );

        using MemoryStream ms = new MemoryStream();
        pdf.Save(ms);
        res = ms.ToArray();

        return res;
    }
}
