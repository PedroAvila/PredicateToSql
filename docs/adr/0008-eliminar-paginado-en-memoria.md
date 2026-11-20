# ADR 0008 — Se elimina el paginado en memoria

- **Estado:** Aceptado
- **Fecha:** 2026-11-19
- **Afecta a:** `DapperExtensions` (`QueryPagedAsync`), `PagingExtensions` (eliminado),
  [README.md](../../README.md), [PAN.PredicateToSql.csproj](../../PAN.PredicateToSql.csproj)
- **Reemplaza a:** [ADR 0006](0006-orden-y-paginado-en-memoria.md)

## Contexto

El [ADR 0006](0006-orden-y-paginado-en-memoria.md) agregó `OrderByProperty`,
`OrderByProperties`, `ToPagedResult` y, más tarde, `QueryPagedAsync` como atajo de
`QueryAsync` + `ToPagedResult`. El enfoque era explícitamente en memoria: se traían
**todas** las filas que cumplían el predicado y recién después se ordenaba y se
recortaba la página pedida.

En la práctica el paginado terminó resolviéndose con SQL propio
(`ORDER BY` + `OFFSET/FETCH` en SQL Server, `LIMIT/OFFSET` en MySQL), que es lo que
el motor hace bien y sin materializar la tabla entera. Mantener la variante en
memoria deja dos caminos para el mismo problema, invita a usar el equivocado por
comodidad y obliga a mantener código y tests que ya no aportan.

## Decisión

Se elimina por completo la API de paginado en memoria:

- `DapperExtensions.QueryPagedAsync<T>`
- `PagingExtensions` (archivo entero): `OrderByProperty`, `OrderByProperties`, los
  dos overloads de `ToPagedResult`, `Paginate` y el `record PagedResult<T>`
- `IsExternalInit.cs`, que existía solo para que compilara `PagedResult<T>` en
  `netstandard2.0`

El paginado queda a cargo del consumidor, con SQL propio contra el motor. La
librería conserva únicamente `UpdateAsync`, `ExistsAsync` y `QueryAsync`.

## Alternativas consideradas

**Marcar la API con `[Obsolete]` y eliminarla en una versión posterior.** Se
descartó: el objetivo es no arrastrar código muerto ni documentación que compite
con la solución correcta. Como el paquete se consume por NuGet, el aviso de
compilación sería la única señal durante un tiempo indefinido.

**Traducir el orden y el paginado a SQL dentro de la librería** (`ORDER BY` por
nombre de columna + `OFFSET/FETCH`/`LIMIT`). Se descartó para este cambio: exige
decidir dialecto por dialecto cómo parametrizar el offset, validar el nombre de
columna contra la entidad y definir un contrato de resultado, es decir, un rediseño
completo. Si vuelve a hacer falta, corresponde un ADR propio.

## Consecuencias

- **Cambio incompatible (2.0.0).** Quien use `QueryPagedAsync`, `OrderByProperty`,
  `OrderByProperties`, `ToPagedResult` o `PagedResult<T>` deja de compilar y debe
  migrar a SQL propio.
- Desaparecen 13 tests de `PagingExtensions` y 4 de `QueryPagedAsync` (dos casos
  por motor); la cobertura de `UpdateAsync`/`ExistsAsync`/`QueryAsync` no cambia.
- El README, el `CLAUDE.md` y la metadata del paquete dejan de mencionar el
  paginado en memoria.
- El [ADR 0006](0006-orden-y-paginado-en-memoria.md) se conserva como historial,
  marcado como reemplazado por este documento.
