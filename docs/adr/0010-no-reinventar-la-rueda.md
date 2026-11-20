# ADR 0010 — No reinventar la rueda ni construir sobre ingeniería inversa

- **Estado:** Aceptado
- **Fecha:** 2026-11-19
- **Afecta a:** toda la librería (`DapperExtensions`, `DapperHelper`, `SqlExpressionVisitor`,
  `SqlDialects`) y al proceso de revisión de cambios
- **Relacionado:** [ADR 0001](0001-excluir-propiedades-no-escribibles-del-update.md),
  [ADR 0003](0003-cache-de-nombres-de-tabla-thread-safe.md),
  [ADR 0004](0004-soporte-multi-motor-por-dialecto.md),
  [ADR 0008](0008-eliminar-paginado-en-memoria.md)

## Contexto

`PAN.PredicateToSql` es, por diseño, una capa delgada sobre **Dapper** y
**Dapper.Contrib**: agrega `UpdateAsync`, `ExistsAsync` y `QueryAsync`, y delega en
esas librerías la ejecución, el binding de parámetros, el mapeo de resultados y las
convenciones de atributos (`[Table]`, `[Key]`, `[ExplicitKey]`, `[Write(false)]`,
`[Computed]`).

Ese límite se cruza con facilidad al agregar funcionalidad. Las tentaciones
concretas son:

- implementar un mapeador propio o un generador de SQL completo;
- definir atributos propios que dupliquen los de Dapper.Contrib;
- reimplementar paginado, transacciones o multi-mapping que Dapper ya resuelve;
- apoyarse en APIs `internal`, formatos no documentados o comportamientos
  accidentales de las dependencias, y copiar código de ellas para "adaptarlo".

El [ADR 0008](0008-eliminar-paginado-en-memoria.md) ya resolvió un caso de esto:
el paginado en memoria se eliminó porque el motor y el consumidor lo resuelven mejor
con SQL propio. Este ADR fija el criterio general para que no vuelva a aparecer por
otro costado.

## Decisión

### 1. Reutilizar antes de implementar

Antes de agregar cualquier funcionalidad se verifica si ya existe en Dapper, en
Dapper.Contrib o en la BCL:

- ejecución, transacciones, binding de parámetros y mapeo → **Dapper**;
- atributos y convenciones de entidad → **Dapper.Contrib**;
- expresiones, reflexión, cancelación y concurrencia → **BCL**.

Si la funcionalidad ya existe, **no se implementa de nuevo**. Como máximo se expone
una sobrecarga o un atajo que componga lo existente.

### 2. No duplicar la superficie de las dependencias

La librería no define atributos equivalentes a los de Dapper.Contrib, no mantiene
su propio caché de mapeo de columnas y no ofrece un `IQueryable` ni un paginado
propio. Cada una de esas cosas ya tiene dueño; duplicarlas obliga a mantener dos
verdades y garantiza que se desincronicen.

### 3. No construir sobre ingeniería inversa

Queda prohibido basar una funcionalidad en:

- APIs `internal`/`private` de Dapper, Dapper.Contrib o los proveedores ADO.NET;
- formatos, nombres de tipo o comportamientos **no documentados** de una dependencia;
- copiar o portar código ajeno para adaptarlo;
- parchear una dependencia por reflexión.

Solo se usan **contratos públicos y documentados**. Si una necesidad no se puede
resolver con ellos, la respuesta por defecto es **no implementarla** y documentar la
limitación. Si aun así fuera imprescindible, requiere un ADR propio que explique por
qué no hay alternativa, cómo se aísla el supuesto y qué test lo cubre.

### 4. Las excepciones vigentes se conservan, pero no se amplían

Hay dos mecanismos ya implementados que rosan esta regla, y se mantienen porque
nacieron de una restricción real:

- **Detección de dialecto por `connection.GetType().Name`** — una librería
  `netstandard2.0` no puede referenciar los paquetes de proveedor. Ver
  [ADR 0004](0004-soporte-multi-motor-por-dialecto.md).
- **Lectura por reflexión de un `TableAttribute` de otra librería** — no se puede
  referenciar ese assembly y `dynamic` no está disponible en el target.

Estas dos excepciones están cubiertas por tests de ambos motores y **no habilitan**
nuevas excepciones por analogía: cualquier otra debe justificarse en un ADR.

### 5. Criterio de revisión

Un cambio que agrega funcionalidad debe poder responder estas preguntas:

1. ¿Dapper, Dapper.Contrib o la BCL ya lo resuelven? Si sí, ¿por qué no se reutiliza?
2. ¿Se está dependiendo de algo que no es API pública y documentada?
3. ¿Se está copiando código de otra librería?
4. ¿Se puede implementar componiendo lo que ya existe, sin nueva infraestructura?

Si alguna respuesta deja dudas, el cambio no entra.

## Alternativas consideradas

**Forkear o vendorizar Dapper.Contrib para poder modificarlo.** Se descartó: obliga
a seguir el upstream a mano, duplica bugs y convierte a la librería en responsable de
código que no le pertenece.

**Implementar un mapeador propio para no depender de Dapper.Contrib.** Se descartó:
es exactamente la rueda que la librería dice no reinventar. Además rompería la
compatibilidad con las entidades ya anotadas por los consumidores.

**Usar reflexión sobre miembros `internal` para evitar atributos.** Se descartó por
frágil: cualquier actualización de la dependencia puede romperlo sin aviso y sin que
el compilador lo detecte.

## Consecuencias

- La librería se mantiene pequeña: cuatro archivos y una superficie pública mínima.
- Cada feature nueva se apoya en una dependencia ya probada, lo que reduce bugs y
  costo de mantenimiento.
- Hay necesidades que **no** se van a cubrir si Dapper no las soporta; se documentan
  como límite en el README o en un ADR, no se improvisan.
- Las excepciones existentes quedan registradas y testeadas; las nuevas requieren un
  ADR y no se aceptan por conveniencia.
- El checklist de la sección 5 se aplica en la revisión de todo PR que agregue
  funcionalidad.
