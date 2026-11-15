using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Radar.Core;

namespace Radar.Bench;

public sealed class Resumen
{
    public double Mediana { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Desv { get; set; }
    public List<double> Muestras { get; set; } = new();
}

public sealed class FilaMetricas
{
    public int P { get; set; }
    public Resumen T { get; set; } = new();
    public double Sabs { get; set; }
    public double Srel { get; set; }
    public double E { get; set; }
    public double EPorNucleo { get; set; }
    public double? KarpFlatt { get; set; }
}

public sealed class ResultadoEtapa
{
    public int Latencia { get; set; }
    public int Jitter { get; set; }
    public Resumen Secuencial { get; set; } = new();
    public List<FilaMetricas> Filas { get; set; } = new();
    public double? FraccionSerialAmdahl { get; set; }
}

public sealed class ResultadoPipeline
{
    public int Latencia { get; set; }
    public int Jitter { get; set; }
    public Dictionary<string, Resumen> Modos { get; set; } = new();
    public Dictionary<string, Dictionary<string, double>> Etapas { get; set; } = new();
    public bool MismoResultado { get; set; }
    public string? Hash { get; set; }
}

public sealed class FichaMaquina
{
    public string Procesador { get; set; } = "";
    public int NucleosFisicos { get; set; }
    public int NucleosLogicos { get; set; }
    public bool Smt { get; set; }
    public string Memoria { get; set; } = "";
    public string SistemaOperativo { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string Fecha { get; set; } = "";
}

public sealed class Resultados
{
    public FichaMaquina Ficha { get; set; } = new();
    public Dictionary<string, ResultadoEtapa> Etapas { get; set; } = new();
    public Dictionary<string, ResultadoPipeline> Pipeline { get; set; } = new();
    public Dictionary<string, Resumen>? Plinq { get; set; }
    public object? Contencion { get; set; }
    public object? Carrera { get; set; }
    public object? Validacion { get; set; }

    const string Ruta = "data/bench/resultados.json";
    static readonly JsonSerializerOptions Js = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static Resultados Cargar() =>
        File.Exists(Ruta) ? JsonSerializer.Deserialize<Resultados>(File.ReadAllText(Ruta), Js) ?? new() : new();

    public void Guardar()
    {
        Directory.CreateDirectory("data/bench");
        File.WriteAllText(Ruta, JsonSerializer.Serialize(this, Js));
        Directory.CreateDirectory("web/data");
        File.Copy(Ruta, "web/data/bench.json", true);
    }
}

public static class Estad
{
    public static double Mediana(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        return s.Count == 0 ? 0 : s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    public static Resumen Resumen(List<double> v)
    {
        double m = v.Average();
        return new Resumen
        {
            Mediana = Math.Round(Mediana(v), 4), Min = Math.Round(v.Min(), 4), Max = Math.Round(v.Max(), 4),
            Desv = Math.Round(Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / Math.Max(1, v.Count - 1)), 4),
            Muestras = v.Select(x => Math.Round(x, 4)).ToList()
        };
    }

