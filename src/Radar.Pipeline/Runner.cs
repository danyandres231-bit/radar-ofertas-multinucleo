using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Radar.Core;
using Radar.Core.Matching;
using Radar.Scraping.Fuentes;
using Radar.Scraping.Http;

namespace Radar.Pipeline;

public sealed class FalloRegistrado
{
    public required long JuegoId { get; init; }
    public required string Fuente { get; init; }
    public required string Url { get; init; }
    public required string Error { get; init; }
    public bool Reintentado { get; set; }
    public bool Recuperado { get; set; }
}

public sealed class ResultadoEjecucion
{
    public required List<FichaJuego> Fichas { get; init; }
    public required Dictionary<string, double> TiemposEtapa { get; init; }   // segundos de pared por etapa
    public required double Total { get; init; }
    public required string Hash { get; init; }
    public required List<FalloRegistrado> Fallos { get; init; }
    public required DateTime Inicio { get; init; }
}

/// <summary>
/// Pipeline de etapas 1 → 2 → 3 → 1b → 4 conectadas por Channel&lt;T&gt; acotados (ADR-004, ADR-010).
/// Tres modos: Seq (todo en un hilo), Conc (E/S con workers), Par (además emparejamiento en N núcleos).
/// </summary>
public sealed class Runner
{
    readonly Opciones _op;
    readonly Descargador _d;
    readonly Igdb _igdb;
    readonly Emparejador _emp;
    readonly Dictionary<string, string> _hltbIds;
    readonly ConcurrentBag<FalloRegistrado> _fallos = new();
    readonly TextWriter _log;

    // Acumuladores de tiempo ocupado por etapa (ticks), para separar descarga de parseo.
    long _ticksParseo;

    public Runner(Opciones op, Descargador d, TextWriter log)
    {
        _op = op; _d = d; _log = log;
        _igdb = new Igdb(d);
        _emp = new Emparejador(op.Umbral);
        _hltbIds = LeerHltb(Path.Combine(op.RutaDatos, "hltb_ids.csv"));
    }

    static Dictionary<string, string> LeerHltb(string ruta) =>
        File.Exists(ruta)
            ? File.ReadAllLines(ruta).Skip(1).Where(l => !l.TrimStart().StartsWith("#")).Select(l => l.Split(',')).Where(c => c.Length >= 2 && c[1].Trim().Length > 0).ToDictionary(c => c[0].Trim(), c => c[1].Trim())
            : new();

    void Fallo(FichaJuego f, string fuente, Exception e)
    {
        lock (f.FuentesFallidas) f.FuentesFallidas.Add(fuente);
        _fallos.Add(new FalloRegistrado { JuegoId = f.Juego.Id, Fuente = fuente, Url = (e as FalloDescarga)?.Url ?? "", Error = e.Message });
    }

    T Parsear<T>(Func<T> f)
    {
        long t0 = Stopwatch.GetTimestamp();
        try { return f(); }
        finally { Interlocked.Add(ref _ticksParseo, Stopwatch.GetTimestamp() - t0); }
    }

    // ---------------- Etapas 1 + 2: descargar y parsear las fuentes de un juego ----------------

