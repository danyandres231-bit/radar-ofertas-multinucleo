# ADR-005 · Fuentes por tipo de dato y sustituciones · 15/11/2026

**Contexto.** Se revisaron robots.txt y acceso real (15/11/2026):

| Sitio | Resultado |
|---|---|
| Steam (búsqueda HTML) | Permitido, HTML con precios |
| GOG (búsqueda HTML) | Permitido, HTML con precios |
| Nintendo eShop | Búsqueda con JS; **sitemap público** con ~39 000 fichas; ficha con precio en HTML |
| GameStop | `/search` **prohibido** en robots.txt; **sitemap público** de productos; ficha con precio en HTML |
| PlayStation Store | Búsqueda solo con JavaScript (requiere navegador) |
| Xbox | `search?q` prohibido en robots.txt |
| Epic | 403 |
| Amazon / Walmart / Best Buy / Target | 503, página de bloqueo, timeout o contenido solo con JS |
| Metacritic | `/browse` y `/game/` permitidos; `/search` prohibido |
| How Long To Beat | `/api` (su buscador) **prohibido** en robots.txt |

**Alternativas.** (a) Usar navegador (Playwright) en PlayStation/Target; (b) usar los sitemaps públicos, que los sitios publican justamente para robots; (c) APIs oficiales.

**Decisión.**

| Tipo | Fuente | Método |
|---|---|---|
| Catálogo (etapa 0) | Metacritic `/browse/game/` paginado por plataforma | HTML |
| Tienda plataforma 1 | Steam | HTML |
| Tienda plataforma 2 | GOG | HTML |
| Tienda plataforma 3 | Nintendo eShop | sitemap + ficha HTML |
| Minorista | GameStop (sustituye a Amazon/Walmart/Best Buy/Target) | sitemap + ficha HTML |
| Calificación 1 | Metacritic (crítica, por plataforma) | HTML |
| Calificación 2 | Reseñas de usuarios de Steam | HTML de la ficha de Steam |
| Duración | IGDB `game_time_to_beats` (respaldo documentado, ADR-006) | API |
| Imagen | Metacritic (portada del catálogo); Steam como respaldo | HTML |

Fuentes por HTML: 6 (≥ 3 exigidas, incluidas 4 tiendas). Total de tiendas: 4.

**Consecuencias.** Los sitemaps dan decenas de miles de candidatos, lo que hace que el emparejamiento sea una etapa de CPU real y medible. PlayStation Store queda como quinta tienda opcional con Playwright si sobra tiempo. Juegos exclusivos de PlayStation/Xbox pueden quedar con una sola tienda (GameStop); esos no se publican como comparables y se listan en el panel.

**Estado.** Aceptada.
