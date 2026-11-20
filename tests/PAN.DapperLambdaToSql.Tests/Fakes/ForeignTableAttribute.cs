namespace OtraLibreria;

/// <summary>
/// Simula el [Table] de otra librería (DataAnnotations, EF): DapperHelper lo reconoce
/// por el nombre del tipo y lee Name por reflexión, sin dynamic ni referencia al
/// assembly que lo declara.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class TableAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
