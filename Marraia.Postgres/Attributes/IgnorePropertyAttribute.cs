using System;

namespace Marraia.Postgres.Attributes
{
    /// <summary>
    /// Marca uma propriedade que não deve ser persistida pelos comandos gerados (INSERT/UPDATE).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class IgnorePropertyAttribute : Attribute
    {
    }
}
