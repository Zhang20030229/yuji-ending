using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Echora.Api.Serialization;

/// <summary>把 API 中的 long 业务标识写成十进制字符串，并兼容客户端提交字符串或数字。</summary>
internal static class ApiIdentifierJsonContract
{
    /// <summary>只修改名称以 Id/Ids 结尾的 long 属性，不影响序号、字节数和耗时等普通数值。</summary>
    public static void Configure(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;

        foreach (var property in typeInfo.Properties)
        {
            if (!IsIdentifierName(property.Name) || !ContainsLongIdentifier(property.PropertyType)) continue;

            // Web 客户端始终收到字符串，读取时仍兼容尚未迁移完的数字请求。
            property.NumberHandling = JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString;
        }
    }

    /// <summary>判断 JSON 属性名是否表达单个或一组业务标识。</summary>
    private static bool IsIdentifierName(string name) =>
        name.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("Ids", StringComparison.OrdinalIgnoreCase);

    /// <summary>识别 long、可空 long 与常用 long 集合，不把其他数值误当成标识。</summary>
    private static bool ContainsLongIdentifier(Type type)
    {
        if (type == typeof(long) || type == typeof(long?)) return true;

        return type.IsArray
            ? type.GetElementType() == typeof(long)
            : type.IsGenericType && type.GetGenericArguments().Length == 1
                && type.GetGenericArguments()[0] == typeof(long)
                && typeof(IEnumerable<long>).IsAssignableFrom(type);
    }
}
