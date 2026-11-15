# Guía para la defensa oral (20 min)

## 1. Demostración en vivo (5 min)

1. Abrir el sitio público (URL de Vercel), filtrar por plataforma y ordenar por descuento. Entrar a un juego y mostrar la lista de precios y el historial.
2. Abrir **Métricas**: tiempos por etapa, solicitudes por sitio, dudosos y curvas de speedup.
3. Ejecutar el pipeline en replay desde la raíz del repo:

```bash
dotnet run --project src/Radar.Pipeline -c Release -- ejecutar --modo par --io 16 --cpu 2 --replay --latencia 50 --sin-persistir
```

> El modo replay necesita la carpeta `data/replay` (respuestas capturadas). Si se clona el repo en otra máquina, primero hay que copiarla (ver README).

## 2. Predicción antes de ejecutar (5 min)

El profesor elige una configuración (workers o latencia). Se predice con este modelo sencillo y luego se ejecuta.

**Descarga (E/S):** cada solicitud espera la latencia `L`; con `p` workers se solapan `p` esperas.

```
T_descarga(p) ≈ N_solicitudes × L / p   +   costo fijo (parseo y planificación)
```

- `N_solicitudes` de la etapa 1 ≈ 3 por juego × 955 juegos = **2 865**.
- Mientras `p` sea mucho menor que `N`, duplicar `p` divide el tiempo a la mitad. Se aplana cuando `N × L / p` se acerca al costo fijo (parseo en CPU, unos 1–2 s en total).
- Ejemplo: `L = 50 ms`, `p = 24` → 2 865 × 0,05 / 24 ≈ **6 s** (+ ~1 s fijo).

**Emparejamiento (CPU):** escala con los núcleos, no con los workers.

```
T_emp(p) ≈ T_emp(1) / min(p, núcleos)   × (1 + pequeño costo de reparto)
```

- En la máquina de medición (2 núcleos): `T_emp(1)` ≈ 64 s → con 2 workers ≈ 33–35 s; con 3 o más workers **no baja** (hay solo 2 núcleos).

**Pipeline completo:** las etapas corren a la vez, así que el total lo marca la etapa más lenta (cuello de botella), como en la segmentación del procesador.

```
T_pipeline ≈ max(T_descarga, T_emp, T_fichas) + llenado y vaciado de las colas
```

- Con latencia baja manda el emparejamiento (CPU). Con latencia alta (≥ 400 ms) o pocos workers de E/S manda la descarga.

## 3. Preguntas típicas y respuesta corta

| Pregunta | Respuesta |
|---|---|
| ¿Qué pasa si se quita el `SemaphoreSlim` por dominio? | En vivo, cientos de solicitudes simultáneas a Steam → HTTP 429 (bloqueo). En replay no cambia el resultado, solo la presión sobre el sitio. |
| ¿Por qué la cola es acotada? | Contrapresión: si el emparejamiento se atrasa, la descarga espera y no se llena la memoria con fichas pendientes. |
| ¿Por qué la descarga mejora con más workers que núcleos? | Porque los workers pasan casi todo el tiempo **esperando la red**, sin usar CPU. Mientras uno espera, otro avanza (concurrencia). |
| ¿Por qué el emparejamiento no mejora pasando de 2 workers? | Es cálculo puro: solo hay 2 núcleos. Más hilos se turnan en los mismos núcleos y agregan cambios de contexto. |
| ¿De dónde sale la fracción serial? | Normalizar los 47 000 candidatos (una vez), repartir los juegos, la persistencia en un solo hilo y la etapa 0. |
| ¿Por qué el lock global es lento? | Todos los hilos se pelean por el mismo candado en cada incremento: el trabajo se vuelve secuencial y además paga el costo del candado. |
| ¿Por qué el estado local por hilo es el más rápido? | Cada hilo cuenta en su propio arreglo sin sincronizar y solo se combina una vez al final. |
| ¿Por qué se pierden incrementos sin sincronización? | `c[x]++` son 3 pasos (leer, sumar, escribir). Dos hilos leen el mismo valor viejo y uno de los dos incrementos se pierde. |
| ¿Qué es el GIL y por qué no aplica? | Es el candado global de Python. En C# no existe: los hilos del `ThreadPool` corren de verdad en paralelo. |
| ¿Por qué replay y no en vivo? | En vivo la red, las cachés y los bloqueos cambian entre corridas; no se puede repetir la medición. |
