using System.Globalization;
using System.Text.Json;
using Radar.Core;
using Radar.Scraping.Http;

namespace Radar.Scraping.Fuentes;

/// <summary>Tienda GOG por su API pública de catálogo (su HTML de búsqueda no filtra en el servidor; ver ADR-005).</summary>
public static class Gog
{
    public static string UrlBusqueda(string termino) =>
        $"https://catalog.gog.com/v1/catalog?query={Uri.EscapeDataString(termino)}&limit=20&countryCode=US&currencyCode=USD&locale=en-US&order=desc:score";

    public static List<Candidato> Parsear(string json)
    {
        var l = new List<Candidato>();
        if (string.IsNullOrWhiteSpace(json)) return l;   // GOG responde 400 cuando no hay resultados
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.GetProperty("products").EnumerateArray())
        {
            var titulo = p.GetProperty("title").GetString() ?? "";
            var tipoGog = p.TryGetProperty("productType", out var pt) ? pt.GetString() : "game";
            double? precio = null, regular = null;
            if (p.TryGetProperty("price", out var pr) && pr.ValueKind == JsonValueKind.Object)
            {
                if (pr.TryGetProperty("finalMoney", out var fm)) precio = double.Parse(fm.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture);
                if (pr.TryGetProperty("baseMoney", out var bm)) regular = double.Parse(bm.GetProperty("amount").GetString()!, CultureInfo.InvariantCulture);
            }
            if (precio is null) continue;
            l.Add(new Candidato("gog", titulo, p.GetProperty("storeLink").GetString() ?? "", "pc", precio, regular, "digital",
                p.TryGetProperty("coverVertical", out var cv) ? cv.GetString() : null, null,
                tipoGog == "game" ? Util.TipoPorTitulo(titulo) : tipoGog == "pack" ? "bundle" : "dlc"));
        }
        return l;
    }

    public static async Task<List<Candidato>> BuscarAsync(Descargador d, string termino, CancellationToken ct) =>
        Parsear(await d.GetAsync(UrlBusqueda(termino), ct));
}
