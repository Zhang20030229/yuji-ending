using SqlSugar;

namespace Echora.Api.Data;

/// <summary>注册 PostgreSQL SqlSugar 客户端。</summary>
public static class SqlSugarRegistration
{
    /// <summary>注册线程安全的 SqlSugarScope，并使用实体特性作为映射来源。</summary>
    public static IServiceCollection AddEchoraDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Default。");

        services.AddSingleton<ISqlSugarClient>(_ => new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = connectionString,
            DbType = DbType.PostgreSQL,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        }));
        return services;
    }
}
