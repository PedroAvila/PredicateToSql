# ADR 0011 — Transacciones, LIKE por prefijo/sufijo y guardia de conexión

- **Estado:** Aceptado
- **Fecha:** 2026-11-19
- **Afecta a:** `DapperExtensions`, `SqlExpressionVisitor`, [README.md](../../README.md),
  [PAN.PredicateToSql.csproj](../../PAN.PredicateToSql.csproj)
- **Relacionado:** [ADR 0005](0005-alcance-de-operadores-y-agrupamiento.md),
  [ADR 0009](0009-renombre-a-exists-cancelacion-y-rhs.md),
  [ADR 0010](0010-no-reinventar-la-rueda.md)

## Contexto

Con `CancellationToken` resuelto, quedaban tres necesidades concretas y acotadas:

1. En ASP.NET Core es habitual ejecutar varias sentencias dentro de una transacción.
   Los tres métodos armaban su `CommandDefinition` sin transaction, así que no había
   forma de participar en una transacción abierta por el llamador sin salirse de la
   librería.
2. `string.Contains` ya se traducía a `LIKE`, pero `StartsWith`/`EndsWith` lanzaban
   `NotSupportedException`. Son búsquedas por prefijo y sufijo muy comunes, y la
   infraestructura para hacerlas —escapado, parámetros, `ESCAPE '!'`— ya existía.
3. Los métodos validaban `entity` y `predicate`, pero una conexión nula terminaba en
   `NullReferenceException` dentro de Dapper o del helper de dialecto.

## Decisión

### Transacciones

Los tres métodos aceptan `IDbTransaction? transaction = null` como último parámetro
opcional (después de `CancellationToken`, para no romper las llamadas existentes) y lo
pasan a `CommandDefinition`. La librería **no** crea, commitea ni hace rollback de
transacciones: eso sigue siendo responsabilidad del llamador. Es composición de lo
que Dapper ya expone, no una abstracción nueva.

### `StartsWith` / `EndsWith`

`VisitContains` pasa a llamarse `VisitLikeMethod` y reconoce tres métodos de `string`:

| Método C# | Patrón |
|---|---|
| `Contains("valor")` | `%valor%` |
| `StartsWith("valor")` | `valor%` |
| `EndsWith("valor")` | `%valor` |

Los tres comparten el mismo escapado de `!`, `%` y `_` y la misma cláusula
`ESCAPE '!'`. Cualquier otro método sigue lanzando `NotSupportedException`, y el
receptor debe seguir siendo una columna simple.

### Guardia de conexión

Los tres métodos lanzan `ArgumentNullException(nameof(connection))` antes de cualquier
otro trabajo, con el mismo criterio que ya se aplicaba a `entity` y `predicate`.

## Alternativas consideradas

**Un parámetro `CommandDefinition` en lugar de `transaction`.** Se descartó: dejaría
que el llamador sobreescriba también el SQL y los parámetros generados por la librería.

**Implementar un `UnitOfWork` propio que abra y cierre transacciones.** Se descartó
por sobre-ingeniería y por el [ADR 0010](0010-no-reinventar-la-rueda.md): ADO.NET y
Dapper ya definen ese contrato.

**Traducir `StartsWith`/`EndsWith` con `SUBSTRING`/`RIGHT` en vez de `LIKE`.** Se
descartó: es SQL más complejo, no aprovecha índices y duplicaría la lógica de `LIKE`
que ya existe.

**Agregar `IN` con colecciones en el mismo cambio.** Se dejó fuera: exige decidir
comportamiento para lista vacía, `null` y límite de parámetros. Merece su propio ADR
si algún día se implementa.

## Consecuencias

- Las firmas públicas crecen un parámetro opcional; el código existente sigue
  compilando sin cambios.
- Los tests capturan la transacción real que Dapper recibe en los tres métodos, igual
  que ya se hacía con el `CancellationToken`.
- `StartsWith` sobre una columna indexada puede usar el índice del motor; `Contains`
  y `EndsWith` con comodín inicial, en general, no.
- No se agrega soporte de `IN`, `CommandTimeout`, bulk operations ni `IQueryable`:
  se mantienen fuera a propósito para no convertir la librería en un ORM.
