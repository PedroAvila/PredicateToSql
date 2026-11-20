# ADR 0012 — Renombre de la librería a `PAN.PredicateToSql`

- **Estado:** Aceptado
- **Fecha:** 2026-11-19
- **Afecta a:** solución, ambos `.csproj`, namespace de la librería y de los tests,
  CI, [README.md](../../README.md), [CLAUDE.md](../../CLAUDE.md), todos los ADRs
- **Relacionado:** [ADR 0007](0007-readme-y-version-se-actualizan-juntos.md)

## Contexto

El nombre `PAN.DapperLambdaToSql` describía la implementación original —una capa
sobre Dapper que convertía lambdas en SQL— pero quedó corto frente a lo que la
librería hace hoy: traducción de predicados, delimitación por motor, claves,
transacciones y cancelación. Además coexistían dos identificadores distintos: el
assembly/namespace `PAN.DapperLambdaToSql` y el `<PackageId>PAN.DapperLambdaSQL`,
una diferencia que obligaba a explicar en el `CLAUDE.md` y confundía a quien
instalaba el paquete.

La versión 2.0.0 todavía no se publicó, así que es el momento de alinear los
nombres sin mantener alias ni compatibilidad intermedia.

## Decisión

Todos los identificadores del proyecto pasan a **`PAN.PredicateToSql`**:

| Elemento | Nombre anterior | Nombre nuevo |
|---|---|---|
| Paquete NuGet (`PackageId`, `Title`) | `PAN.DapperLambdaSQL` / `PAN.DapperLambdaToSql` | `PAN.PredicateToSql` |
| Proyecto de la librería | `PAN.DapperLambdaToSql.csproj` | `PAN.PredicateToSql.csproj` |
| Solución | `DapperQueryExpressions.sln` | `PAN.PredicateToSql.sln` |
| Namespace del assembly | `PAN.DapperLambdaToSql` | `PAN.PredicateToSql` |
| Proyecto de tests | `PAN.DapperLambdaToSql.Tests.csproj` | `PAN.PredicateToSql.Tests.csproj` |
| Namespace de tests | `PAN.DapperLambdaToSql.Tests` | `PAN.PredicateToSql.Tests` |

Se actualizan además el `InternalsVisibleTo`, las rutas de `ProjectReference`, el
workflow de CI, el README (instalación, título, nota de migración), el `CLAUDE.md`,
las notas de release y todos los ADRs que referenciaban el nombre anterior.

Los nombres de tipos públicos (`DapperExtensions`, `SqlDialects`, `ISqlDialect`) y
los de los métodos **no** cambian: el renombre es de paquete/namespace/proyecto, no
de API. Las URLs de GitHub siguen apuntando a `DapperQueryExpressions` porque
identifican el repositorio remoto, no el producto.

## Alternativas consideradas

**Mantener `PAN.DapperLambdaSQL` como `PackageId` y cambiar solo el namespace.**
Se descartó: preservaba la confusión que motivó el cambio y obligaba a seguir
explicando la diferencia en la documentación.

**Publicar un paquete de compatibilidad que reexporte los tipos antiguos.** Se
descartó: 2.0.0 ya es una versión mayor con cambios incompatibles, y agregar un
paquete puente duplicaría superficie pública y documentación por un beneficio
transitorio.

**Renombrar también las clases a `PredicateExtensions`, `PredicateVisitor`, etc.**
Se descartó: no aporta al objetivo del cambio y multiplicaría las roturas
innecesarias. Va en línea con el [ADR 0010](0010-no-reinventar-la-rueda.md):
cambios mínimos, sin sobre-ingeniería.

## Consecuencias

- **Cambio incompatible (2.0.0):** los consumidores deben instalar
  `PAN.PredicateToSql` y reemplazar `using PAN.DapperLambdaToSql;` por
  `using PAN.PredicateToSql;`.
- Los `.nupkg` generados con el nombre anterior que queden en `bin/` son artefactos
  viejos, ignorados por Git; el siguiente build emite
  `PAN.PredicateToSql.2.0.0.nupkg`.
- La solución, los proyectos y sus namespaces comparten un único nombre, así que no
  hace falta documentar excepciones.
- Todo el código y los tests siguen donde estaban; solo cambiaron rutas, nombres de
  proyecto y namespaces.
