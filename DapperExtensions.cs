using Dapper;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace PAN.PredicateToSql;

public static class DapperExtensions
{
    /// <summary>
    /// Actualiza las propiedades no nulas de la entidad cuya clave primaria coincida.
    /// Devuelve <c>false</c> si no se afectó ninguna fila o no hay columnas que actualizar.
    /// </summary>
    /// <param name="cancellationToken">Token para cancelar la operación.</param>
    /// <param name="transaction">Transacción opcional que Dapper usa al ejecutar el comando.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> o <paramref name="entity"/> es <c>null</c>.</exception>
    /// <exception cref="ArgumentException">La clave primaria no tiene valor.</exception>
    /// <exception cref="InvalidOperationException">No se pudo resolver la clave primaria.</exception>
    /// <exception cref="NotSupportedException">La entidad tiene más de una clave marcada con atributos.</exception>
    public static async Task<bool> UpdateAsync<T>(
        this IDbConnection connection,
        T entity,
        CancellationToken cancellationToken = default,
        IDbTransaction? transaction = null)
    {
        if (connection is null) throw new ArgumentNullException(nameof(connection));
        if (entity is null) throw new ArgumentNullException(nameof(entity));

        Type entityType = typeof(T);

        var dialect = DapperHelper.GetDialect(connection);
        var tableName = DapperHelper.GetTableName(entityType);

        // Los indexers no son columnas: GetValue lanzaría TargetParameterCountException.
        PropertyInfo[] properties = entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToArray();

        PropertyInfo keyProperty = DapperHelper.GetKeyProperty(entityType, properties);
        var keyValue = keyProperty.GetValue(entity);

        // Un WHERE con NULL no actualiza nada y devolvería false sin explicar por qué.
        if (keyValue is null)
        {
            throw new ArgumentException(
                $"La entidad '{entityType.Name}' no tiene valor en su clave primaria '{keyProperty.Name}'.",
                nameof(entity));
        }

        StringBuilder setClause = new StringBuilder();
        var parameters = new DynamicParameters();
        foreach (PropertyInfo property in properties)
        {
            // Omitir las propiedades marcadas con [Write(false)] -propiedades de
            // navegación cargadas por JOIN que no existen como columna en la tabla-
            // y las [Computed], cuyo valor produce la base de datos. Ver docs/adr/0001.
            if (!DapperHelper.IsWriteable(property) || DapperHelper.IsComputed(property)) continue;

            // Omitir la clave primaria comparando la instancia resuelta y no el nombre:
            // así las llaves foráneas -GymId, RoleId- se siguen actualizando con normalidad.
            if (property == keyProperty) continue;

            var value = property.GetValue(entity);
            if (value != null)
            {
                // Agregar la propiedad a la parte SET de la consulta SQL. Se delimita la
                // columna pero nunca el parámetro: @Key es válido, @`Key` no lo sería.
                setClause.Append($"{dialect.Delimit(property.Name)} = @{property.Name}, ");
                parameters.Add(property.Name, value); // Agregar el parámetro correspondiente
            }
        }
        parameters.Add(keyProperty.Name, keyValue);
        if (setClause.Length > 0)
        {
            setClause.Length -= 2; // Eliminar la coma y el espacio finales

            // Construir la consulta SQL completa
            var query = $"UPDATE {dialect.Delimit(tableName)} SET {setClause} WHERE {dialect.Delimit(keyProperty.Name)} = @{keyProperty.Name}";

            // Ejecutar la consulta SQL utilizando Dapper
            var affectedRows = await connection
                .ExecuteAsync(new CommandDefinition(
                    query, parameters, transaction, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            // Retornar true si se afectaron filas, false si no
            return affectedRows > 0;
        }
        else
        {
            // No hay propiedades para actualizar, retorno falso
            return false;
        }
    }

    /// <summary>
    /// Indica si existe al menos una fila que cumple el predicado, usando
    /// EXISTS(SELECT 1 ...) en lugar de contar todas las coincidencias.
    /// </summary>
    /// <param name="cancellationToken">Token para cancelar la operación.</param>
    /// <param name="transaction">Transacción opcional que Dapper usa al ejecutar el comando.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> o <paramref name="predicate"/> es <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">El predicado contiene una expresión no soportada.</exception>
    public static async Task<bool> ExistsAsync<T>(
        this IDbConnection connection,
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default,
        IDbTransaction? transaction = null)
    {
        if (connection is null) throw new ArgumentNullException(nameof(connection));
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));

        Type entityType = typeof(T);

        // Obtener el nombre de la tabla utilizando Dapper.Contrib
        var dialect = DapperHelper.GetDialect(connection);
        string tableName = DapperHelper.GetTableName(entityType);

        // CASE WHEN EXISTS es válido en SQL Server y MySQL, y evita contar todas
        // las coincidencias como hacía COUNT(*).
        var query = $"SELECT CASE WHEN EXISTS (SELECT 1 FROM {dialect.Delimit(tableName)} WHERE ";

        // Obtener la condición del predicado y agregarla a la consulta SQL
        var whereClause = DapperHelper.ToSql(predicate, dialect);
        query += whereClause.Sql + ") THEN 1 ELSE 0 END";

        // Ejecutar la consulta SQL utilizando Dapper y obtener el indicador
        var exists = await connection
            .QueryFirstOrDefaultAsync<int>(new CommandDefinition(
                query, whereClause.Parameters, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return exists > 0;
    }

    /// <summary>
    /// Devuelve las filas que cumplen el predicado, al estilo de un Where() de EF.
    /// </summary>
    /// <param name="cancellationToken">Token para cancelar la operación.</param>
    /// <param name="transaction">Transacción opcional que Dapper usa al ejecutar el comando.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> o <paramref name="predicate"/> es <c>null</c>.</exception>
    /// <exception cref="NotSupportedException">El predicado contiene una expresión no soportada.</exception>
    public static async Task<IEnumerable<T>> QueryAsync<T>(
        this IDbConnection connection,
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default,
        IDbTransaction? transaction = null)
    {
        if (connection is null) throw new ArgumentNullException(nameof(connection));
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));

        Type entityType = typeof(T);

        var dialect = DapperHelper.GetDialect(connection);
        string tableName = DapperHelper.GetTableName(entityType);

        // Construir la consulta SQL
        var query = $"SELECT * FROM {dialect.Delimit(tableName)} WHERE ";

        var whereClause = DapperHelper.ToSql(predicate, dialect);
        query += whereClause.Sql;

        // Ejecutar la consulta SQL utilizando Dapper y devolver las filas encontradas
        return await connection
            .QueryAsync<T>(new CommandDefinition(
                query, whereClause.Parameters, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
