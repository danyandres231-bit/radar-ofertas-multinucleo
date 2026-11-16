# ADR-006 · Duración: How Long To Beat y respaldo IGDB · 15/11/2026

**Contexto.** How Long To Beat (HLTB) es obligatoria, pero su robots.txt (revisado el 16/11/2026) dice que **todo uso de robots o scrapers está prohibido sin permiso escrito de Ziff Davis** (su dueño), y además prohíbe `/api`, que es lo que usa su buscador. Sin buscador no se obtiene el ID de cada juego. Las fichas `/game/<id>` sí están permitidas. El enunciado acepta IGDB "solo como respaldo documentado".

**Alternativas.**
- Usar el buscador de HLTB igual: viola robots.txt (prohibido por el enunciado, sección 10).
- Mapear a mano los IDs de HLTB de 500 juegos: poco realista en el tiempo disponible.
- **HLTB por ficha cuando el ID esté en `data/hltb_ids.csv` + IGDB para el resto.**

**Decisión.** Se usa HLTB solo leyendo fichas `/game/<id>` de los IDs conocidos (archivo editable, sin tocar código). Para el resto se usa IGDB (`game_time_to_beats`: *hastily* ≈ historia principal, *normally* ≈ historia + extras, *completely* ≈ completista), con credenciales de Twitch Developer guardadas como secreto de GitHub (`IGDB_CLIENT_ID`, `IGDB_CLIENT_SECRET`), nunca en el código. El sitio indica la fuente de cada duración.

**Resultado (16/11/2026).** IGDB dio duración para 528 de 955 juegos. Se descartan valores absurdos (por ejemplo, completista > 400 h o más de 6 veces la historia). Las tiendas (Steam, GOG, Nintendo, GameStop) y Metacritic no publican duración en sus páginas.

**Consecuencias.** Se respeta robots.txt y los términos de HLTB. Las duraciones de IGDB tienen menos cobertura que las de HLTB; los juegos sin dato muestran "sin dato".

**Estado.** Aceptada.
