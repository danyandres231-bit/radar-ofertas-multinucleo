using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Radar.Scraping.Http;

/// <summary>Almacén de respuestas capturadas: data/replay/&lt;sha1&gt;.gz (ADR-009).</summary>
public sealed class AlmacenReplay
{
    readonly string _dir;
    public AlmacenReplay(string dir) { _dir = dir; Directory.CreateDirectory(dir); }

    public static string Clave(string metodo, string url, string? cuerpo) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(metodo + " " + url + "\n" + (cuerpo ?? "")))).ToLowerInvariant();

    string Ruta(string clave) => Path.Combine(_dir, clave[..2], clave + ".gz");

    public void Guardar(string clave, int status, string contenido)
    {
        var ruta = Ruta(clave);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        var tmp = ruta + "." + Environment.CurrentManagedThreadId + ".tmp";
        using (var fs = File.Create(tmp))
        using (var gz = new GZipStream(fs, CompressionLevel.SmallestSize))
        using (var w = new StreamWriter(gz, Encoding.UTF8))
        { w.Write(status); w.Write('\n'); w.Write(contenido); }
        File.Move(tmp, ruta, overwrite: true);
    }

    public (int status, string contenido)? Leer(string clave)
    {
        var ruta = Ruta(clave);
        if (!File.Exists(ruta)) return null;
        using var fs = File.OpenRead(ruta);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var r = new StreamReader(gz, Encoding.UTF8);
        int status = int.Parse(r.ReadLine()!);
        return (status, r.ReadToEnd());
    }

    public int Cantidad() => Directory.Exists(_dir) ? Directory.EnumerateFiles(_dir, "*.gz", SearchOption.AllDirectories).Count() : 0;
}

/// <summary>Handler que sirve respuestas capturadas con latencia simulada. El resto del código no cambia.</summary>
public sealed class ReplayHandler : HttpMessageHandler
{
    readonly AlmacenReplay _almacen;
    readonly int _latenciaMs, _jitterMs, _semilla;

    public ReplayHandler(AlmacenReplay almacen, int latenciaMs, int jitterMs, int semilla)
    { _almacen = almacen; _latenciaMs = latenciaMs; _jitterMs = jitterMs; _semilla = semilla; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        string? cuerpo = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
        var clave = AlmacenReplay.Clave(req.Method.Method, req.RequestUri!.OriginalString, cuerpo);
        // Jitter determinista por URL y semilla: misma configuración → misma latencia → medición reproducible.
        int jitter = 0;
        if (_jitterMs > 0)
        {
            int h = BitConverter.ToInt32(SHA1.HashData(Encoding.UTF8.GetBytes(_semilla + clave)), 0) & int.MaxValue;
            jitter = h % (2 * _jitterMs + 1) - _jitterMs;
        }
        int espera = Math.Max(0, _latenciaMs + jitter);
        if (espera > 0) await Task.Delay(espera, ct);
        var r = _almacen.Leer(clave);
        if (r is null) return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("replay: no capturado"), RequestMessage = req };
        return new HttpResponseMessage((HttpStatusCode)r.Value.status) { Content = new StringContent(r.Value.contenido, Encoding.UTF8), RequestMessage = req };
    }
}
