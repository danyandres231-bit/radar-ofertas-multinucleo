# Bitácora de IA

Herramienta: Claude (Anthropic). Uso permitido por el profesor, incluido el diseño del Hito 1 por falta de tiempo.

| Fecha | Qué se pidió | Qué se obtuvo | Qué se aceptó / modificó y por qué |
|---|---|---|---|
| 15/11/2026 | Planificación del proyecto por capas e hitos | Plan de 10 capas y calendario | Se aceptó; se cambió C++ por C# porque el enunciado solo admite C# o Python |
| 15/11/2026 | Revisión de fuentes y robots.txt | Tabla de sitios permitidos y bloqueados | Se aceptó; se descartaron buscadores prohibidos por robots.txt |
| 15/11/2026 | Documentos del Hito 1 (acta, ADRs, diseño, predicciones) | Borradores completos | Se aceptaron. **Nota:** las predicciones las hizo la IA con permiso del profesor, no el equipo. |

| 15/11/2026 | Código C# completo (pipeline, scrapers, replay, banco de pruebas, sitio) | Solución de 4 proyectos | Se aceptó tras probarlo; se corrigieron los errores de abajo |
| 15/11/2026 | Primera captura en vivo | Steam respondió 862 veces HTTP 429 | Se agregó intervalo mínimo por dominio (Steam 1,5 s) |
| 15/11/2026 | Modo replay | 5 372 fallos "no capturado" | **Error de la IA:** la clave del replay se calculaba con `RequestUri.ToString()`, que quita los `%20`; al capturar se usaba la URL original. Se corrigió con `OriginalString`. |
| 15/11/2026 | Banco de pruebas del emparejamiento | Se detenía con "el resultado paralelo difiere" | **Error de la IA:** comparaba el hash de la corrida de calentamiento antes de tener el secuencial. Se corrigió. |
| 15/11/2026 | Validación con 200 pares | Pares etiquetados por la IA | **El equipo debe revisar** `data/validacion_pares.csv` (columna `correcto`) antes de la entrega. |

## Entrada obligatoria: error real de concurrencia producido por la IA

**Pendiente — la debe completar el equipo.** Hasta ahora no se detectó en el código un defecto de concurrencia real producido por la IA: los errores encontrados (arriba) no fueron de concurrencia, y la condición de carrera del experimento está hecha a propósito. No se inventa un caso. Una forma honesta de completar esta entrada es pedirle a la IA, sin darle pistas, que agregue un contador compartido (por ejemplo, solicitudes por sitio) y revisar si lo hace sin `Interlocked` o sin `lock`. Si el código falla, se documenta: cómo se detectó (conteo distinto al esperado), la causa y la corrección.
