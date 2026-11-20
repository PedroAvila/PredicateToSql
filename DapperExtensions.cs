using Dapper;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace PAN.DapperLambdaToSql;

public static class DapperExtensions
{
    public static async Task<bool> UpdateAsync<T>(
        this IDbConnection connection,
        T entity,
        CancellationToken cancellationToken = default)
    {
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
                .ExecuteAsync(new CommandDefinition(query, parameters, cancellationToken: cancellationToken))
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

    public static async Task<bool> ExistsAsync<T>(
        this IDbConnection connection,
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
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
                query, whereClause.Parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return exists > 0;
    }

    public static async Task<IEnumerable<T>> QueryAsync<T>(
        this IDbConnection connection,
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
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
                query, whereClause.Parameters, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }
}
