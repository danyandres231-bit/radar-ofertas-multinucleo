using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Radar.Core;
using Radar.Scraping.Http;

namespace Radar.Scraping.Fuentes;

/// <summary>Etapa 0 (listado /browse paginado) y calificación de crítica (ficha /game/). robots.txt permite ambas rutas.</summary>
public static partial class Metacritic
{
    public static readonly string[] PlataformasCatalogo = { "pc", "ps5", "switch", "xbox-series-x", "ps4" };

    public static string UrlListado(string plataforma, int pagina) =>
        $"https://www.metacritic.com/browse/game/{plataforma}/all/all-time/metascore/?releaseYearMin=2016&releaseYearMax=2026&page={pagina}";

    [GeneratedRegex(@"href=""/game/([^/""]+)/""")] private static partial Regex RxSlug();
    [GeneratedRegex(@"alt=""([^""]*)""")] private static partial Regex RxAlt();
    [GeneratedRegex(@">\s*([A-Z][a-z]{2} \d{1,2}, \d{4})\s*<")] private static partial Regex RxFecha();
    [GeneratedRegex(@"title=""Metascore (\d+)")] private static partial Regex RxScore();
    [GeneratedRegex(@"src=""(https://www\.metacritic\.com/a/img/[^""]+)""")] private static partial Regex RxImg();

    public static List<Juego> ParsearListado(string html, string plataforma)
    {
        var l = new List<Juego>();
        foreach (var card in html.Split("data-testid=\"filter-results\"").Skip(1))
        {
            var slug = RxSlug().Match(card); var alt = RxAlt().Match(card);
            if (!slug.Success || !alt.Success || alt.Groups[1].Value.Length == 0) continue;
            var f = RxFecha().Match(card); var s = RxScore().Match(card); var img = RxImg().Match(card);
            string? fecha = f.Success && DateTime.TryParseExact(f.Groups[1].Value, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd") : null;
            l.Add(new Juego
            {
                Slug = slug.Groups[1].Value,
                Titulo = Util.Html(alt.Groups[1].Value),
                FechaLanz = fecha,
                MetascoreCatalogo = s.Success ? int.Parse(s.Groups[1].Value) : null,
                ImagenUrl = img.Success ? Util.Html(img.Groups[1].Value).Replace("height=144", "height=432").Replace("width=96", "width=288") : null,
                Plataformas = { Radar.Core.Matching.Plataformas.Canonica(plataforma == "xbox-series-x" ? "series" : plataforma) },
                Origen = "metacritic:browse"
            });
        }
        return l;
    }

    public sealed record Ficha(int? Metascore, List<string> Plataformas, string? Fecha, string? Imagen);

    public static string UrlFicha(string slug) => $"https://www.metacritic.com/game/{slug}/";

    public static async Task<Ficha> FichaAsync(Descargador d, string slug, CancellationToken ct) =>
        ParsearFicha(await d.GetAsync(UrlFicha(slug), ct));

    public static Ficha ParsearFicha(string html)
    {
        int? score = null; var plats = new List<string>(); string? fecha = null, img = null;
        foreach (var e in Util.ObjetosLd(html))
        {
            if (!Util.EsTipo(e, "VideoGame")) continue;
            if (e.TryGetProperty("aggregateRating", out var ar) && Util.Str(ar, "ratingValue") is { } rv && double.TryParse(rv, NumberStyles.Float, Util.Inv, out var v)) score = (int)Math.Round(v);
            if (e.TryGetProperty("gamePlatform", out var gp))
                plats = gp.ValueKind == JsonValueKind.Array ? gp.EnumerateArray().Select(x => x.GetString()!).ToList() : new() { gp.GetString()! };
            fecha = Util.Str(e, "datePublished");
            img = Util.Str(e, "image");
        }
        return new Ficha(score, plats, fecha, img);
    }
}
