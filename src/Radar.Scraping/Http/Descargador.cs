using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Radar.Core;

namespace Radar.Scraping.Http;

public sealed class EstadisticaDominio
{
    public int Exitosas, Reintentadas, Fallidas, DesdeCache;
    public long MsTotales;
}

public sealed class FalloDescarga : Exception
{
    public string Url { get; }
    public FalloDescarga(string url, string msg, Exception? inner = null) : base(msg, inner) { Url = url; }
}

/// <summary>
/// Cliente HTTP robusto: timeout por solicitud, reintentos con backoff exponencial + jitter,
/// SemaphoreSlim por dominio, caché en disco y captura para replay.
/// </summary>
public sealed class Descargador : IDisposable
{
    readonly Opciones _op;
    readonly HttpClient _http;
    readonly AlmacenReplay? _captura;
    readonly ConcurrentDictionary<string, SemaphoreSlim> _semaforos = new();
    public ConcurrentDictionary<string, EstadisticaDominio> Estadisticas { get; } = new();
    readonly Random _rnd;
    readonly object _rndLock = new();

    public Descargador(Opciones op)
    {
        _op = op;
        _rnd = new Random(op.Semilla);
        HttpMessageHandler handler = op.Replay
            ? new ReplayHandler(new AlmacenReplay(op.RutaReplay), op.LatenciaMs, op.JitterMs, op.Semilla)
            : new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                MaxConnectionsPerServer = 16,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AllowAutoRedirect = true
            };
        if (op.Capturar && !op.Replay) _captura = new AlmacenReplay(op.RutaReplay);
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(op.UserAgent);
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    SemaphoreSlim Semaforo(string dominio) => _semaforos.GetOrAdd(dominio, d =>
    {
        int lim = _op.Replay ? _op.LimiteDominioReplay : (_op.LimitePorDominio.TryGetValue(d, out var l) ? l : 2);
        return new SemaphoreSlim(lim, lim);
    });

    int Jitter(int max) { lock (_rndLock) return _rnd.Next(0, max + 1); }

    /// <param name="vacioEn">Códigos HTTP que el sitio usa para "sin resultados" (GOG responde 400): se devuelve "" sin contarlo como fallo.</param>
    public Task<string> GetAsync(string url, CancellationToken ct = default, IDictionary<string, string>? headers = null, int[]? vacioEn = null) =>
        EnviarAsync(HttpMethod.Get, url, null, headers, ct, false, vacioEn);

    public Task<string> PostAsync(string url, string cuerpo, IDictionary<string, string>? headers = null, CancellationToken ct = default, bool privado = false) =>
        EnviarAsync(HttpMethod.Post, url, cuerpo, headers, ct, privado);

    /// <param name="privado">Solicitudes con secretos (token de IGDB): no se guardan en caché ni en replay y la URL no aparece en errores.</param>
    async Task<string> EnviarAsync(HttpMethod metodo, string url, string? cuerpo, IDictionary<string, string>? headers, CancellationToken ct, bool privado = false, int[]? vacioEn = null)
    {
        var urlLog = privado ? new Uri(url).GetLeftPart(UriPartial.Path) : url;
        var dominio = new Uri(url).Host;
        var est = Estadisticas.GetOrAdd(dominio, _ => new EstadisticaDominio());
        var clave = AlmacenReplay.Clave(metodo.Method, url, cuerpo);

        // Caché en disco (solo en vivo): evita repetir solicitudes innecesarias.
        if (!privado && !_op.Replay && _op.CacheHoras > 0 && LeerCache(clave) is { } cacheado)
        {
            Interlocked.Increment(ref est.DesdeCache);
            _captura?.Guardar(clave, 200, cacheado);
            return cacheado;
        }

        var sem = Semaforo(dominio);
        Exception? ultimo = null;
        for (int intento = 0; intento <= _op.MaxReintentos; intento++)
        {
            if (intento > 0)
            {
                Interlocked.Increment(ref est.Reintentadas);
                int espera = _op.BackoffBaseMs * (1 << (intento - 1)) + Jitter(250);
                await Task.Delay(espera, ct);
            }
            await sem.WaitAsync(ct);
            if (!_op.Replay) await EsperarTurnoAsync(dominio, ct);
            var sw = Stopwatch.StartNew();
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(_op.TimeoutSeg));
                using var req = new HttpRequestMessage(metodo, url);
                if (cuerpo != null) req.Content = new StringContent(cuerpo, Encoding.UTF8, "application/json");
                if (headers != null) foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token);
                var texto = await resp.Content.ReadAsStringAsync(cts.Token);
                int code = (int)resp.StatusCode;
                if (code == 429 || code >= 500) { ultimo = new FalloDescarga(urlLog, $"HTTP {code}"); continue; }
                if (vacioEn != null && vacioEn.Contains(code))
                {
                    Interlocked.Increment(ref est.Exitosas);
                    if (!privado) { if (!_op.Replay && _op.CacheHoras > 0) GuardarCache(clave, ""); _captura?.Guardar(clave, code, ""); }
                    return "";
                }
                if (code >= 400) { Interlocked.Increment(ref est.Fallidas); throw new FalloDescarga(urlLog, $"HTTP {code}"); }
                Interlocked.Increment(ref est.Exitosas);
                if (!privado)
                {
                    if (!_op.Replay && _op.CacheHoras > 0) GuardarCache(clave, texto);
                    _captura?.Guardar(clave, code, texto);
                }
                return texto;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { ultimo = new FalloDescarga(urlLog, "timeout"); }
            catch (HttpRequestException e) { ultimo = new FalloDescarga(urlLog, e.Message, e); }
            finally
            {
                Interlocked.Add(ref est.MsTotales, sw.ElapsedMilliseconds);
                sem.Release();
            }
        }
        Interlocked.Increment(ref est.Fallidas);
        throw ultimo as FalloDescarga ?? new FalloDescarga(urlLog, ultimo?.Message ?? "error", ultimo);
    }

    // Intervalo mínimo entre solicitudes al mismo dominio (Steam limita la tasa con HTTP 429).
    readonly ConcurrentDictionary<string, long> _proximo = new();
    async Task EsperarTurnoAsync(string dominio, CancellationToken ct)
    {
        if (!_op.IntervaloMinMs.TryGetValue(dominio, out var ms) || ms <= 0) return;
        long ahora = Environment.TickCount64, turno;
        while (true)
        {
            long prev = _proximo.GetOrAdd(dominio, 0);
            turno = Math.Max(ahora, prev);
            if (_proximo.TryUpdate(dominio, turno + ms, prev)) break;
            ahora = Environment.TickCount64;
        }
        var espera = turno - Environment.TickCount64;
        if (espera > 0) await Task.Delay((int)espera, ct);
    }

    string RutaCache(string clave) => Path.Combine(_op.RutaCache, clave[..2], clave + ".txt");

    string? LeerCache(string clave)
    {
        var r = RutaCache(clave);
        if (!File.Exists(r) || DateTime.UtcNow - File.GetLastWriteTimeUtc(r) > TimeSpan.FromHours(_op.CacheHoras)) return null;
        return File.ReadAllText(r);
    }

    void GuardarCache(string clave, string texto)
    {
        var r = RutaCache(clave);
        Directory.CreateDirectory(Path.GetDirectoryName(r)!);
        var tmp = r + "." + Environment.CurrentManagedThreadId + ".tmp";
        File.WriteAllText(tmp, texto);
        File.Move(tmp, r, true);
    }

    public void Dispose() => _http.Dispose();
}
