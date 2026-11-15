using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Radar.Core;
using Radar.Core.Matching;

namespace Radar.Pipeline;

/// <summary>Etapa 5 · Publicar: genera los JSON que lee el sitio estático (web/data).</summary>
public static class Publicador
{
    static readonly JsonSerializerOptions Js = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    static readonly Dictionary<string, string> NombreTienda = new() { ["steam"] = "Steam", ["gog"] = "GOG", ["nintendo"] = "Nintendo eShop", ["gamestop"] = "GameStop" };

    sealed record Precio(string Tienda, string TiendaNombre, double Actual, double? Regular, int Descuento, bool EnOferta, string Criterio,
        string Formato, string? Plataforma, string Url, string TituloTienda, double Similitud, bool Dudoso, string ObtenidoEn, bool Desactualizado);

    static string? Img(string? u) => u is null ? null : u.Contains("metacritic.com") ? Radar.Scraping.Fuentes.Metacritic.ImagenOriginal(u) : u;

    public static void Publicar(Db db, Opciones op, TextWriter log)
    {
        var dir = op.RutaWeb;
        Directory.CreateDirectory(Path.Combine(dir, "juego"));
        var ultima = db.Query("SELECT id,inicio,fin,modo,workers_io,workers_cpu,replay,tiempos_etapa_json FROM ejecucion ORDER BY id DESC LIMIT 1",
            r => new { Id = r.GetInt64(0), Inicio = r.GetString(1), Fin = r.GetString(2), Modo = r.GetString(3), WIo = r.GetInt32(4), WCpu = r.GetInt32(5), Replay = r.GetInt32(6) == 1, Tiempos = r.GetString(7) }).FirstOrDefault();
        if (ultima is null) { log.WriteLine("No hay ejecuciones para publicar."); return; }

        var juegos = db.CargarJuegos().ToDictionary(j => j.Id);
        var cal = db.Query("SELECT juego_id,fuente,valor,obtenido_en FROM calificacion", r => (Id: r.GetInt64(0), F: r.GetString(1), V: r.GetDouble(2), O: r.GetString(3)))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.ToList());
        var dur = db.Query("SELECT juego_id,fuente,principal_h,extras_h,completista_h,obtenido_en FROM duracion",
            r => (Id: r.GetInt64(0), F: r.GetString(1), A: r.IsDBNull(2) ? (double?)null : r.GetDouble(2), B: r.IsDBNull(3) ? (double?)null : r.GetDouble(3), C: r.IsDBNull(4) ? (double?)null : r.GetDouble(4), O: r.GetString(5)))
            .ToDictionary(x => x.Id);
        var estado = db.Query("SELECT juego_id,fuente,ultimo_ok,ultimo_fallo FROM fuente_estado",
            r => (Id: r.GetInt64(0), F: r.GetString(1), Ok: r.IsDBNull(2) ? null : r.GetString(2), Fa: r.IsDBNull(3) ? null : r.GetString(3)))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.ToList());

        // Todas las observaciones (para historial) — se agrupan en memoria.
        var obs = db.Query("""
            SELECT juego_id,tienda_id,precio_actual,precio_regular,en_oferta,criterio_oferta,formato,plataforma,url,titulo_tienda,similitud,dudoso,obtenido_en,ejecucion_id
            FROM observacion_precio ORDER BY obtenido_en
            """, r => (Id: r.GetInt64(0), T: r.GetString(1), P: r.GetDouble(2), R: r.IsDBNull(3) ? (double?)null : r.GetDouble(3), Of: r.GetInt32(4) == 1,
                Cr: r.GetString(5), Fo: r.GetString(6), Pl: r.IsDBNull(7) ? null : r.GetString(7), U: r.GetString(8), Tt: r.GetString(9), S: r.GetDouble(10),
                D: r.GetInt32(11) == 1, O: r.GetString(12), E: r.GetInt64(13)))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.ToList());

        var tarjetas = new List<object>();
        var noPublicados = new List<object>();
        foreach (var j in juegos.Values)
        {
            obs.TryGetValue(j.Id, out var lo); lo ??= new();
            // Último precio por (tienda, formato, plataforma).
            var actuales = lo.GroupBy(x => (x.T, x.Fo, x.Pl)).Select(g => g.Last())
                .Select(x => new Precio(x.T, NombreTienda.GetValueOrDefault(x.T, x.T), x.P, x.R, Oferta.PorcentajeDescuento(x.P, x.R), x.Of, x.Cr, x.Fo,
                    x.Pl is null ? null : Plataformas.Nombre(x.Pl), x.U, x.Tt, x.S, x.D, x.O, x.E != ultima.Id))
                .OrderBy(p => p.Actual).ThenBy(p => p.Tienda).ToList();
            int nTiendas = actuales.Select(p => p.Tienda).Distinct().Count();
            if (nTiendas < 2)
            {
                noPublicados.Add(new { j.Id, j.Titulo, Tiendas = actuales.Select(p => p.Tienda).Distinct(), Motivo = nTiendas == 0 ? "sin_emparejar" : "una_sola_tienda" });
                continue;
            }
            var mejor = actuales[0];
            double minHist = lo.Min(x => x.P);
            cal.TryGetValue(j.Id, out var cj); cj ??= new();
            var meta = cj.FirstOrDefault(c => c.F == "metacritic"); var steamU = cj.FirstOrDefault(c => c.F == "steam_usuarios");
            estado.TryGetValue(j.Id, out var est); est ??= new();
            var desact = est.Where(e => e.Fa != null && (e.Ok == null || string.CompareOrdinal(e.Fa, e.Ok) > 0))
                .Select(e => new { Fuente = e.F, Desde = e.Ok }).ToList();
            var plats = actuales.Select(p => p.Plataforma).Where(p => p != null).Distinct().OrderBy(p => p).ToList();

            tarjetas.Add(new
            {
                j.Id, j.Slug, j.Titulo, Imagen = Img(j.ImagenUrl), j.Tipo, j.FechaLanz, Plataformas = plats,
                Formatos = actuales.Select(p => p.Formato).Distinct().OrderBy(x => x),
                Tiendas = actuales.Select(p => p.Tienda).Distinct().OrderBy(x => x),
                Mejor = new { mejor.Tienda, mejor.TiendaNombre, mejor.Actual, mejor.Regular, mejor.Descuento, mejor.Url, mejor.Formato, mejor.Plataforma },
                DescuentoMax = actuales.Max(p => p.Descuento),
                EnOferta = actuales.Any(p => p.EnOferta),
                MinimoHistorico = mejor.Actual <= minHist + 0.001,
                Metascore = meta.F is null ? (double?)null : meta.V,
                SteamUsuarios = steamU.F is null ? (double?)null : steamU.V,
                Desactualizado = desact.Count > 0
            });

            dur.TryGetValue(j.Id, out var dj);
            var historial = lo.GroupBy(x => x.T).OrderBy(g => g.Key).Select(g => new
            {
                Tienda = g.Key, Nombre = NombreTienda.GetValueOrDefault(g.Key, g.Key),
                Puntos = g.GroupBy(x => x.O[..10]).Select(d => new { Fecha = d.Key, Precio = d.Min(x => x.P) })
            });
            var detalle = new
            {
                j.Id, j.Slug, j.Titulo, Imagen = Img(j.ImagenUrl), j.Tipo, j.FechaLanz, Msrp = j.Msrp, Plataformas = plats, TitulosAlt = j.TitulosAlt,
                Precios = actuales, MinimoHistorico = minHist,
                Calificaciones = cj.Select(c => new { c.F, c.V, c.O }),
                Duracion = dj.F is null ? null : new { Fuente = dj.F, Principal = dj.A, Extras = dj.B, Completista = dj.C, ObtenidoEn = dj.O },
                Historial = historial, Desactualizado = desact
            };
            File.WriteAllText(Path.Combine(dir, "juego", $"{j.Id}.json"), JsonSerializer.Serialize(detalle, Js));
        }

        File.WriteAllText(Path.Combine(dir, "juegos.json"), JsonSerializer.Serialize(new { Actualizado = ultima.Fin, Total = tarjetas.Count, Juegos = tarjetas }, Js));

        // Panel de métricas.
        var porSitio = db.Query("SELECT dominio,exitosas,reintentadas,fallidas FROM solicitud_log WHERE ejecucion_id=$e ORDER BY dominio",
            r => new { Dominio = r.GetString(0), Exitosas = r.GetInt32(1), Reintentadas = r.GetInt32(2), Fallidas = r.GetInt32(3) }, ("$e", ultima.Id));
        var fallos = db.Query("SELECT f.juego_id, j.titulo, f.fuente, f.url, f.error FROM fallo f LEFT JOIN juego j ON j.id=f.juego_id WHERE ejecucion_id=$e ORDER BY f.fuente LIMIT 300",
            r => new { JuegoId = r.GetInt64(0), Titulo = r.IsDBNull(1) ? "" : r.GetString(1), Fuente = r.GetString(2), Url = r.GetString(3), Error = r.GetString(4) }, ("$e", ultima.Id));
        var dudosos = db.Query("SELECT d.juego_id, j.titulo, d.tienda_id, d.titulo_tienda, d.url, d.similitud, d.motivo FROM dudoso d JOIN juego j ON j.id=d.juego_id WHERE ejecucion_id=$e ORDER BY d.similitud DESC LIMIT 400",
            r => new { JuegoId = r.GetInt64(0), Titulo = r.GetString(1), Tienda = r.GetString(2), TituloTienda = r.GetString(3), Url = r.GetString(4), Similitud = r.GetDouble(5), Motivo = r.IsDBNull(6) ? null : r.GetString(6) }, ("$e", ultima.Id));
        var ejecuciones = db.Query("SELECT id,inicio,fin,modo,workers_io,workers_cpu,replay,tiempos_etapa_json FROM ejecucion ORDER BY id DESC LIMIT 20",
            r => new { Id = r.GetInt64(0), Inicio = r.GetString(1), Fin = r.GetString(2), Modo = r.GetString(3), WorkersIo = r.GetInt32(4), WorkersCpu = r.GetInt32(5), Replay = r.GetInt32(6) == 1, Tiempos = JsonDocument.Parse(r.GetString(7)).RootElement.Clone() });

        var metricas = new
        {
            Ultima = new { ultima.Id, ultima.Inicio, ultima.Fin, ultima.Modo, WorkersIo = ultima.WIo, WorkersCpu = ultima.WCpu, ultima.Replay, Tiempos = JsonDocument.Parse(ultima.Tiempos).RootElement.Clone() },
            Catalogo = juegos.Count, Publicados = tarjetas.Count, NoPublicados = noPublicados,
            Solicitudes = porSitio, Fallos = fallos, Dudosos = dudosos, Ejecuciones = ejecuciones
        };
        File.WriteAllText(Path.Combine(dir, "metricas.json"), JsonSerializer.Serialize(metricas, Js));

        var bench = Path.Combine(op.RutaDatos, "bench", "resultados.json");
        if (File.Exists(bench)) File.Copy(bench, Path.Combine(dir, "bench.json"), true);
        log.WriteLine($"  [etapa5] publicados {tarjetas.Count} de {juegos.Count} juegos; {noPublicados.Count} sin 2 tiendas");
    }
}
