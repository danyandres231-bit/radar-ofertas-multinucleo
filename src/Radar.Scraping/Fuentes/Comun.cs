using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Radar.Scraping.Fuentes;

static partial class Util
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static double? Dinero(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = Regex.Match(s, @"(\d+(?:[.,]\d{3})*(?:\.\d+)?)");
        return m.Success && double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, Inv, out var v) ? v : null;
    }

    public static string Html(string s) => WebUtility.HtmlDecode(s).Trim();

    [GeneratedRegex(@"<script[^>]*type=""application/ld\+json""[^>]*>(.*?)</script>", RegexOptions.Singleline)]
    public static partial Regex JsonLd();

    /// <summary>Recorre todos los bloques JSON-LD (incluido @graph) y devuelve los objetos.</summary>
    public static IEnumerable<JsonElement> ObjetosLd(string html)
    {
        foreach (Match m in JsonLd().Matches(html))
        {
            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(m.Groups[1].Value); } catch { }
            if (doc is null) continue;
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array) { foreach (var e in root.EnumerateArray()) yield return e; }
            else if (root.TryGetProperty("@graph", out var g) && g.ValueKind == JsonValueKind.Array) { foreach (var e in g.EnumerateArray()) yield return e; }
            else yield return root;
        }
    }

    public static bool EsTipo(JsonElement e, string tipo)
    {
        if (!e.TryGetProperty("@type", out var t)) return false;
        return t.ValueKind == JsonValueKind.String ? t.GetString() == tipo : t.EnumerateArray().Any(x => x.GetString() == tipo);
    }

    public static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null } : null;

    public static string Titulo(string slug) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('-', ' ').Trim());

    public static readonly string[] PalabrasNoJuego = { "dlc", "pack", "bundle", "season pass", "soundtrack", "expansion", "upgrade", "costume", "currency", "coins", "demo" };
    public static string TipoPorTitulo(string t)
    {
        var s = t.ToLowerInvariant();
        return PalabrasNoJuego.Any(s.Contains) ? (s.Contains("bundle") || s.Contains("pack") ? "bundle" : "dlc") : "juego";
    }
}
