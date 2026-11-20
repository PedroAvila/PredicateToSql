# ADR 0009 — Renombre a `ExistsAsync`, `EXISTS`, cancelación y evaluación del lado derecho

- **Estado:** Aceptado
- **Fecha:** 2026-11-19
- **Afecta a:** `DapperExtensions`, `SqlExpressionVisitor`, [README.md](../../README.md),
  [PAN.PredicateToSql.csproj](../../PAN.PredicateToSql.csproj)
- **Relacionado:** [ADR 0005](0005-alcance-de-operadores-y-agrupamiento.md)

## Contexto

Antes de publicar 2.0.0 quedaron cuatro deudas visibles en la API y en el SQL generado:

1. El método de existencia se llamaba `ExistAsync` en singular, mientras que el
   resto de la API —y la convención de Dapper— usa `ExistsAsync`. Era una errata
   consolidada que obligaba a los consumidores a recordar la excepción.
2. `ExistsAsync` emitía `SELECT COUNT(*)`, lo que obliga al motor a contar todas las
   coincidencias cuando solo hace falta saber si hay al menos una.
3. Ningún método aceptaba un `CancellationToken`, así que una consulta no podía
   cancelarse y las llamadas no usaban `ConfigureAwait(false)`, algo relevante en una
   librería que no necesita volver al contexto de sincronización.
4. El lado derecho de una comparación solo admitía constantes y variables capturadas.
   Una expresión como `x.Fecha > DateTime.Now.AddDays(-1)` fallaba con una excepción
   opaca, aunque el árbol podía evaluarse como cualquier expresión C#.

Además, `Contains` no escapaba `%` ni `_`: un valor con esos caracteres se
interpretaba como patrón en vez de como texto literal.

## Decisión

- El método pasa a llamarse **`ExistsAsync`**. Es un cambio incompatible, aceptable
  en una versión mayor.
- La consulta de existencia pasa a
  `SELECT CASE WHEN EXISTS (SELECT 1 FROM ... WHERE ...) THEN 1 ELSE 0 END`, válida
  en SQL Server y MySQL sin depender de `LIMIT`.
- Los tres métodos aceptan `CancellationToken cancellationToken = default` y lo
  propagan a Dapper mediante `CommandDefinition`; todas las esperas usan
  `ConfigureAwait(false)`.
- El lado derecho se evalúa con `Expression.Lambda<Func<object?>>(...).Compile()`.
  Se sigue rechazando con `NotSupportedException` cuando la expresión referencia el
  parámetro de la entidad (comparación columna-columna), porque eso no es un valor.
- `Contains` escapa `!`, `%` y `_`, envuelve el valor en `%...%` y agrega
  `ESCAPE '!'`. Se eligió `!` en lugar de `\` porque MySQL interpreta la barra
  invertida de forma distinta según el modo SQL.

## Alternativas consideradas

**Mantener `ExistAsync` y agregar `ExistsAsync` como alias.** Se descartó: dos
nombres para lo mismo obliga a mantener dos rutas y no resuelve la errata, solo la
esconde.

**Usar `SELECT 1 ... LIMIT 1`.** No es portable a SQL Server sin `TOP`, y `TOP`
tampoco lo es a MySQL. `EXISTS` expresa la intención y funciona en ambos.

**Escapar `LIKE` con `\`.** Depende del modo `NO_BACKSLASH_ESCAPES` de MySQL. `!`
es un carácter de escape explícito y portable.

## Consecuencias

- **Cambio incompatible (2.0.0):** el código que llamaba a `ExistAsync` deja de
  compilar; debe cambiar a `ExistsAsync`.
- Los parámetros opcionales de cancelación no rompen las llamadas existentes: no
  cambian la firma desde el punto de vista del consumidor.
- `Contains` ahora devuelve coincidencias literales para `%` y `_`. Es el
  comportamiento esperado, pero cambia el resultado de un predicado que antes
  dependía del comodín accidental.
- El SQL de `ExistsAsync` cambia; los tests comparan el `CommandText` completo, así
  que cualquier regresión en el formato se detecta.
