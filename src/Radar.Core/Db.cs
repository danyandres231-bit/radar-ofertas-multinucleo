using Microsoft.Data.Sqlite;

namespace Radar.Core;

/// <summary>Repositorio SQLite (ADR-002). Solo un hilo escribe (etapa 4).</summary>
public sealed class Db : IDisposable
{
    readonly SqliteConnection _cn;

    public Db(string ruta)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
        _cn = new SqliteConnection($"Data Source={ruta}");
        _cn.Open();
        Exec("PRAGMA journal_mode=DELETE; PRAGMA foreign_keys=ON;");
        Exec(Esquema);
    }

    public const string Esquema = """
    CREATE TABLE IF NOT EXISTS juego (
      id INTEGER PRIMARY KEY, slug TEXT UNIQUE NOT NULL, titulo TEXT NOT NULL, fecha_lanz TEXT, msrp_usd REAL,
      imagen_url TEXT, tipo TEXT DEFAULT 'juego', caso_dificil INTEGER DEFAULT 0, origen TEXT NOT NULL, metascore_catalogo INTEGER,
      activo INTEGER DEFAULT 1);
    CREATE TABLE IF NOT EXISTS titulo_alt (juego_id INTEGER REFERENCES juego(id) ON DELETE CASCADE, titulo TEXT, PRIMARY KEY(juego_id, titulo));
    CREATE TABLE IF NOT EXISTS plataforma (juego_id INTEGER REFERENCES juego(id) ON DELETE CASCADE, plataforma TEXT, PRIMARY KEY(juego_id, plataforma));
    CREATE TABLE IF NOT EXISTS tienda (id TEXT PRIMARY KEY, nombre TEXT, tipo TEXT);
    CREATE TABLE IF NOT EXISTS observacion_precio (
      id INTEGER PRIMARY KEY, juego_id INTEGER REFERENCES juego(id), tienda_id TEXT, ejecucion_id INTEGER,
      precio_actual REAL, precio_regular REAL, en_oferta INTEGER, criterio_oferta TEXT, formato TEXT, plataforma TEXT,
      url TEXT, titulo_tienda TEXT, similitud REAL, dudoso INTEGER, obtenido_en TEXT NOT NULL);
    CREATE INDEX IF NOT EXISTS ix_obs_juego ON observacion_precio(juego_id, tienda_id, obtenido_en);
    CREATE TABLE IF NOT EXISTS calificacion (juego_id INTEGER, fuente TEXT, plataforma TEXT, valor REAL, obtenido_en TEXT,
      PRIMARY KEY(juego_id, fuente, plataforma));
    CREATE TABLE IF NOT EXISTS duracion (juego_id INTEGER PRIMARY KEY, fuente TEXT, principal_h REAL, extras_h REAL, completista_h REAL, obtenido_en TEXT);
    CREATE TABLE IF NOT EXISTS ejecucion (id INTEGER PRIMARY KEY, inicio TEXT, fin TEXT, modo TEXT, workers_io INTEGER,
      workers_cpu INTEGER, replay INTEGER, tiempos_etapa_json TEXT);
    CREATE TABLE IF NOT EXISTS solicitud_log (ejecucion_id INTEGER, dominio TEXT, exitosas INTEGER, reintentadas INTEGER, fallidas INTEGER);
    CREATE TABLE IF NOT EXISTS fallo (ejecucion_id INTEGER, juego_id INTEGER, fuente TEXT, url TEXT, error TEXT, reintentado INTEGER);
    CREATE TABLE IF NOT EXISTS dudoso (ejecucion_id INTEGER, juego_id INTEGER, tienda_id TEXT, titulo_tienda TEXT, url TEXT, similitud REAL, motivo TEXT);
    CREATE TABLE IF NOT EXISTS fuente_estado (juego_id INTEGER, fuente TEXT, ultimo_ok TEXT, ultimo_fallo TEXT, PRIMARY KEY(juego_id, fuente));
    INSERT OR IGNORE INTO tienda VALUES ('steam','Steam','plataforma'),('gog','GOG','plataforma'),
      ('nintendo','Nintendo eShop','plataforma'),('gamestop','GameStop','minorista');
    """;

    public void Exec(string sql, params (string, object?)[] ps)
    {
        using var cmd = _cn.CreateCommand(); cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public T? Scalar<T>(string sql, params (string, object?)[] ps)
    {
        using var cmd = _cn.CreateCommand(); cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var r = cmd.ExecuteScalar();
        return r is null or DBNull ? default : (T)Convert.ChangeType(r, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] ps)
    {
        using var cmd = _cn.CreateCommand(); cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        using var rd = cmd.ExecuteReader();
        var l = new List<T>();
        while (rd.Read()) l.Add(map(rd));
        return l;
    }

    public SqliteTransaction Tx() => _cn.BeginTransaction();

    public int ContarJuegos() => Scalar<int>("SELECT COUNT(*) FROM juego WHERE activo=1");

    public void GuardarJuego(Juego j)
    {
        Exec("""
            INSERT INTO juego(slug,titulo,fecha_lanz,msrp_usd,imagen_url,tipo,caso_dificil,origen,metascore_catalogo)
            VALUES($s,$t,$f,$m,$i,$tp,$cd,$o,$ms)
            ON CONFLICT(slug) DO UPDATE SET titulo=excluded.titulo, fecha_lanz=COALESCE(excluded.fecha_lanz,fecha_lanz),
              imagen_url=COALESCE(excluded.imagen_url,imagen_url), caso_dificil=MAX(caso_dificil,excluded.caso_dificil),
              msrp_usd=COALESCE(excluded.msrp_usd,msrp_usd), tipo=excluded.tipo, activo=1,
              metascore_catalogo=COALESCE(excluded.metascore_catalogo,metascore_catalogo)
            """, ("$s", j.Slug), ("$t", j.Titulo), ("$f", j.FechaLanz), ("$m", j.Msrp), ("$i", j.ImagenUrl), ("$tp", j.Tipo),
            ("$cd", j.CasoDificil ? 1 : 0), ("$o", j.Origen), ("$ms", j.MetascoreCatalogo));
        j.Id = Scalar<long>("SELECT id FROM juego WHERE slug=$s", ("$s", j.Slug));
        foreach (var t in j.TitulosAlt) Exec("INSERT OR IGNORE INTO titulo_alt VALUES($j,$t)", ("$j", j.Id), ("$t", t));
        foreach (var p in j.Plataformas) Exec("INSERT OR IGNORE INTO plataforma VALUES($j,$p)", ("$j", j.Id), ("$p", p));
    }

    public List<Juego> CargarJuegos()
    {
        var juegos = Query("SELECT id,slug,titulo,fecha_lanz,msrp_usd,imagen_url,tipo,caso_dificil,origen,metascore_catalogo FROM juego WHERE activo=1 ORDER BY id",
            r => new Juego
            {
                Id = r.GetInt64(0), Slug = r.GetString(1), Titulo = r.GetString(2), FechaLanz = r.IsDBNull(3) ? null : r.GetString(3),
                Msrp = r.IsDBNull(4) ? null : r.GetDouble(4), ImagenUrl = r.IsDBNull(5) ? null : r.GetString(5), Tipo = r.GetString(6),
                CasoDificil = r.GetInt64(7) == 1, Origen = r.GetString(8), MetascoreCatalogo = r.IsDBNull(9) ? null : r.GetInt32(9)
            });
        var porId = juegos.ToDictionary(j => j.Id);
        foreach (var (id, t) in Query("SELECT juego_id,titulo FROM titulo_alt", r => (r.GetInt64(0), r.GetString(1))))
            if (porId.TryGetValue(id, out var j)) j.TitulosAlt.Add(t);
        foreach (var (id, p) in Query("SELECT juego_id,plataforma FROM plataforma", r => (r.GetInt64(0), r.GetString(1))))
            if (porId.TryGetValue(id, out var j)) j.Plataformas.Add(p);
        return juegos;
    }

    public void Dispose() => _cn.Dispose();
}