    /// <summary>Nivel 2 de paralelismo: las fuentes de un mismo juego en paralelo (o en serie si secuencial).</summary>
    public async Task<FichaJuego> DescargarJuegoAsync(Juego j, bool paralelo, CancellationToken ct)
    {
        var f = new FichaJuego { Juego = j };
        var fuentes = new List<Func<Task>>
        {
            async () =>
            {
                try { var raw = await _d.GetAsync(Steam.UrlBusqueda(j.Titulo), ct); var l = Parsear(() => Steam.Parsear(raw)); lock (f.CandidatosBusqueda) f.CandidatosBusqueda.AddRange(l); }
                catch (Exception e) when (e is not OperationCanceledException) { Fallo(f, "steam", e); }
            },
            async () =>
            {
                try { var raw = await _d.GetAsync(Gog.UrlBusqueda(j.Titulo), ct, vacioEn: new[] { 400 }); var l = Parsear(() => Gog.Parsear(raw)); lock (f.CandidatosBusqueda) f.CandidatosBusqueda.AddRange(l); }
                catch (Exception e) when (e is not OperationCanceledException) { Fallo(f, "gog", e); }
            },
            async () =>
            {
                if (j.Origen == "manual" && !j.Slug.Contains('-')) return;
                try
                {
                    var raw = await _d.GetAsync(Metacritic.UrlFicha(j.Slug), ct);
                    var mc = Parsear(() => Metacritic.ParsearFicha(raw));
                    f.Metascore = mc.Metascore ?? j.MetascoreCatalogo; f.PlataformasMc = mc.Plataformas; f.FechaMc = mc.Fecha; f.ImagenMc = mc.Imagen;
                }
                catch (Exception e) when (e is not OperationCanceledException) { Fallo(f, "metacritic", e); f.Metascore = j.MetascoreCatalogo; }
            },
            async () =>
            {
                try
                {
                    if (_hltbIds.TryGetValue(j.Slug, out var hid)) { var raw = await _d.GetAsync($"https://howlongtobeat.com/game/{hid}", ct); f.Duracion = Parsear(() => Hltb.Parsear(raw)); }
                    else if (_igdb.Disponible(_op.Replay)) f.Duracion = await _igdb.DuracionAsync(j.Titulo, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException) { Fallo(f, "duracion", e); }
            }
        };
        if (paralelo) await Task.WhenAll(fuentes.Select(x => x()));
        else foreach (var x in fuentes) await x();
        // Orden determinista de candidatos (las tareas terminan en cualquier orden).
        var orden = f.CandidatosBusqueda.OrderBy(c => c.Tienda, StringComparer.Ordinal).ThenBy(c => c.Url, StringComparer.Ordinal).ToList();
        f.CandidatosBusqueda.Clear(); f.CandidatosBusqueda.AddRange(orden);
        return f;
    }

    // ---------------- Etapa 3: emparejar (CPU) ----------------

    public void Emparejar(FichaJuego f, IReadOnlyList<CandidatoNorm> globales)
    {
        var propios = f.CandidatosBusqueda.Select(Normalizador.Preparar).ToList();
        _emp.Emparejar(f, globales, propios);
        var ahora = DateTime.UtcNow;
        // Steam y GOG ya traen el precio en la búsqueda: se convierten en observaciones aquí.
        foreach (var e in f.Emparejados.Where(e => e.Candidato.Tienda is "steam" or "gog" && e.Candidato.Precio is not null))
        {
            var c = e.Candidato;
            var (oferta, crit) = Oferta.Evaluar(c.Precio!.Value, c.PrecioRegular, f.Juego.Msrp);
            f.Observaciones.Add(new Observacion(c.Tienda, c.Precio.Value, c.PrecioRegular, oferta, crit, c.Formato, "pc", c.Url, c.Titulo, e.Similitud, e.Dudoso, ahora));
        }
    }

    // ---------------- Etapa 1b: fichas de Nintendo/GameStop emparejadas ----------------

    /// <summary>Nivel 3 de paralelismo: variantes (plataformas/formatos) de una tienda en paralelo, máximo N.</summary>
    public async Task DescargarFichasAsync(FichaJuego f, bool paralelo, CancellationToken ct)
    {
        var objetivos = f.Emparejados.Where(e => e.Candidato.Tienda is "nintendo" or "gamestop")
            .OrderByDescending(e => e.Similitud).ThenBy(e => e.Candidato.Url, StringComparer.Ordinal)
            .GroupBy(e => e.Candidato.Tienda).SelectMany(g => g.Take(_op.MaxVariantesPorTienda)).ToList();
        var obs = new ConcurrentBag<Observacion>();

        async Task Una(Emparejamiento e)
        {
            var c = e.Candidato;
            try
            {
                var html = await _d.GetAsync(c.Url, ct);
                var ahora = DateTime.UtcNow;
                if (c.Tienda == "nintendo")
                {
                    var (precio, regular, titulo, _) = Parsear(() => Nintendo.ParsearFicha(html));
                    if (precio is null) return;
                    var (of, cr) = Oferta.Evaluar(precio.Value, regular, f.Juego.Msrp);
                    obs.Add(new Observacion("nintendo", precio.Value, regular, of, cr, "digital", Plataformas.Canonica(c.Plataforma ?? "switch"), c.Url, titulo ?? c.Titulo, e.Similitud, e.Dudoso, ahora));
                }
                else
                {
                    foreach (var (precio, formato, titulo, _) in Parsear(() => GameStop.ParsearFicha(html)))
                    {
                        var (of, cr) = Oferta.Evaluar(precio, null, f.Juego.Msrp);
                        obs.Add(new Observacion("gamestop", precio, null, of, cr, formato, Plataformas.Canonica(c.Plataforma ?? ""), c.Url, titulo, e.Similitud, e.Dudoso, ahora));
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { Fallo(f, c.Tienda, ex); }
        }

        if (paralelo) await Task.WhenAll(objetivos.Select(Una));
        else foreach (var e in objetivos) await Una(e);
        f.Observaciones.AddRange(obs.OrderBy(o => o.Tienda, StringComparer.Ordinal).ThenBy(o => o.Url, StringComparer.Ordinal).ThenBy(o => o.Formato).ThenBy(o => o.PrecioActual));
    }

    // ---------------- Pipeline completo ----------------

    public async Task<ResultadoEjecucion> EjecutarAsync(List<Juego> juegos, IReadOnlyList<CandidatoNorm> globales, CancellationToken ct)
    {
        var inicio = DateTime.UtcNow;
        _ticksParseo = 0;
        var swTotal = Stopwatch.StartNew();
        var tiempos = new ConcurrentDictionary<string, double>();
        var fichas = new List<FichaJuego>();

        if (_op.Modo == Modo.Seq)
        {
            // Línea base: un juego y una fuente a la vez, un solo hilo de trabajo.
            double tDesc = 0, tEmp = 0, tFich = 0;
            foreach (var j in juegos)
            {
                var t0 = Stopwatch.GetTimestamp();
                var f = DescargarJuegoAsync(j, paralelo: false, ct).GetAwaiter().GetResult();
                var t1 = Stopwatch.GetTimestamp();
                Emparejar(f, globales);
                var t2 = Stopwatch.GetTimestamp();
                DescargarFichasAsync(f, paralelo: false, ct).GetAwaiter().GetResult();
                var t3 = Stopwatch.GetTimestamp();
                tDesc += Seg(t1 - t0); tEmp += Seg(t2 - t1); tFich += Seg(t3 - t2);
                fichas.Add(f);
            }
            tiempos["1_descargar"] = tDesc;
            tiempos["3_emparejar"] = tEmp;
            tiempos["1b_fichas"] = tFich;
        }
        else
        {
            var ch0 = Channel.CreateBounded<Juego>(new BoundedChannelOptions(_op.ColaDescubrir) { FullMode = BoundedChannelFullMode.Wait, SingleWriter = true });
            var ch1 = Channel.CreateBounded<FichaJuego>(new BoundedChannelOptions(_op.ColaDescargar) { FullMode = BoundedChannelFullMode.Wait });
            var ch2 = Channel.CreateBounded<FichaJuego>(new BoundedChannelOptions(_op.ColaDescargar) { FullMode = BoundedChannelFullMode.Wait });
            var ch3 = Channel.CreateBounded<FichaJuego>(new BoundedChannelOptions(_op.ColaPersistir) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

            var productor = Task.Run(async () =>
            {
                foreach (var j in juegos) await ch0.Writer.WriteAsync(j, ct);
                ch0.Writer.Complete();
            }, ct);

            // Etapa 1+2 · E/S: nivel 1 (por juego) con workers; nivel 2 (por fuente) con Task.WhenAll.
            var descarga = Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                await Parallel.ForEachAsync(ch0.Reader.ReadAllAsync(ct), new ParallelOptions { MaxDegreeOfParallelism = _op.WorkersIo, CancellationToken = ct },
                    async (j, t) => await ch1.Writer.WriteAsync(await DescargarJuegoAsync(j, paralelo: true, t), t));
                ch1.Writer.Complete();
                tiempos["1_descargar"] = sw.Elapsed.TotalSeconds;
            }, ct);

            // Etapa 3 · CPU: Conc = un consumidor; Par = N consumidores en núcleos distintos.
            var emparejar = Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                int dop = _op.Modo == Modo.Par ? _op.WorkersCpu : 1;
                await Parallel.ForEachAsync(ch1.Reader.ReadAllAsync(ct), new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct },
                    async (f, t) => { Emparejar(f, globales); await ch2.Writer.WriteAsync(f, t); });
                ch2.Writer.Complete();
                tiempos["3_emparejar"] = sw.Elapsed.TotalSeconds;
            }, ct);

            // Etapa 1b · E/S: fichas de producto (nivel 3 dentro de cada juego).
            var fichasTask = Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                await Parallel.ForEachAsync(ch2.Reader.ReadAllAsync(ct), new ParallelOptions { MaxDegreeOfParallelism = _op.WorkersIo, CancellationToken = ct },
                    async (f, t) => { await DescargarFichasAsync(f, paralelo: true, t); await ch3.Writer.WriteAsync(f, t); });
                ch3.Writer.Complete();
                tiempos["1b_fichas"] = sw.Elapsed.TotalSeconds;
            }, ct);

