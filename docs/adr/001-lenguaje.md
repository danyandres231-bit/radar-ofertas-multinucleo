# ADR-001 · Lenguaje de implementación · 15/11/2026

**Contexto.** El enunciado admite C# (.NET 8+) o Python 3.11+. El equipo quiere usar Visual Studio. El emparejamiento debe mostrar paralelismo real entre núcleos.

**Alternativas.**
- **C# .NET 8:** hilos reales sin GIL; `Parallel.ForEach`, `Channel<T>`, `SemaphoreSlim`, `ThreadLocal<T>` nativos; integración con Visual Studio. Desventaja: scraping algo más verboso que en Python.
- **Python 3.11:** librerías de scraping muy simples. Desventaja: el GIL obliga a usar procesos para el cálculo y complica las mediciones.
- **C++:** no permitido por el enunciado.

**Decisión.** C# con .NET 8, solución de Visual Studio `RadarOfertas.sln`.

**Consecuencias.** El paralelismo del emparejamiento usa hilos del mismo proceso (sin costo de serializar datos). Hay que medir el andamiaje (`T_par(1)` vs `T_sec`).

**Estado.** Aceptada.
