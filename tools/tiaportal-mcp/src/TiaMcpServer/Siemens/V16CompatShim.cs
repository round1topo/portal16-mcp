using System;
using System.Reflection;

namespace TiaMcpServer.Siemens
{
    internal static class V16CompatShim
    {
        public static string? GetOptionalNamespace(object? engineeringObject)
        {
            if (engineeringObject == null)
                return null;

            try
            {
                return engineeringObject
                    .GetType()
                    .GetProperty("Namespace", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(engineeringObject)
                    ?.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
