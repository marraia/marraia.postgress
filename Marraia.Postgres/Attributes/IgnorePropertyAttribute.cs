using System;

namespace Marraia.Postgres.Attributes
{
    [AttributeUsage(AttributeTargets.Property |
                        AttributeTargets.Struct)]
    public class IgnorePropertyAttribute : Attribute
    {
    }
}