            // Recolector único (la escritura en BD es de un solo hilo, etapa 4).
            await foreach (var f in ch3.Reader.ReadAllAsync(ct)) fichas.Add(f);
            await Task.WhenAll(productor, descarga, emparejar, fichasTask);
        }
        tiempos["2_parsear_cpu"] = Seg(_ticksParseo); // tiempo de CPU de parseo, ya incluido dentro de 1 y 1b
        double tPipeline = swTotal.Elapsed.TotalSeconds;

        // Cola de fallidos: se reintenta al final.
        var swF = Stopwatch.StartNew();
        await ReintentarFallidosAsync(fichas, globales, ct);
        tiempos["cola_fallidos"] = swF.Elapsed.TotalSeconds;

        fichas.Sort((a, b) => a.Juego.Id.CompareTo(b.Juego.Id));
        tiempos["pipeline"] = tPipeline;
        return new ResultadoEjecucion
        {
            Fichas = fichas, TiemposEtapa = new(tiempos), Total = swTotal.Elapsed.TotalSeconds,
            Hash = HashResultado(fichas), Fallos = _fallos.OrderBy(x => x.JuegoId).ThenBy(x => x.Fuente).ToList(), Inicio = inicio
        };
    }

    static double Seg(long ticks) => (double)ticks / Stopwatch.Frequency;

    async Task ReintentarFallidosAsync(List<FichaJuego> fichas, IReadOnlyList<CandidatoNorm> globales, CancellationToken ct)
    {
        var porId = fichas.ToDictionary(f => f.Juego.Id);
        var grupos = _fallos.Where(x => !x.Reintentado).GroupBy(x => x.JuegoId).ToList();
        if (grupos.Count == 0) return;
        var nuevos = new ConcurrentDictionary<long, FichaJuego>();
        await Parallel.ForEachAsync(grupos, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _op.Modo == Modo.Seq ? 1 : _op.WorkersIo), CancellationToken = ct }, async (g, t) =>
        {
            foreach (var x in g) x.Reintentado = true;
            var antes = _fallos.Count;
            var f = await DescargarJuegoAsync(porId[g.Key].Juego, paralelo: false, t);
            Emparejar(f, globales);
            await DescargarFichasAsync(f, paralelo: false, t);
            var falloNuevo = _fallos.Any(y => y.JuegoId == g.Key && !y.Reintentado);
            if (!falloNuevo) foreach (var x in g) x.Recuperado = true;
            nuevos[g.Key] = f;
        });
        // Fallos producidos en el reintento: quedan registrados como reintentados.
        foreach (var x in _fallos) x.Reintentado = true;
        for (int i = 0; i < fichas.Count; i++)
            if (nuevos.TryGetValue(fichas[i].Juego.Id, out var nf) && nf.Observaciones.Count >= fichas[i].Observaciones.Count) fichas[i] = nf;
    }

    /// <summary>Hash de lo que se emparejó y los precios obtenidos (sin fechas): los tres modos deben dar el mismo.</summary>
    public static string HashResultado(IEnumerable<FichaJuego> fichas)
    {
        var sb = new StringBuilder();
        foreach (var f in fichas.OrderBy(f => f.Juego.Id))
        {
            sb.Append(f.Juego.Id).Append('|').Append(f.Metascore).Append('|');
            foreach (var e in f.Emparejados.OrderBy(e => e.Candidato.Url, StringComparer.Ordinal)) sb.Append(e.Candidato.Url).Append(':').Append(e.Similitud.ToString("F4")).Append(';');
            foreach (var o in f.Observaciones.OrderBy(o => o.Url, StringComparer.Ordinal).ThenBy(o => o.Formato).ThenBy(o => o.PrecioActual)) sb.Append(o.Url).Append('=').Append(o.PrecioActual).Append(';');
            sb.Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }
}
