using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Radar.Bench;
using Radar.Core;
using Radar.Core.Matching;
using Radar.Pipeline;
using Radar.Scraping.Fuentes;
using Radar.Scraping.Http;

// Banco de pruebas (modo replay). Uso:
//   dotnet run --project src/Radar.Bench -c Release -- todo        (todo el experimento, ~2 h)
//   dotnet run --project src/Radar.Bench -c Release -- ficha | descarga | emparejar | pipeline | contencion | carrera
//   opciones: --reps 5 --latencia 100 --jitter 0 --limite N
var cmd = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "todo";
int reps = Arg("--reps", 5), latencia = Arg("--latencia", 100), jitter = Arg("--jitter", 0);
int? limite = args.Contains("--limite") ? Arg("--limite", 0) : null;
var modosArg = args.Contains("--modos") ? args[Array.IndexOf(args, "--modos") + 1].Split(',') : new[] { "seq", "conc", "par" };
int Arg(string n, int def) { var i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? int.Parse(args[i + 1]) : def; }

var res = Resultados.Cargar();
res.Ficha = Ficha.Obtener();
Console.WriteLine($"Máquina: {res.Ficha.Procesador} · {res.Ficha.NucleosLogicos} lógicos · SMT {(res.Ficha.Smt ? "sí" : "no")} · {res.Ficha.Runtime}");

Opciones Op(int io, int cpu, Modo modo, int lat, int jit) => new()
{
    Replay = true, LatenciaMs = lat, JitterMs = jit, WorkersIo = io, WorkersCpu = cpu, Modo = modo,
    Persistir = false, Publicar = false, CacheHoras = 0, MaxReintentos = 0
};

List<Juego> Juegos()
{
    using var db = new Db(new Opciones().RutaDb);
    var l = db.CargarJuegos();
    return limite is int n ? l.Take(n).ToList() : l;
}

async Task<List<CandidatoNorm>> Globales()
{
    using var d = new Descargador(Op(8, 1, Modo.Par, 0, 0));
    return (await Catalogo.IndicesAsync(d, TextWriter.Null, CancellationToken.None)).globales;
}

