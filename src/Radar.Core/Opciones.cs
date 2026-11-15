namespace Radar.Core;

public enum Modo { Seq, Conc, Par }

/// <summary>Configuración de una ejecución. Se carga de config/radar.json y se puede sobreescribir por línea de comandos.</summary>
public sealed class Opciones
{
    public Modo Modo { get; set; } = Modo.Par;
    public int WorkersIo { get; set; } = 8;
    public int WorkersCpu { get; set; } = Environment.ProcessorCount;
    public int MaxVariantesPorTienda { get; set; } = 3;
    public int ColaDescubrir { get; set; } = 64;
    public int ColaDescargar { get; set; } = 32;
    public int ColaPersistir { get; set; } = 128;
    public Dictionary<string, int> LimitePorDominio { get; set; } = new()
    {
        ["store.steampowered.com"] = 3, ["catalog.gog.com"] = 3, ["www.metacritic.com"] = 2,
        ["www.nintendo.com"] = 2, ["www.gamestop.com"] = 2, ["api.igdb.com"] = 1, ["id.twitch.tv"] = 1, ["howlongtobeat.com"] = 1
    };
    /// <summary>Espera mínima entre solicitudes a un dominio (solo en vivo).</summary>
    public Dictionary<string, int> IntervaloMinMs { get; set; } = new() { ["store.steampowered.com"] = 1500 };
    public int LimiteDominioReplay { get; set; } = 1024;
    public int TimeoutSeg { get; set; } = 15;
    public int MaxReintentos { get; set; } = 4;
    public int BackoffBaseMs { get; set; } = 500;
    public int CacheHoras { get; set; } = 12;
    public bool Replay { get; set; }
    public bool Capturar { get; set; }
    public int LatenciaMs { get; set; } = 100;
    public int JitterMs { get; set; } = 0;
    public int Semilla { get; set; } = 42;
    public int MinJuegos { get; set; } = 500;
    public int? LimiteJuegos { get; set; }
    public double Umbral { get; set; } = 0.88;
    public string RutaDatos { get; set; } = "data";
    public string RutaWeb { get; set; } = "web/data";
    public string UserAgent { get; set; } = "RadarOfertasTEC/1.0 (IC-3101 TEC; proyecto academico)";
    public bool Publicar { get; set; } = true;
    public bool Persistir { get; set; } = true;
    public bool Descubrir { get; set; }

    public string RutaDb => Path.Combine(RutaDatos, "radar.db");
    public string RutaReplay => Path.Combine(RutaDatos, "replay");
    public string RutaCache => Path.Combine(RutaDatos, "cache");
}