    /// <summary>Tabla del Anexo 2: S_abs, S_rel, E, Karp–Flatt; y fracción serial f ajustada a Amdahl.</summary>
    public static ResultadoEtapa Tabla(List<double> sec, Dictionary<int, List<double>> porP, int lat, int jit)
    {
        var r = new ResultadoEtapa { Latencia = lat, Jitter = jit, Secuencial = Resumen(sec) };
        double tSec = Mediana(sec);
        double t1 = Mediana(porP.OrderBy(k => k.Key).First().Value);
        int nuc = Environment.ProcessorCount;
        var kfs = new List<double>();
        foreach (var (p, v) in porP.OrderBy(k => k.Key))
        {
            double tp = Mediana(v), srel = t1 / tp;
            double? kf = p > 1 ? (1 / srel - 1.0 / p) / (1 - 1.0 / p) : null;
            if (kf is not null && p <= nuc) kfs.Add(kf.Value);
            r.Filas.Add(new FilaMetricas
            {
                P = p, T = Resumen(v), Sabs = Math.Round(tSec / tp, 3), Srel = Math.Round(srel, 3), E = Math.Round(srel / p, 3),
                EPorNucleo = Math.Round(tSec / tp / Math.Min(p, nuc), 3), KarpFlatt = kf is null ? null : Math.Round(kf.Value, 4)
            });
        }
        r.FraccionSerialAmdahl = kfs.Count > 0 ? Math.Round(kfs.Average(), 4) : null;
        return r;
    }
}

public static class Ficha
{
    public static FichaMaquina Obtener()
    {
        string cpu = "desconocido"; int fisicos = Environment.ProcessorCount; string mem = "";
        try
        {
            if (File.Exists("/proc/cpuinfo"))
            {
                var lineas = File.ReadAllLines("/proc/cpuinfo");
                cpu = lineas.FirstOrDefault(l => l.StartsWith("model name"))?.Split(':', 2)[1].Trim() ?? cpu;
                var cores = lineas.Where(l => l.StartsWith("physical id") || l.StartsWith("core id")).ToList();
                var pares = new HashSet<string>(); string pid = "0";
                foreach (var l in lineas)
                {
                    if (l.StartsWith("physical id")) pid = l.Split(':')[1].Trim();
                    if (l.StartsWith("core id")) pares.Add(pid + ":" + l.Split(':')[1].Trim());
                }
                if (pares.Count > 0) fisicos = pares.Count;
                var mt = File.ReadAllLines("/proc/meminfo").First(l => l.StartsWith("MemTotal")).Split(':')[1].Trim();
                mem = $"{double.Parse(mt.Split(' ')[0]) / 1024 / 1024:F1} GB";
            }
            else
            {
                cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? cpu;
                mem = $"{GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024 / 1024:F1} GB";
            }
        }
        catch { }
        return new FichaMaquina
        {
            Procesador = cpu, NucleosFisicos = fisicos, NucleosLogicos = Environment.ProcessorCount, Smt = Environment.ProcessorCount > fisicos,
            Memoria = mem, SistemaOperativo = RuntimeInformation.OSDescription, Runtime = RuntimeInformation.FrameworkDescription,
            Fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
        };
    }
}

/// <summary>Experimento de contención: conteo de ofertas por plataforma con 3 mecanismos + condición de carrera.</summary>
public static class Contencion
{
    static (string[] plats, int[] datos) Datos()
    {
        // Observaciones reales (plataforma de las que están en oferta), repetidas para tener trabajo medible.
        var lista = new List<string>();
        try
        {
            using var db = new Db(new Opciones().RutaDb);
            lista = db.Query("SELECT COALESCE(plataforma,'pc') FROM observacion_precio WHERE en_oferta=1", r => r.GetString(0));
            if (lista.Count < 50) lista.AddRange(db.Query("SELECT COALESCE(plataforma,'pc') FROM observacion_precio", r => r.GetString(0)));
        }
        catch { }
        if (lista.Count == 0) lista = new() { "pc", "switch", "ps5", "ps4", "xbox-series", "switch2" };
        var plats = lista.Distinct().OrderBy(x => x).ToArray();
        var idx = plats.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
        const int N = 4_000_000;
        var datos = new int[N];
        for (int i = 0; i < N; i++) datos[i] = idx[lista[i % lista.Count]];
        return (plats, datos);
    }

    public static object Ejecutar(int reps)
    {
        var (plats, datos) = Datos();
        int L = Environment.ProcessorCount;
        var ws = new[] { 1, 2, 4, L, 2 * L }.Distinct().OrderBy(x => x).ToArray();
        var esperado = new long[plats.Length];
        foreach (var d in datos) esperado[d]++;
        var tiempos = new Dictionary<string, Dictionary<int, Resumen>>();

