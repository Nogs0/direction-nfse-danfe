using System.Text.RegularExpressions;

internal static class HtmlNormalization
{
    public static string Normalize(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        // Normalizações típicas:
        html = html.Replace("\r\n", "\n");

        // Colapsa qualquer sequência de espaços/tabs/quebras de linha em um único espaço —
        // mesma semântica de colapso de whitespace que um navegador aplica ao renderizar HTML,
        // então diferenças de indentação/quebra de linha (ex.: reformatação via Prettier) não
        // devem quebrar o snapshot.
        html = Regex.Replace(html, @"\s+", " ");

        // Remove espaço redundante entre tags adjacentes
        html = Regex.Replace(html, @">\s+<", "><");

        // Se tiver base64 muito variável:
        // html = Regex.Replace(html, "data:image/png;base64,[A-Za-z0-9+/=]+", "data:image/png;base64,<redacted>");

        return html.Trim();
    }
}
