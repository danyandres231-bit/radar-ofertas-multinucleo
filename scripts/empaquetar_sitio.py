"""Empaqueta los JSON del sitio (web/data) en un solo archivo comprimido web/data/sitio.bin (JSON compacto + gzip).
El navegador lo descomprime con DecompressionStream. Así el sitio completo pesa ~60 KB en lugar de ~2 MB.
Uso (desde la raíz del repo): python scripts/empaquetar_sitio.py"""
import json, glob, gzip, os

W = "web/data"
J = json.load(open(f"{W}/juegos.json", encoding="utf-8"))
M = json.load(open(f"{W}/metricas.json", encoding="utf-8"))
B = json.load(open(f"{W}/bench.json", encoding="utf-8")) if os.path.exists(f"{W}/bench.json") else None
TI = ["steam", "gog", "nintendo", "gamestop"]
PRE = ["https://store.steampowered.com/", "https://www.gog.com/en/game/", "https://www.nintendo.com/us/store/products/", "https://www.gamestop.com/"]
plats, fechas = [], []
def idx(lista, v):
    if v not in lista: lista.append(v)
    return lista.index(v)

juegos = []
for c in J["juegos"]:
    d = json.load(open(f"{W}/juego/{c['id']}.json", encoding="utf-8"))
    img = (c.get("imagen") or "").replace("https://www.metacritic.com/a/img/catalog/provider/", "~")
    precios = []
    for p in d["precios"]:
        t = TI.index(p["tienda"]); u = p["url"]
        if u.startswith(PRE[t]): u = u[len(PRE[t]):]
        precios.append([t, p["actual"], p.get("regular"), 1 if p["formato"] == "fisico" else 0, idx(plats, p.get("plataforma") or ""),
                        u, 1 if p.get("dudoso") else 0, 1 if p.get("enOferta") else 0, idx(fechas, p["obtenidoEn"][:16] + "Z"), 1 if p.get("desactualizado") else 0])
    cal = {x["f"]: x["v"] for x in d.get("calificaciones", [])}
    du = d.get("duracion")
    hist = [[TI.index(h["tienda"]), [[x["fecha"], x["precio"]] for x in h["puntos"]]] for h in d.get("historial", [])]
    juegos.append([c["id"], c["titulo"], img, c["tipo"], c.get("fechaLanz") or "", d.get("msrp"), d["minimoHistorico"],
                   cal.get("metacritic"), cal.get("steam_usuarios"), precios, hist,
                   [du["fuente"], du.get("principal"), du.get("extras"), du.get("completista"), du.get("obtenidoEn")] if du else None,
                   [[x["fuente"], x.get("desde")] for x in d.get("desactualizado", [])]])

if B:
    for e in B.get("etapas", {}).values():
        e["secuencial"].pop("muestras", None)
        for f in e["filas"]: f["t"].pop("muestras", None)
    for p in B.get("pipeline", {}).values():
        for v in p["modos"].values(): v.pop("muestras", None)
    for v in (B.get("plinq") or {}).values(): v.pop("muestras", None)
    B.pop("validacion", None)
for k in ("ejecuciones",): M.pop(k, None)
M["noPublicados"] = [[x["titulo"], x["motivo"]] for x in M["noPublicados"]]
M["dudosos"] = [[x["titulo"], x["tienda"], x["tituloTienda"], x["similitud"], x.get("motivo")] for x in M["dudosos"][:80]]
M["fallos"] = [[x["titulo"], x["fuente"], x["error"]] for x in M["fallos"]]

datos = {"a": J["actualizado"], "pl": plats, "fe": fechas, "j": juegos, "m": M, "b": B}
crudo = json.dumps(datos, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
open(f"{W}/sitio.bin", "wb").write(gzip.compress(crudo, 9, mtime=0))
print(f"sitio.bin: {len(crudo):,} bytes → {os.path.getsize(f'{W}/sitio.bin'):,} bytes gzip")

# Se parte en 3 trozos (el navegador los vuelve a unir antes de descomprimir).
datos_gz = open(f"{W}/sitio.bin", "rb").read()
n = (len(datos_gz) + 2) // 3
for i in range(3):
    open(f"{W}/sitio.{i}.bin", "wb").write(datos_gz[i * n:(i + 1) * n])
os.remove(f"{W}/sitio.bin")
print("partes:", [os.path.getsize(f"{W}/sitio.{i}.bin") for i in range(3)])