var cuerpo = new Dictionary<string, Func<Task>>
{
    ["ficha"] = () => Task.CompletedTask,

    // ---------- Etapa 1+2: descarga (E/S) aislada. Tareas planas (juego × fuente) con p workers ----------
    ["descarga"] = async () =>
    {
        var juegos = Juegos();
        var tareas = juegos.SelectMany(j => new[] { Steam.UrlBusqueda(j.Titulo), Gog.UrlBusqueda(j.Titulo), Metacritic.UrlFicha(j.Slug) }).ToList();
        Console.WriteLine($"[descarga] {tareas.Count} solicitudes, latencia {latencia}±{jitter} ms");
        async Task<double> Medir(int? p)
        {
            using var d = new Descargador(Op(p ?? 1, 1, Modo.Conc, latencia, jitter));
            var sw = Stopwatch.StartNew();
            async Task Una(string u) { try { var s = await d.GetAsync(u); Parsear(u, s); } catch (FalloDescarga) { } }
            if (p is null) foreach (var u in tareas) Una(u).GetAwaiter().GetResult();   // secuencial: un hilo, una a la vez
            else await Parallel.ForEachAsync(tareas, new ParallelOptions { MaxDegreeOfParallelism = p.Value }, async (u, _) => await Una(u));
            return sw.Elapsed.TotalSeconds;
        }
        var e = Barrido("descarga", reps, new[] { 1, 2, 4, 8, 16, 32, 64 }, Medir, latencia, jitter, calentar: false);
        res.Etapas[$"descarga_lat{latencia}_jit{jitter}"] = await e;
    },

    // ---------- Etapa 3: emparejamiento (CPU) aislado ----------
    ["emparejar"] = async () =>
    {
        var juegos = Juegos();
        var globales = await Globales();
        // Fichas de entrada: se descargan una vez en replay sin latencia (no se mide).
        var fichasBase = await DescargarTodo(juegos);
        Console.WriteLine($"[emparejar] {fichasBase.Count} juegos × {globales.Count} candidatos globales");
        var emp = new Emparejador();
        var propios = fichasBase.ToDictionary(f => f.Juego.Id, f => (IReadOnlyList<CandidatoNorm>)f.CandidatosBusqueda.Select(Normalizador.Preparar).ToList());
        FichaJuego Nueva(FichaJuego f) { var n = new FichaJuego { Juego = f.Juego }; n.PlataformasMc = f.PlataformasMc; return n; }
        string? hashSec = null;
        Task<double> Medir(int? p)
        {
            var fichas = fichasBase.Select(Nueva).ToList();
            var sw = Stopwatch.StartNew();
            if (p is null) foreach (var f in fichas) emp.Emparejar(f, globales, propios[f.Juego.Id]);
            else Parallel.ForEach(fichas, new ParallelOptions { MaxDegreeOfParallelism = p.Value }, f => emp.Emparejar(f, globales, propios[f.Juego.Id]));
            var t = sw.Elapsed.TotalSeconds;
            var h = Runner.HashResultado(fichas);
            if (p is null) hashSec = h; else if (hashSec != null && h != hashSec) throw new Exception($"El resultado paralelo (p={p}) difiere del secuencial");
            return Task.FromResult(t);
        }
        int L = Environment.ProcessorCount;
        var ws = new[] { 1, 2, 4, L, 2 * L }.Distinct().OrderBy(x => x).ToArray();
        res.Etapas["emparejar"] = await Barrido("emparejar", reps, ws, Medir, 0, 0);

        // Extra: misma etapa con PLINQ (comparación de mecanismos).
        var plinq = new Dictionary<int, List<double>>();
        foreach (var p in ws)
        {
            plinq[p] = new();
            for (int r = 0; r < Math.Min(reps, 3); r++)
            {
                var fichas = fichasBase.Select(Nueva).ToList();
                var sw = Stopwatch.StartNew();
                fichas.AsParallel().WithDegreeOfParallelism(p).ForAll(f => emp.Emparejar(f, globales, propios[f.Juego.Id]));
                plinq[p].Add(sw.Elapsed.TotalSeconds);
            }
            Console.WriteLine($"  [plinq] p={p} mediana {Estad.Mediana(plinq[p]):F2} s");
        }
        res.Plinq = plinq.ToDictionary(k => k.Key.ToString(), k => Estad.Resumen(k.Value));
    },

    // ---------- Pipeline completo en los tres modos ----------
    ["pipeline"] = async () =>
    {
        var juegos = Juegos();
        var globales = await Globales();
        int L = Environment.ProcessorCount;
        foreach (var lat in new[] { latencia })
        {
            var tiempos = new Dictionary<string, List<double>>();
            var etapas = new Dictionary<string, List<Dictionary<string, double>>>();
            string? hash = null; bool distinto = false;
            foreach (var (nombre, modo, io, cpu) in new[] { ("seq", Modo.Seq, 1, 1), ("conc", Modo.Conc, 16, 1), ("par", Modo.Par, 16, L) }.Where(x => modosArg.Contains(x.Item1)))
            {
                tiempos[nombre] = new(); etapas[nombre] = new();
                for (int r = 0; r < reps; r++)
                {
                    using var d = new Descargador(Op(io, cpu, modo, lat, jitter));
                    var runner = new Runner(Op(io, cpu, modo, lat, jitter), d, TextWriter.Null);
                    var rr = await runner.EjecutarAsync(juegos, globales, CancellationToken.None);
                    tiempos[nombre].Add(rr.TiemposEtapa["pipeline"]);
                    etapas[nombre].Add(rr.TiemposEtapa.ToDictionary(k => k.Key, k => Math.Round(k.Value, 3)));
                    hash ??= rr.Hash; if (rr.Hash != hash) distinto = true;
                    Console.WriteLine($"  [pipeline lat={lat}] {nombre} rep {r + 1}: {rr.TiemposEtapa["pipeline"]:F2} s hash={rr.Hash}{(rr.Hash != hash ? " ¡DISTINTO!" : "")}");
                }
            }
            var clave = $"lat{lat}_jit{jitter}";
            var rp = res.Pipeline.TryGetValue(clave, out var previo) ? previo : new ResultadoPipeline { Latencia = lat, Jitter = jitter, MismoResultado = true };
            foreach (var (k, v) in tiempos) rp.Modos[k] = Estad.Resumen(v);
            foreach (var (k, v) in etapas) rp.Etapas[k] = v.First().Keys.ToDictionary(e => e, e => Math.Round(Estad.Mediana(v.Select(x => x[e]).ToList()), 3));
            rp.MismoResultado &= !distinto && (rp.Hash is null || rp.Hash == hash); rp.Hash = hash;
            res.Pipeline[clave] = rp;
        }
    },

    // ---------- Validación del emparejamiento ----------
    // "pares": genera candidatos a revisar a mano (data/validacion_pares.csv, columna correcto vacía).
    ["pares"] = async () =>
    {
        var juegos = Juegos(); var globales = await Globales();
        var fichas = await DescargarTodo(juegos);
        var rnd = new Random(7);
        var elegidos = fichas.Where(f => f.Juego.CasoDificil).Concat(fichas.Where(f => !f.Juego.CasoDificil).OrderBy(_ => rnd.Next()).Take(15)).ToList();
        var emp = new Emparejador();
        var lineas = new List<string> { "juego_slug;juego_titulo;tienda;titulo_tienda;url;score;correcto" };
        foreach (var f in elegidos)
        {
            var claves = Emparejador.ClavesDe(f.Juego);
            var todos = globales.Concat(f.CandidatosBusqueda.Select(Normalizador.Preparar));
            foreach (var g in todos.Select(c => (c, s: emp.Puntuar(claves, false, c).score)).Where(x => x.s >= 0.72).GroupBy(x => x.c.C.Tienda))
                foreach (var (c, sc) in g.OrderByDescending(x => x.s).Take(2))
                    lineas.Add($"{f.Juego.Slug};{f.Juego.Titulo};{c.C.Tienda};{c.C.Titulo.Replace(';', ',')};{c.C.Url};{sc:F3};");
        }
        File.WriteAllLines("data/validacion_pares_sin_revisar.csv", lineas);
        Console.WriteLine($"  {lineas.Count - 1} pares generados en data/validacion_pares_sin_revisar.csv");
    },

    // "validar": precisión y exhaustividad por umbral con los pares revisados (data/validacion_pares.csv).
    ["validar"] = () =>
    {
        using var db = new Db(new Opciones().RutaDb);
        var juegos = db.CargarJuegos().ToDictionary(j => j.Slug);
        var pares = File.ReadAllLines("data/validacion_pares.csv").Skip(1).Select(l => l.Split(';')).Where(c => c.Length >= 7 && c[6].Trim().Length > 0).ToList();
        var emp = new Emparejador();
        var puntuados = pares.Select(c =>
        {
            var j = juegos[c[0]];
            var cand = Normalizador.Preparar(new Candidato(c[2], c[3], c[4], null));
            var (s, motivo) = emp.Puntuar(Emparejador.ClavesDe(j), j.Tipo != "juego", cand, Emparejador.AnioDe(j.FechaLanz));
            return (s, motivo, real: c[6].Trim() == "1", juego: c[1], tienda: c[3]);
        }).ToList();
        var curva = new List<object>();
        double mejorF1 = -1, mejorU = 0;
        for (double u = 0.78; u <= 0.981; u += 0.01)
        {
            int tp = puntuados.Count(x => x.real && x.s >= u && x.motivo == null), fp = puntuados.Count(x => !x.real && x.s >= u && x.motivo == null), fn = puntuados.Count(x => x.real && !(x.s >= u && x.motivo == null));
            double pr = tp + fp == 0 ? 1 : (double)tp / (tp + fp), rc = tp + fn == 0 ? 1 : (double)tp / (tp + fn), f1 = pr + rc == 0 ? 0 : 2 * pr * rc / (pr + rc);
            curva.Add(new { Umbral = Math.Round(u, 2), Precision = Math.Round(pr, 3), Exhaustividad = Math.Round(rc, 3), F1 = Math.Round(f1, 3), TP = tp, FP = fp, FN = fn });
            if (f1 > mejorF1 + 1e-9) { mejorF1 = f1; mejorU = Math.Round(u, 2); }
            Console.WriteLine($"  umbral {u:F2}: precisión {pr:F3} exhaustividad {rc:F3} F1 {f1:F3}");
        }
        var errores = puntuados.Where(x => x.real != (x.s >= 0.88 && x.motivo == null)).Select(x => new { x.juego, x.tienda, Score = Math.Round(x.s, 3), x.motivo, Real = x.real });
        res.Validacion = new { Pares = puntuados.Count, Positivos = puntuados.Count(x => x.real), Curva = curva, MejorUmbral = mejorU, MejorF1 = Math.Round(mejorF1, 3), ErroresConUmbral088 = errores };
        return Task.CompletedTask;
    },

    ["contencion"] = () => { res.Contencion = Contencion.Ejecutar(reps); return Task.CompletedTask; },
    ["carrera"] = () => { res.Carrera = Contencion.Carrera(); return Task.CompletedTask; },
};

