"""Genera los gráficos y tablas del informe a partir de data/bench/resultados.json.
Uso (desde la raíz del repo): python scripts/graficos.py"""
import json, os
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

R = json.load(open("data/bench/resultados.json", encoding="utf-8"))
os.makedirs("docs/img", exist_ok=True)
S1, S2, S3, NEUTRO, TINTA, GRID = "#2a78d6", "#eb6834", "#1baf7a", "#6b7a90", "#1f2a3c", "#e3e8ef"
plt.rcParams.update({"font.size": 10, "axes.edgecolor": GRID, "axes.labelcolor": TINTA, "xtick.color": TINTA, "ytick.color": TINTA,
                     "axes.spines.top": False, "axes.spines.right": False, "axes.grid": True, "grid.color": GRID, "grid.linewidth": .8})

def speedup(e, nombre, titulo):
    ps = [f["p"] for f in e["filas"]]
    fig, ax = plt.subplots(figsize=(6.4, 4))
    ax.plot(ps, ps, "--", color=NEUTRO, lw=1.3, label="Ideal (S = p)")
    ax.plot(ps, [f["sabs"] for f in e["filas"]], "-o", color=S1, lw=2, ms=6, label="S_abs (vs. secuencial)")
    ax.plot(ps, [f["srel"] for f in e["filas"]], "-D", color=S2, lw=2, ms=5, label="S_rel (vs. p = 1)")
    ax.set_xscale("log", base=2); ax.set_yscale("log", base=2)
    ax.set_xticks(ps); ax.set_xticklabels(ps)
    ax.set_xlabel("workers (p)"); ax.set_ylabel("speedup (escala log)"); ax.set_title(titulo, loc="left", fontsize=11, color=TINTA)
    ax.legend(frameon=False); fig.tight_layout(); fig.savefig(f"docs/img/{nombre}.png", dpi=160); plt.close(fig)

def eficiencia(e, nombre, titulo):
    ps = [f["p"] for f in e["filas"]]
    fig, ax = plt.subplots(figsize=(6.4, 3.4))
    ax.bar([str(p) for p in ps], [f["e"] for f in e["filas"]], color=S1, width=.55)
    ax.axhline(1, color=NEUTRO, ls="--", lw=1.2)
    ax.set_xlabel("workers (p)"); ax.set_ylabel("eficiencia E(p)"); ax.set_title(titulo, loc="left", fontsize=11, color=TINTA)
    fig.tight_layout(); fig.savefig(f"docs/img/{nombre}.png", dpi=160); plt.close(fig)

def tabla(e):
    s = "| Workers (p) | Mediana (s) | Mín – máx (s) | S_abs(p) | S_rel(p) | E(p) | e (Karp–Flatt) |\n|---|---|---|---|---|---|---|\n"
    q = e["secuencial"]
    s += f"| Secuencial | {q['mediana']:.3f} | {q['min']:.3f} – {q['max']:.3f} | 1,00 | — | — | — |\n"
    for f in e["filas"]:
        kf = "—" if f["karpFlatt"] is None else f"{f['karpFlatt']:.4f}"
        s += f"| {f['p']} | {f['t']['mediana']:.3f} | {f['t']['min']:.3f} – {f['t']['max']:.3f} | {f['sabs']:.2f} | {f['srel']:.2f} | {f['e']:.3f} | {kf} |\n"
    return s.replace(".", ",")

out = []
for k, e in R["etapas"].items():
    titulo = "Descarga (E/S)" if k.startswith("descarga") else "Emparejamiento (CPU)"
    speedup(e, f"speedup_{k}", f"{titulo}: speedup vs. workers")
    eficiencia(e, f"eficiencia_{k}", f"{titulo}: eficiencia por worker")
    out.append(f"### {titulo} · `{k}`\n\n{tabla(e)}")

# Pipeline: tres modos por latencia
pip = R.get("pipeline", {})
if pip:
    fig, ax = plt.subplots(figsize=(7, 3.8))
    claves = sorted(pip, key=lambda k: (pip[k]["latencia"], pip[k]["jitter"]))
    modos = ["seq", "conc", "par"]; ancho = .26
    colores = {"seq": NEUTRO, "conc": S1, "par": S2}
    nombres = {"seq": "Secuencial", "conc": "Concurrente (E/S)", "par": "Paralelo completo"}
    for i, m in enumerate(modos):
        xs = [j + (i - 1) * ancho for j, k in enumerate(claves) if m in pip[k]["modos"]]
        ys = [pip[k]["modos"][m]["mediana"] for k in claves if m in pip[k]["modos"]]
        ax.bar(xs, ys, ancho * .92, color=colores[m], label=nombres[m])
    ax.set_xticks(range(len(claves)))
    ax.set_xticklabels([f"{pip[k]['latencia']} ms" + (f" ± {pip[k]['jitter']}" if pip[k]['jitter'] else "") for k in claves])
    ax.set_xlabel("latencia simulada"); ax.set_ylabel("segundos (mediana)"); ax.set_title("Pipeline completo por modo y latencia", loc="left", fontsize=11, color=TINTA)
    ax.legend(frameon=False); fig.tight_layout(); fig.savefig("docs/img/pipeline_modos.png", dpi=160); plt.close(fig)
    s = "| Latencia | Modo | Mediana (s) | Mín – máx (s) | Desv (s) | S_abs | Descarga (s) | Emparejar (s) | Fichas 1b (s) |\n|---|---|---|---|---|---|---|---|---|\n"
    for k in claves:
        p = pip[k]; tsec = p["modos"].get("seq", {}).get("mediana")
        for m in modos:
            if m not in p["modos"]: continue
            v = p["modos"][m]; et = p["etapas"][m]
            sabs = f"{tsec / v['mediana']:.2f}" if tsec else "—"
            lat = f"{p['latencia']} ms" + (f" ± {p['jitter']}" if p["jitter"] else "")
            s += f"| {lat} | {nombres[m]} | {v['mediana']:.2f} | {v['min']:.2f} – {v['max']:.2f} | {v['desv']:.2f} | {sabs} | {et.get('1_descargar', 0):.2f} | {et.get('3_emparejar', 0):.2f} | {et.get('1b_fichas', 0):.2f} |\n"
    out.append("### Pipeline completo\n\n" + s.replace(".", ","))

c = R.get("contencion")
if c:
    ws = list(c["tiemposMs"]["lock_global"].keys())
    s = "| Mecanismo | " + " | ".join(f"p = {w} (ms)" for w in ws) + " |\n|---|" + "---|" * len(ws) + "\n"
    nom = {"lock_global": "Lock global", "local_por_hilo": "Estado local por hilo + combinar", "concurrent_dictionary": "ConcurrentDictionary"}
    for k, v in c["tiemposMs"].items():
        s += f"| {nom[k]} | " + " | ".join(f"{v[w]['mediana']:.1f}" for w in ws) + " |\n"
    s += f"| Secuencial (referencia) | {c['secuencialMs']['mediana']:.1f} | | |\n"
    out.append(f"### Contención ({c['elementos']:,} elementos)\n\n".replace(",", ".") + s.replace(".", ","))
open("docs/tablas-resultados.md", "w", encoding="utf-8").write("# Tablas de resultados (generadas por scripts/graficos.py)\n\n" + "\n\n".join(out) + "\n")
print("ok:", os.listdir("docs/img"))
