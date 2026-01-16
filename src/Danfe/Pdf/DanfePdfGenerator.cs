using System;
using System.IO;
using PdfSharp;
using PdfSharp.Pdf;
using TheArtOfDev.HtmlRenderer.PdfSharp;

namespace Direction.NFSe.Danfe;

public static class DanfePdfGenerator
{
    public static byte[] Generate(string html)
    {
        Byte[] res = null;
        using (MemoryStream ms = new MemoryStream())
        {
            PdfDocument pdf = PdfGenerator.GeneratePdf(html, PageSize.A4);

            pdf.Save(ms);
            res = ms.ToArray();
        }

        return res;
    }
}
