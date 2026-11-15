using System.Text.Json;
using System.Text.RegularExpressions;
using Radar.Core;
using Radar.Scraping.Http;

namespace Radar.Scraping.Fuentes;

/// <summary>Tienda Steam por scraping del HTML de resultados de búsqueda (incluye reseñas de usuarios).</summary>
public static partial class Steam
{
    public static string UrlBusqueda(string termino) =>
        $"https://store.steampowered.com/search/results/?term={Uri.EscapeDataString(termino)}&category1=998%2C21%2C996&infinite=1&cc=us&l=english&count=25";

    [GeneratedRegex(@"<a href=""(https://store\.steampowered\.com/(app|sub|bundle)/[^""]+)""(.*?)</a>", RegexOptions.Singleline)] private static partial Regex RxFila();
    [GeneratedRegex(@"<span class=""title"">([^<]*)</span>")] private static partial Regex RxTitulo();
    [GeneratedRegex(@"data-price-final=""(\d+)""")] private static partial Regex RxPrecio();
    [GeneratedRegex(@"discount_original_price"">([^<]*)<")] private static partial Regex RxOriginal();
    [GeneratedRegex(@"(\d+)% of the ([\d,]+) user reviews")] private static partial Regex RxResenas();
    [GeneratedRegex(@"<img src=""([^""]+)""")] private static partial Regex RxImg();

    public static List<Candidato> Parsear(string json)
    {
        string html;
        try { html = JsonDocument.Parse(json).RootElement.GetProperty("results_html").GetString() ?? ""; }
        catch { html = json; }
        var l = new List<Candidato>();
        foreach (Match m in RxFila().Matches(html))
        {
            var cuerpo = m.Groups[3].Value;
            var t = RxTitulo().Match(cuerpo);
            if (!t.Success) continue;
            var titulo = Util.Html(t.Groups[1].Value);
            var p = RxPrecio().Match(cuerpo);
            double? precio = p.Success ? int.Parse(p.Groups[1].Value) / 100.0 : null;
            var o = RxOriginal().Match(cuerpo);
            double? regular = o.Success ? Util.Dinero(o.Groups[1].Value) : precio;
            var r = RxResenas().Match(Util.Html(cuerpo));
            var img = RxImg().Match(cuerpo);
            var url = m.Groups[1].Value; var q = url.IndexOf("?snr", StringComparison.Ordinal); if (q > 0) url = url[..q];
            var tipo = m.Groups[2].Value == "app" ? Util.TipoPorTitulo(titulo) : "bundle";
            if (precio is null or 0 && !cuerpo.Contains("Free")) continue; // sin precio publicado
            l.Add(new Candidato("steam", titulo, url, "pc", precio ?? 0, regular, "digital",
                img.Success ? img.Groups[1].Value.Replace("capsule_sm_120", "header").Replace("capsule_231x87", "header") : null,
                r.Success ? double.Parse(r.Groups[1].Value) : null, tipo));
        }
        return l;
    }

    public static async Task<List<Candidato>> BuscarAsync(Descargador d, string termino, CancellationToken ct) =>
        Parsear(await d.GetAsync(UrlBusqueda(termino), ct));
}
