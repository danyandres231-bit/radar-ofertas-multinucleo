using System.Diagnostics;
using Radar.Core;
using Radar.Core.Matching;
using Radar.Scraping.Fuentes;
using Radar.Scraping.Http;

namespace Radar.Pipeline;

/// <summary>Etapa 0 · Descubrir: catálogo desde Metacritic /browse (paginado) + CSV manual, e índices de candidatos desde sitemaps.</summary>
public static class Catalogo
{
    public static async Task<int> DescubrirAsync(Opciones op, Descargador d, Db db, TextWriter log, CancellationToken ct)
    {
        var porSlug = new Dictionary<string, Juego>();
        int pagina = 1;
        // Recorre las plataformas en "ronda" (página 1 de cada una, luego página 2...) hasta tener el mínimo.
        while (porSlug.Count < op.MinJuegos * 3 / 2 + 60 && pagina <= 20)
        {
            var tareas = Metacritic.PlataformasCatalogo.Select(async p =>
            {
                try { return Metacritic.ParsearListado(await d.GetAsync(Metacritic.UrlListado(p, pagina), ct), p); }
                catch (Exception e) { log.WriteLine($"  [etapa0] {p} p{pagina}: {e.Message}"); return new List<Juego>(); }
            });
            foreach (var lista in await Task.WhenAll(tareas))
                foreach (var j in lista)
                {
                    if (porSlug.TryGetValue(j.Slug, out var ya)) { foreach (var p in j.Plataformas) if (!ya.Plataformas.Contains(p)) ya.Plataformas.Add(p); }
                    else porSlug[j.Slug] = j;
                }
            log.WriteLine($"  [etapa0] página {pagina}: {porSlug.Count} juegos únicos");
            pagina++;
        }

        // Altas manuales (casos difíciles) y exclusiones, sin tocar código.
        foreach (var j in LeerCsvExtra(Path.Combine(op.RutaDatos, "juegos_extra.csv")))
            if (porSlug.TryGetValue(j.Slug, out var ya)) { ya.CasoDificil |= j.CasoDificil; ya.TitulosAlt.AddRange(j.TitulosAlt); }
            else porSlug[j.Slug] = j;
        var excluidos = LeerLineas(Path.Combine(op.RutaDatos, "juegos_excluidos.csv")).ToHashSet();

        foreach (var j in porSlug.Values.Where(j => j.Origen != "manual")) j.Tipo = TipoPorTitulo(j.Titulo);
        using var tx = db.Tx();
        foreach (var j in porSlug.Values.Where(j => !excluidos.Contains(j.Slug))) db.GuardarJuego(j);
        tx.Commit();
        if (excluidos.Count > 0) foreach (var s in excluidos) db.Exec("UPDATE juego SET activo=0 WHERE slug=$s", ("$s", s));
        return db.ContarJuegos();
    }

    /// <summary>Clasifica el tipo para el filtro del sitio: juego, juego+dlc (ediciones completas), dlc u otro.</summary>
    public static string TipoPorTitulo(string t)
    {
        var s = t.ToLowerInvariant();
        if (s.Contains("dlc") || s.Contains("expansion") || s.Contains("season pass")) return "dlc";
        if (s.Contains("complete") || s.Contains("goty") || s.Contains("game of the year") || s.Contains("definitive") || s.Contains("ultimate")
            || s.Contains(" + ") || s.Contains("royal") || s.Contains("final cut") || s.Contains("director")) return "juego+dlc";
        if (s.Contains("collection") || s.Contains("bundle") || s.Contains("trilogy")) return "otro";
        return "juego";
    }

    static IEnumerable<string> LeerLineas(string ruta) =>
        File.Exists(ruta) ? File.ReadAllLines(ruta).Skip(1).Select(l => l.Split(',')[0].Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')) : Array.Empty<string>();

    /// <summary>CSV: slug;titulo;alternativos (separados por |);plataformas (|);fecha;msrp;tipo;caso_dificil</summary>
    public static IEnumerable<Juego> LeerCsvExtra(string ruta)
    {
        if (!File.Exists(ruta)) yield break;
        foreach (var linea in File.ReadAllLines(ruta).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(linea) || linea.StartsWith('#')) continue;
            var c = linea.Split(';');
            yield return new Juego
            {
                Slug = c[0].Trim(), Titulo = c[1].Trim(),
                TitulosAlt = c.Length > 2 ? c[2].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() : new(),
                Plataformas = c.Length > 3 ? c[3].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() : new(),
                FechaLanz = c.Length > 4 && c[4].Trim().Length > 0 ? c[4].Trim() : null,
                Msrp = c.Length > 5 && double.TryParse(c[5], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : null,
                Tipo = c.Length > 6 && c[6].Trim().Length > 0 ? c[6].Trim() : "juego",
                CasoDificil = c.Length > 7 && c[7].Trim() == "1",
                Origen = "manual"
            };
        }
    }

    /// <summary>Descarga y normaliza los candidatos de sitemaps (Nintendo y GameStop). La normalización es parte serial del sistema.</summary>
    public static async Task<(List<CandidatoNorm> globales, double sDescarga, double sNormalizar)> IndicesAsync(Descargador d, TextWriter log, CancellationToken ct, int maxSitemapsGameStop = 40)
    {
        var sw = Stopwatch.StartNew();
        var crudos = new List<Candidato>();
        try { crudos.AddRange(Nintendo.ParsearSitemap(await d.GetAsync(Nintendo.UrlSitemap, ct))); }
        catch (Exception e) { log.WriteLine($"  [etapa0] sitemap Nintendo: {e.Message}"); }
        int nNin = crudos.Count;
        try
        {
            var mapas = GameStop.ParsearIndice(await d.GetAsync(GameStop.UrlIndice, ct)).Take(maxSitemapsGameStop).ToList();
            var partes = await Task.WhenAll(mapas.Select(async u =>
            {
                try { return GameStop.ParsearSitemap(await d.GetAsync(u, ct)); }
                catch (Exception e) { log.WriteLine($"  [etapa0] {u}: {e.Message}"); return new List<Candidato>(); }
            }));
            foreach (var p in partes) crudos.AddRange(p);
        }
        catch (Exception e) { log.WriteLine($"  [etapa0] sitemap GameStop: {e.Message}"); }
        double tDesc = sw.Elapsed.TotalSeconds;
        sw.Restart();
        var vistos = new HashSet<string>();
        var globales = new List<CandidatoNorm>(crudos.Count);
        foreach (var c in crudos)
            if (vistos.Add(c.Url)) globales.Add(Normalizador.Preparar(c));
        log.WriteLine($"  [etapa0] candidatos: Nintendo {nNin}, GameStop {crudos.Count - nNin}, únicos {globales.Count}");
        return (globales, tDesc, sw.Elapsed.TotalSeconds);
    }
}
