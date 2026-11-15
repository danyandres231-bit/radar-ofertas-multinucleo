using System.Diagnostics;
using System.Text.Json;
using Radar.Core;
using Radar.Pipeline;
using Radar.Scraping.Http;

// Radar de Ofertas Multinúcleo · IC-3101
// Uso:
//   dotnet run --project src/Radar.Pipeline -- descubrir [--capturar]
//   dotnet run --project src/Radar.Pipeline -- ejecutar --modo seq|conc|par [--io 16] [--cpu 2] [--replay --latencia 100 --jitter 20] [--capturar] [--limite 50]
//   dotnet run --project src/Radar.Pipeline -- publicar

var op = Config.Cargar(args);
var comando = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "ejecutar";
var log = Console.Out;
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

using var db = new Db(op.RutaDb);
using var d = new Descargador(op);
log.WriteLine($"Radar de Ofertas · {comando} · modo={op.Modo} io={op.WorkersIo} cpu={op.WorkersCpu} replay={op.Replay} latencia={op.LatenciaMs}±{op.JitterMs}ms núcleos={Environment.ProcessorCount}");

switch (comando)
{
    case "descubrir":
    {
        var sw = Stopwatch.StartNew();
        int n = await Catalogo.DescubrirAsync(op, d, db, log, cts.Token);
        log.WriteLine($"Catálogo: {n} juegos en {sw.Elapsed.TotalSeconds:F1} s");
        break;
    }
    case "ejecutar":
    {
        var sw = Stopwatch.StartNew();
        if (op.Descubrir || db.ContarJuegos() < op.MinJuegos)
        {
            int n = await Catalogo.DescubrirAsync(op, d, db, log, cts.Token);
            log.WriteLine($"  [etapa0] catálogo: {n} juegos");
        }
        double t0Catalogo = sw.Elapsed.TotalSeconds;
        var (globales, tIdx, tNorm) = await Catalogo.IndicesAsync(d, log, cts.Token);
        var juegos = db.CargarJuegos();
        if (op.LimiteJuegos is int lim) juegos = juegos.Take(lim).ToList();
        var runner = new Runner(op, d, log);
        var r = await runner.EjecutarAsync(juegos, globales, cts.Token);
        r.TiemposEtapa["0_descubrir"] = t0Catalogo + tIdx;
        r.TiemposEtapa["0_normalizar_candidatos"] = tNorm;
        var sw4 = Stopwatch.StartNew();
        long ej = op.Persistir ? Persistencia.Guardar(db, op, r, d) : 0;
        r.TiemposEtapa["4_persistir"] = sw4.Elapsed.TotalSeconds;
        if (op.Persistir) db.Exec("UPDATE ejecucion SET tiempos_etapa_json=$t WHERE id=$e",
            ("$t", JsonSerializer.Serialize(r.TiemposEtapa.Append(new("total", sw.Elapsed.TotalSeconds)).ToDictionary(k => k.Key, k => Math.Round(k.Value, 3)))), ("$e", ej));
        var sw5 = Stopwatch.StartNew();
        if (op.Publicar && op.Persistir) Publicador.Publicar(db, op, log);
        log.WriteLine($"  [etapa5] publicar: {sw5.Elapsed.TotalSeconds:F2} s");
        log.WriteLine($"Juegos: {r.Fichas.Count} · emparejados: {r.Fichas.Sum(f => f.Emparejados.Count)} · dudosos: {r.Fichas.Sum(f => f.Dudosos.Count)} · observaciones: {r.Fichas.Sum(f => f.Observaciones.Count)} · fallos: {r.Fallos.Count}");
        foreach (var (k, v) in r.TiemposEtapa.OrderBy(k => k.Key)) log.WriteLine($"  {k,-26} {v,8:F2} s");
        log.WriteLine($"  {"TOTAL",-26} {sw.Elapsed.TotalSeconds,8:F2} s   hash={r.Hash}");
        foreach (var (dom, e) in d.Estadisticas.OrderBy(k => k.Key))
            log.WriteLine($"  {dom,-28} ok={e.Exitosas} cache={e.DesdeCache} reint={e.Reintentadas} fall={e.Fallidas}");
        break;
    }
    case "publicar":
        Publicador.Publicar(db, op, log);
        break;
    default:
        log.WriteLine("Comandos: descubrir | ejecutar | publicar");
        break;
}

namespace Radar.Pipeline
{
    public static class Config
    {
        /// <summary>Lee config/radar.json (si existe) y aplica los argumentos de línea de comandos.</summary>
        public static Opciones Cargar(string[] args)
        {
            var op = File.Exists("config/radar.json")
                ? JsonSerializer.Deserialize<Opciones>(File.ReadAllText("config/radar.json"), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } })!
                : new Opciones();
            for (int i = 0; i < args.Length; i++)
            {
                string? Sig() => i + 1 < args.Length ? args[++i] : null;
                switch (args[i])
                {
                    case "--modo": op.Modo = Enum.Parse<Modo>(Sig()!, true); break;
                    case "--io": op.WorkersIo = int.Parse(Sig()!); break;
                    case "--cpu": op.WorkersCpu = int.Parse(Sig()!); break;
                    case "--replay": op.Replay = true; break;
                    case "--capturar": op.Capturar = true; break;
                    case "--latencia": op.LatenciaMs = int.Parse(Sig()!); break;
                    case "--jitter": op.JitterMs = int.Parse(Sig()!); break;
                    case "--limite": op.LimiteJuegos = int.Parse(Sig()!); break;
                    case "--umbral": op.Umbral = double.Parse(Sig()!, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--descubrir": op.Descubrir = true; break;
                    case "--sin-publicar": op.Publicar = false; break;
                    case "--sin-persistir": op.Persistir = false; break;
                    case "--sin-cache": op.CacheHoras = 0; break;
                    case "--datos": op.RutaDatos = Sig()!; break;
                    case "--web": op.RutaWeb = Sig()!; break;
                }
            }
            return op;
        }
    }
}
