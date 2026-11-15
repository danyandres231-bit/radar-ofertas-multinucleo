using System.Globalization;
using System.Text.RegularExpressions;
using Radar.Core;
using Radar.Scraping.Http;

namespace Radar.Scraping.Fuentes;

/// <summary>Nintendo eShop: sitemap público de la tienda + ficha HTML (JSON-LD) con el precio.</summary>
public static partial class Nintendo
{
    public const string UrlSitemap = "https://www.nintendo.com/us/store/sitemap.xml";
    [GeneratedRegex(@"<loc>(https://www\.nintendo\.com/us/store/products/([a-z0-9-]+)/)</loc>")] private static partial Regex RxLoc();
    [GeneratedRegex(@"-\d{10,}")] private static partial Regex RxNsuid();

    public static List<Candidato> ParsearSitemap(string xml)
    {
        var l = new List<Candidato>();
        foreach (Match m in RxLoc().Matches(xml))
        {
            var slug = m.Groups[2].Value;
            string plat;
            if (slug.EndsWith("-switch-2")) { plat = "switch2"; slug = slug[..^9]; }
            else if (slug.EndsWith("-switch")) { plat = "switch"; slug = slug[..^7]; }
            else continue; // fichas regionales/duplicadas (-us, etc.)
            bool conId = RxNsuid().IsMatch(slug);
            slug = RxNsuid().Replace(slug, "");
            var titulo = Util.Titulo(slug);
            var tipo = conId ? "dlc" : Util.TipoPorTitulo(titulo);
            l.Add(new Candidato("nintendo", titulo, m.Groups[1].Value, plat, Formato: "digital", TipoProducto: tipo));
        }
        return l;
    }

    [GeneratedRegex(@"""regularPrice"":([\d.]+),""finalPrice"":([\d.]+)")] private static partial Regex RxPrecios();

    public static (double? precio, double? regular, string? titulo, string? img) ParsearFicha(string html)
    {
        double? precio = null; string? titulo = null, img = null;
        foreach (var e in Util.ObjetosLd(html))
        {
            if (!Util.EsTipo(e, "VideoGame") && !Util.EsTipo(e, "Product")) continue;
            titulo = Util.Str(e, "name"); img = Util.Str(e, "image");
            if (e.TryGetProperty("offers", out var of) && Util.Str(of, "price") is { } p && double.TryParse(p, NumberStyles.Float, Util.Inv, out var v)) precio = v;
            break;
        }
        double? regular = null;
        if (precio is not null)
            foreach (Match m in RxPrecios().Matches(html))
                if (double.Parse(m.Groups[2].Value, Util.Inv) == precio) { regular = double.Parse(m.Groups[1].Value, Util.Inv); break; }
        return (precio, regular, titulo, img);
    }
}

/// <summary>GameStop (minorista): /search está prohibido en robots.txt, así que se usa su sitemap público + ficha HTML (JSON-LD).</summary>
public static partial class GameStop
{
    public const string UrlIndice = "https://www.gamestop.com/sitemap_index.xml";
    static readonly HashSet<string> Plats = new() { "nintendo-switch", "nintendo-switch-2", "playstation-5", "playstation-4", "xbox-series-x", "xbox-one", "pc" };

    [GeneratedRegex(@"<loc>(https://www\.gamestop\.com/sitemap_\d+-product\.xml)</loc>")] private static partial Regex RxSitemaps();
    [GeneratedRegex(@"<loc>(https://www\.gamestop\.com/video-games/([a-z0-9-]+)/products/([a-z0-9-]+)/(\d+)\.html)</loc>")] private static partial Regex RxLoc();

    public static List<string> ParsearIndice(string xml) => RxSitemaps().Matches(xml).Select(m => m.Groups[1].Value).ToList();

    public static List<Candidato> ParsearSitemap(string xml)
    {
        var l = new List<Candidato>();
        foreach (Match m in RxLoc().Matches(xml))
        {
            var plat = m.Groups[2].Value;
            if (!Plats.Contains(plat)) continue;
            var slug = m.Groups[3].Value;
            var corte = slug.IndexOf("---", StringComparison.Ordinal);
            if (corte > 0) slug = slug[..corte];
            var titulo = Util.Titulo(slug);
            l.Add(new Candidato("gamestop", titulo, m.Groups[1].Value, plat, Formato: "fisico", TipoProducto: Util.TipoPorTitulo(titulo)));
        }
        return l;
    }

    /// <summary>Devuelve las ofertas nuevas (físico) y digitales. Los usados solo se usan si no hay ninguna nueva, marcados "(usado)".</summary>
    public static List<(double precio, string formato, string titulo, string? img)> ParsearFicha(string html)
    {
        var l = new List<(double, string, string, string?)>();
        var usados = new List<(double, string, string, string?)>();
        foreach (var e in Util.ObjetosLd(html))
        {
            if (!Util.EsTipo(e, "Product")) continue;
            var nombre = Util.Str(e, "name") ?? "";
            string? img = null;
            if (e.TryGetProperty("image", out var im))
            {
                var x = im.ValueKind == System.Text.Json.JsonValueKind.Array && im.GetArrayLength() > 0 ? im[0] : im;
                img = x.ValueKind == System.Text.Json.JsonValueKind.String ? x.GetString() : x.ValueKind == System.Text.Json.JsonValueKind.Object ? Util.Str(x, "url") ?? Util.Str(x, "contentUrl") : null;
            }
            if (!e.TryGetProperty("offers", out var ofs)) continue;
            var lista = ofs.ValueKind == System.Text.Json.JsonValueKind.Array ? ofs.EnumerateArray().ToList() : new() { ofs };
            foreach (var o in lista)
            {
                var cond = Util.Str(o, "itemCondition") ?? ""; var n = (Util.Str(o, "name") ?? "").ToLowerInvariant();
                bool usado = cond.Contains("Used") || n.Contains("pre-owned") || n.Contains("refurb");
                if (Util.Str(o, "price") is { } p && double.TryParse(p, NumberStyles.Float, Util.Inv, out var v) && v > 0)
                {
                    if (usado) usados.Add((v, "fisico", nombre + " (usado)", img));
                    else l.Add((v, n.Contains("digital") ? "digital" : "fisico", nombre, img));
                }
            }
            break;
        }
        return l.Count > 0 ? l : usados.OrderBy(x => x.Item1).Take(1).ToList();
    }
}
