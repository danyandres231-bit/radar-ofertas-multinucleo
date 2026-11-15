# ADR-010 · Mecanismos de concurrencia por etapa · 15/11/2026

**Contexto.** La descarga es E/S (espera red); el emparejamiento es CPU.

**Alternativas.**
- Usar `Parallel.ForEach` en todo: bloquea hilos del pool esperando la red (mala para E/S).
- Usar `Task.WhenAll` en todo: no reparte cálculo entre núcleos de forma controlada.
- **Mecanismo por tipo de etapa.**

**Decisión.**
- Colas: `Channel<T>` acotados (`BoundedChannelFullMode.Wait`).
- Descarga: `Parallel.ForEachAsync` con `MaxDegreeOfParallelism = workers` (nivel 1), `Task.WhenAll` por fuente (nivel 2) y por resultado (nivel 3, máx. 3).
- Emparejamiento: `Parallel.ForEach` con particionador por rangos y `MaxDegreeOfParallelism = workers`.
- Límite por sitio: `SemaphoreSlim` por dominio.
- Agregados compartidos: `ThreadLocal<T>` / `localInit-localFinally`.

**Consecuencias.** Cada etapa escala con lo que la limita (red o núcleos). Se compara además `Parallel.ForEach` vs PLINQ en el emparejamiento (extra de bonificación).

**Estado.** Aceptada.
