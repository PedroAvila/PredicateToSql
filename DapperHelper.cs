using Dapper;
using Dapper.Contrib.Extensions;
using System.Collections.Concurrent;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;

namespace PAN.DapperLambdaToSql;

internal static class DapperHelper
{
    // ConcurrentDictionary y no Dictionary: esta caché es estática y se escribe desde
    // cualquier hilo que resuelva una entidad por primera vez. Con Dictionary, dos
    // escrituras simultáneas pueden corromper sus buckets internos. Ver docs/adr/0003.
    private static readonly ConcurrentDictionary<RuntimeTypeHandle, string> TypeTableName = new();

    public static string GetTableName(Type type)
    {
        // Bajo contención GetOrAdd puede ejecutar la fábrica más de una vez, pero solo
        // un valor queda almacenado. Es inocuo aquí: ResolveTableName es una función
        // pura del tipo y siempre devuelve el mismo nombre.
        return TypeTableName.GetOrAdd(type.TypeHandle, _ => ResolveTableName(type));
    }

    private static string ResolveTableName(Type type)
    {
        // Se acepta [Table] de Dapper.Contrib y, por compatibilidad, cualquier otro
        // atributo llamado TableAttribute (por ejemplo el de DataAnnotations). El
        // nombre se lee por reflexión para no depender de dynamic ni de un assembly
        // que esta librería no referencia.
        var tableAttrName =
            type.GetCustomAttribute<TableAttribute>(false)?.Name
            ?? ResolveForeignTableAttributeName(type);

        if (tableAttrName != null)
        {
            return tableAttrName;
        }

        var name = type.Name + "s";
        if (type.IsInterface && name.StartsWith("I"))
            name = name.Substring(1);

        return name;
    }

    private static string? ResolveForeignTableAttributeName(Type type)
    {
        foreach (var attribute in type.GetCustomAttributes(false))
        {
            if (attribute.GetType().Name != "TableAttribute") continue;

            return attribute.GetType().GetProperty("Name")?.GetValue(attribute) as string;
        }

        return null;
    }

    public static bool IsWriteable(PropertyInfo property)
    {
        // Replica el IsWriteable interno de Dapper.Contrib: solo se excluye
        // la propiedad cuando existe un [Write(false)] explícito.
        var writeAttribute = property.GetCustomAttribute<WriteAttribute>(false);
        return writeAttribute?.Write ?? true;
    }

    public static bool IsComputed(PropertyInfo property)
    {
        // [Computed] es un atributo marcador (no tiene propiedades): la columna sí
        // existe en la tabla, pero su valor lo genera el motor -columnas calculadas,
        // DEFAULT, rowversion, triggers- y por eso no debe escribirse.
        return property.GetCustomAttribute<ComputedAttribute>(false) != null;
    }

    public static PropertyInfo GetKeyProperty(Type type, PropertyInfo[] properties)
    {
        // Orden de resolución (ver docs/adr/0002): los atributos explícitos ganan
        // siempre a la convención, y el "Id" exacto se evalúa antes que el resto
        // para no alterar el comportamiento de las entidades que ya funcionaban.
        var explicitKeys = properties
            .Where(p => p.GetCustomAttribute<ExplicitKeyAttribute>(false) != null
                        || p.GetCustomAttribute<KeyAttribute>(false) != null)
            .Take(2)
            .ToList();

        // Las claves compuestas no están soportadas: el WHERE asume una sola
        // columna. Antes se elegía la primera y la otra terminaba en el SET, lo
        // que actualizaba filas de más sin avisar.
        if (explicitKeys.Count > 1)
        {
            throw new NotSupportedException(
                $"La entidad '{type.Name}' tiene más de una clave primaria " +
                $"({string.Join(", ", explicitKeys.Select(p => p.Name))}). " +
                "Las claves compuestas no están soportadas.");
        }

        var key = explicitKeys.FirstOrDefault();
        if (key != null) return key;

        key = properties.FirstOrDefault(p => p.Name == "Id");
        if (key != null) return key;

        // Insensible a mayúsculas: cubre ID, iD.
        key = properties.FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase));
        if (key != null) return key;

        // Clase+Id. Se compara contra un nombre concreto y nunca contra un patrón
        // "termina en Id": de lo contrario las llaves foráneas -GymId, RoleId- o un
        // identificador externo -TransactionId- serían candidatos a clave primaria.
        var expectedName = type.Name + "Id";
        key = properties.FirstOrDefault(p => p.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase));
        if (key != null) return key;

        throw new InvalidOperationException(
            $"La entidad '{type.Name}' no tiene clave primaria. Se buscó una propiedad con " +
            $"[Key] o [ExplicitKey], una llamada 'Id' y una llamada '{expectedName}'.");
    }

    public static ISqlDialect GetDialect(IDbConnection connection)
    {
        // SqlDialects.Forced permite forzar un dialecto concreto cuando el proveedor
        // envuelve la conexión real y por eso no se reconoce por su nombre de tipo.
        var forced = SqlDialects.Forced;
        if (forced != null) return forced;
        if (connection == null) return SqlDialects.None;

        // Mismo criterio que Dapper.Contrib para elegir su ISqlAdapter: el nombre del
        // tipo de conexión. Cubre System.Data.SqlClient y Microsoft.Data.SqlClient
        // -ambos SqlConnection- y MySql.Data y MySqlConnector -ambos MySqlConnection-.
        switch (connection.GetType().Name.ToLowerInvariant())
        {
            case "sqlconnection": return SqlDialects.SqlServer;
            case "mysqlconnection": return SqlDialects.MySql;

            // Un proveedor desconocido conserva el comportamiento previo -sin delimitar-
            // en lugar de arriesgar SQL inválido con delimitadores del motor equivocado.
            default: return SqlDialects.None;
        }
    }

    public static (string Sql, DynamicParameters Parameters) ToSql<T>(Expression<Func<T, bool>> predicate)
        => ToSql(predicate, SqlDialects.None);

    public static (string Sql, DynamicParameters Parameters) ToSql<T>(Expression<Func<T, bool>> predicate, ISqlDialect dialect)
    {
        if (predicate is null) throw new ArgumentNullException(nameof(predicate));
        if (predicate.Parameters.Count != 1)
        {
            throw new ArgumentException(
                "El predicado debe tener exactamente un parámetro (por ejemplo, u => u.Id == 1).",
                nameof(predicate));
        }

        var visitor = new SqlExpressionVisitor(dialect, predicate.Parameters[0]);
        visitor.VisitPredicate(predicate);

        return (visitor.Sql, visitor.Parameters);
    }
}