var orden = cmd == "todo" ? new[] { "ficha", "contencion", "carrera", "descarga", "emparejar", "pipeline" } : new[] { cmd };
foreach (var c in orden)
{
    var sw = Stopwatch.StartNew();
    Console.WriteLine($"== {c}");
    await cuerpo[c]();
    res.Guardar();
    Console.WriteLine($"== {c} listo en {sw.Elapsed.TotalMinutes:F1} min");
}

static void Parsear(string url, string s)
{
    if (url.Contains("steampowered")) Steam.Parsear(s);
    else if (url.Contains("gog.com")) Gog.Parsear(s);
    else Metacritic.ParsearFicha(s);
}

async Task<List<FichaJuego>> DescargarTodo(List<Juego> juegos)
{
    var op = Op(32, 1, Modo.Conc, 0, 0);
    using var d = new Descargador(op);
    var runner = new Runner(op, d, TextWriter.Null);
    var bag = new ConcurrentBag<FichaJuego>();
    await Parallel.ForEachAsync(juegos, new ParallelOptions { MaxDegreeOfParallelism = 32 }, async (j, t) => bag.Add(await runner.DescargarJuegoAsync(j, true, t)));
    return bag.OrderBy(f => f.Juego.Id).ToList();
}

static async Task<ResultadoEtapa> Barrido(string nombre, int reps, int[] workers, Func<int?, Task<double>> medir, int lat, int jit, bool calentar = true)
{
    var sec = new List<double>();
    if (calentar) await medir(1); // calentamiento (JIT); no se cuenta
    for (int r = 0; r < reps; r++) { sec.Add(await medir(null)); Console.WriteLine($"  [{nombre}] sec rep {r + 1}: {sec[^1]:F2} s"); }
    var porP = new Dictionary<int, List<double>>();
    foreach (var p in workers)
    {
        porP[p] = new();
        for (int r = 0; r < reps; r++) porP[p].Add(await medir(p));
        Console.WriteLine($"  [{nombre}] p={p}: mediana {Estad.Mediana(porP[p]):F2} s");
    }
    return Estad.Tabla(sec, porP, lat, jit);
}
