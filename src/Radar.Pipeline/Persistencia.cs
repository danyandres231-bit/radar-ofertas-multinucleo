using System.Text.Json;
using Radar.Core;
using Radar.Core.Matching;
using Radar.Scraping.Http;

namespace Radar.Pipeline;

/// <summary>Etapa 4 · Persistir: un solo hilo escribe en SQLite. Cada observación de precio se INSERTA (nunca se sobrescribe).</summary>
public static class Persistencia
{
    static readonly string[] FuentesJuego = { "steam", "gog", "metacritic", "duracion", "nintendo", "gamestop" };

    public static long Guardar(Db db, Opciones op, ResultadoEjecucion r, Descargador d)
    {
        var fin = DateTime.UtcNow;
        using var tx = db.Tx();
        db.Exec("INSERT INTO ejecucion(inicio,fin,modo,workers_io,workers_cpu,replay,tiempos_etapa_json) VALUES($i,$f,$m,$wi,$wc,$r,$t)",
            ("$i", r.Inicio.ToString("o")), ("$f", fin.ToString("o")), ("$m", op.Modo.ToString().ToLowerInvariant()), ("$wi", op.WorkersIo),
            ("$wc", op.WorkersCpu), ("$r", op.Replay ? 1 : 0), ("$t", JsonSerializer.Serialize(r.TiemposEtapa.Append(new("total", r.Total)).ToDictionary(k => k.Key, k => Math.Round(k.Value, 3)))));
        long ej = db.Scalar<long>("SELECT last_insert_rowid()");
        string ahora = fin.ToString("o");

        foreach (var f in r.Fichas)
        {
            var j = f.Juego;
            // Datos del juego que completa Metacritic.
            var plats = f.PlataformasMc.Select(Plataformas.Canonica).Where(p => p is "pc" or "ps5" or "ps4" or "switch" or "switch2" or "xbox-series" or "xbox-one").Distinct();
            foreach (var p in plats) db.Exec("INSERT OR IGNORE INTO plataforma VALUES($j,$p)", ("$j", j.Id), ("$p", p));
            // MSRP: mayor precio regular visto en tiendas de plataforma (ADR-003 lo usa cuando el sitio no muestra precio tachado).
            double? msrp = j.Msrp ?? f.Observaciones.Where(o => o.Tienda is "steam" or "gog" or "nintendo").Select(o => (double?)(o.PrecioRegular ?? o.PrecioActual)).DefaultIfEmpty(null).Max();
            if (msrp is 0) msrp = null;
            var img = j.ImagenUrl ?? f.ImagenMc ?? f.CandidatosBusqueda.FirstOrDefault(c => c.Imagen != null)?.Imagen;
            db.Exec("UPDATE juego SET fecha_lanz=COALESCE(fecha_lanz,$f), imagen_url=COALESCE(imagen_url,$i), msrp_usd=COALESCE(msrp_usd,$m) WHERE id=$j",
                ("$f", f.FechaMc), ("$i", img), ("$m", msrp), ("$j", j.Id));

            foreach (var o0 in f.Observaciones)
            {
                var o = o0;
                if (o.PrecioRegular is null && msrp is not null) { var (of, cr) = Oferta.Evaluar(o.PrecioActual, null, msrp); o = o with { EnOferta = of, CriterioOferta = cr }; }
                db.Exec("""
                    INSERT INTO observacion_precio(juego_id,tienda_id,ejecucion_id,precio_actual,precio_regular,en_oferta,criterio_oferta,formato,plataforma,url,titulo_tienda,similitud,dudoso,obtenido_en)
                    VALUES($j,$t,$e,$p,$r,$o,$c,$fo,$pl,$u,$tt,$s,$d,$ob)
                    """, ("$j", j.Id), ("$t", o.Tienda), ("$e", ej), ("$p", o.PrecioActual), ("$r", o.PrecioRegular), ("$o", o.EnOferta ? 1 : 0),
                    ("$c", o.CriterioOferta), ("$fo", o.Formato), ("$pl", o.Plataforma), ("$u", o.Url), ("$tt", o.TituloTienda), ("$s", o.Similitud),
                    ("$d", o.Dudoso ? 1 : 0), ("$ob", o.ObtenidoEn.ToString("o")));
            }
            if (f.Metascore is not null)
                db.Exec("INSERT OR REPLACE INTO calificacion VALUES($j,'metacritic','todas',$v,$o)", ("$j", j.Id), ("$v", f.Metascore), ("$o", ahora));
            var steamPct = f.Emparejados.FirstOrDefault(e => e.Candidato.Tienda == "steam")?.Candidato.ReseñasPct;
            if (steamPct is not null)
                db.Exec("INSERT OR REPLACE INTO calificacion VALUES($j,'steam_usuarios','pc',$v,$o)", ("$j", j.Id), ("$v", steamPct), ("$o", ahora));
            if (f.Duracion is { } du)
                db.Exec("INSERT OR REPLACE INTO duracion VALUES($j,$f,$a,$b,$c,$o)", ("$j", j.Id), ("$f", du.Fuente), ("$a", du.PrincipalH), ("$b", du.ExtrasH), ("$c", du.CompletistaH), ("$o", ahora));
            foreach (var e in f.Dudosos)
                db.Exec("INSERT INTO dudoso VALUES($e,$j,$t,$tt,$u,$s,$m)", ("$e", ej), ("$j", j.Id), ("$t", e.Candidato.Tienda), ("$tt", e.Candidato.Titulo), ("$u", e.Candidato.Url), ("$s", e.Similitud), ("$m", e.Motivo));
            foreach (var fu in FuentesJuego)
            {
                bool fallo = f.FuentesFallidas.Contains(fu);
                db.Exec(fallo
                    ? "INSERT INTO fuente_estado(juego_id,fuente,ultimo_fallo) VALUES($j,$f,$o) ON CONFLICT DO UPDATE SET ultimo_fallo=$o"
                    : "INSERT INTO fuente_estado(juego_id,fuente,ultimo_ok) VALUES($j,$f,$o) ON CONFLICT DO UPDATE SET ultimo_ok=$o",
                    ("$j", j.Id), ("$f", fu), ("$o", ahora));
            }
        }
        foreach (var x in r.Fallos)
            db.Exec("INSERT INTO fallo VALUES($e,$j,$f,$u,$er,$r)", ("$e", ej), ("$j", x.JuegoId), ("$f", x.Fuente), ("$u", x.Url), ("$er", x.Error + (x.Recuperado ? " (recuperado al reintentar)" : "")), ("$r", x.Reintentado ? 1 : 0));
        foreach (var (dom, est) in d.Estadisticas.OrderBy(k => k.Key))
            db.Exec("INSERT INTO solicitud_log VALUES($e,$d,$ok,$re,$fa)", ("$e", ej), ("$d", dom), ("$ok", est.Exitosas + est.DesdeCache), ("$re", est.Reintentadas), ("$fa", est.Fallidas));
        tx.Commit();
        return ej;
    }
}
