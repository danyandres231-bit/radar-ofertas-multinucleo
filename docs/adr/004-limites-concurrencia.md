# ADR-004 · Workers, colas y límites por dominio · 15/11/2026

**Contexto.** Hay tres niveles de paralelismo (por juego, por fuente, por resultado). Anidados sin control generan miles de tareas y bloqueos de los sitios. La máquina de medición tiene 2 núcleos lógicos.

**Alternativas.**
- **Sin límites:** máxima velocidad teórica; en vivo provoca bloqueos (HTTP 429/403).
- **Un solo límite global:** fácil, pero un sitio lento frena a todos.
- **Workers por etapa + semáforo por dominio + colas acotadas.**

**Decisión.**

| Parámetro | Vivo | Replay (mediciones) |
|---|---|---|
| Workers de descarga (por juego) | 8 | barrido 1–64 |
| Fuentes en paralelo por juego | todas (`Task.WhenAll`) | igual |
| Resultados en paralelo por tienda | máx. 3 | igual |
| Conexiones por dominio (`SemaphoreSlim`) | 2 (Nintendo, GameStop, Metacritic), 3 (Steam, GOG), 1 (IGDB, 4 req/s) | 64 (sin servidor real) |
| Workers de emparejamiento | `ProcessorCount` | barrido 1, 2, 4 (= 2 × lógicos) |
| Cola Descubrir → Descargar | 64 | 64 |
| Cola Descargar → Parsear → Emparejar | 32 | 32 |
| Cola Emparejar → Persistir | 128 | 128 |

**Consecuencias.** La colas acotadas aplican contrapresión: si el emparejamiento se atrasa, la descarga espera. En vivo el techo de la descarga lo fijan los semáforos por dominio, no los workers; eso se mide como experimento aparte.

**Estado.** Aceptada.