        void Medir(string nombre, Func<int, long[]> f)
        {
            tiempos[nombre] = new();
            f(1); // calentamiento
            foreach (var p in ws)
            {
                var v = new List<double>();
                for (int r = 0; r < reps; r++)
                {
                    var sw = Stopwatch.StartNew(); var res = f(p); v.Add(sw.Elapsed.TotalMilliseconds);
                    if (!res.SequenceEqual(esperado)) throw new Exception($"{nombre} p={p} dio un conteo incorrecto");
                }
                tiempos[nombre][p] = Estad.Resumen(v);
                Console.WriteLine($"  [contención] {nombre,-22} p={p}: {tiempos[nombre][p].Mediana:F1} ms");
            }
        }

        // 1) Lock global: cada incremento toma el mismo candado → los hilos se turnan.
        Medir("lock_global", p =>
        {
            var c = new long[plats.Length]; var candado = new object();
            Parallel.For(0, datos.Length, new ParallelOptions { MaxDegreeOfParallelism = p }, i => { lock (candado) c[datos[i]]++; });
            return c;
        });
        // 2) Estado local por hilo (localInit / localFinally): se combina una vez por hilo al final.
        Medir("local_por_hilo", p =>
        {
            var c = new long[plats.Length]; var candado = new object();
            Parallel.For(0, datos.Length, new ParallelOptions { MaxDegreeOfParallelism = p },
                () => new long[plats.Length],
                (i, _, local) => { local[datos[i]]++; return local; },
                local => { lock (candado) for (int k = 0; k < c.Length; k++) c[k] += local[k]; });
            return c;
        });
        // 3) Colección concurrente: ConcurrentDictionary.AddOrUpdate (bloqueo fino interno + reintentos).
        Medir("concurrent_dictionary", p =>
        {
            var cd = new System.Collections.Concurrent.ConcurrentDictionary<int, long>();
            Parallel.For(0, datos.Length, new ParallelOptions { MaxDegreeOfParallelism = p }, i => cd.AddOrUpdate(datos[i], 1, (_, v) => v + 1));
            var c = new long[plats.Length]; foreach (var (k, v) in cd) c[k] = v; return c;
        });
        // Secuencial de referencia.
        var sec = new List<double>();
        for (int r = 0; r < reps; r++) { var sw = Stopwatch.StartNew(); var c = new long[plats.Length]; foreach (var d in datos) c[d]++; sec.Add(sw.Elapsed.TotalMilliseconds); }

        return new
        {
            Elementos = datos.Length, Plataformas = plats, Esperado = esperado, SecuencialMs = Estad.Resumen(sec),
            TiemposMs = tiempos.ToDictionary(k => k.Key, k => k.Value.ToDictionary(x => x.Key.ToString(), x => x.Value))
        };
    }

    /// <summary>Condición de carrera real: c[x]++ sin sincronización desde varios hilos pierde incrementos.</summary>
    public static object Carrera()
    {
        var (plats, datos) = Datos();
        var esperado = new long[plats.Length];
        foreach (var d in datos) esperado[d]++;
        var intentos = new List<object>();
        for (int r = 0; r < 5; r++)
        {
            var c = new long[plats.Length];
            Parallel.For(0, datos.Length, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, i => c[datos[i]]++);
            long perdidos = esperado.Sum() - c.Sum();
            intentos.Add(new { Intento = r + 1, Obtenido = c.Sum(), Perdidos = perdidos });
            Console.WriteLine($"  [carrera] intento {r + 1}: esperado {esperado.Sum():N0}, obtenido {c.Sum():N0}, perdidos {perdidos:N0}");
        }
        // Con Interlocked el mismo código no pierde nada.
        var ci = new long[plats.Length];
        Parallel.For(0, datos.Length, i => Interlocked.Increment(ref ci[datos[i]]));
        return new { Esperado = esperado.Sum(), Intentos = intentos, ConInterlocked = ci.Sum() };
    }
}
