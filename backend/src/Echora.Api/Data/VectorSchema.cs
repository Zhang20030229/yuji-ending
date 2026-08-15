using SqlSugar;

namespace Echora.Api.Data;

/// <summary>建立 pgvector 检索结构；失败只降级不阻断启动。</summary>
public static class VectorSchema
{
    /// <summary>向量检索是否可用；不可用时检索层退化为纯关键词。</summary>
    public static bool VectorSearchAvailable { get; private set; }

    /// <summary>当前生效的向量维度。</summary>
    public static int Dimensions { get; private set; }

    /// <summary>创建扩展、memory_embeddings 表与索引；任何异常都被吞掉并记日志。</summary>
    public static void Initialize(ISqlSugarClient db, int dimensions, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);
        VectorSearchAvailable = false;
        Dimensions = dimensions;

        // 维度是唯一需要拼接进 DDL 的值，必须先收敛到合法整数区间再使用。
        if (dimensions is < 256 or > 4096)
        {
            logger.LogError(
                "Vector schema skipped: Dimensions {Dimensions} is outside the supported range 256-4096.",
                dimensions);
            return;
        }

        try
        {
            db.Ado.ExecuteCommand("CREATE EXTENSION IF NOT EXISTS vector;");

            var existing = db.Ado.SqlQuerySingle<int?>(
                """
                SELECT a.atttypmod
                FROM pg_attribute a
                JOIN pg_class c ON c.oid = a.attrelid
                WHERE c.relname = 'memory_embeddings' AND a.attname = 'embedding' AND a.attnum > 0;
                """);
            if (existing is > 0 && existing != dimensions)
            {
                // 维度变更意味着已有向量全部不可比较；删表属于破坏性操作，交由人工决策。
                logger.LogError(
                    "Vector schema disabled: existing memory_embeddings.embedding has {Existing} dimensions but configuration requires {Configured}. Drop or migrate the table manually.",
                    existing,
                    dimensions);
                return;
            }

            db.Ado.ExecuteCommand(
                $"""
                CREATE TABLE IF NOT EXISTS memory_embeddings (
                    id          bigserial PRIMARY KEY,
                    user_id     bigint      NOT NULL,
                    source_type varchar(20) NOT NULL,
                    source_id   bigint      NOT NULL,
                    content     text        NOT NULL,
                    text_hash   varchar(64) NOT NULL,
                    model_id    varchar(64) NOT NULL,
                    embedding   vector({dimensions}) NOT NULL,
                    occurred_at timestamptz NULL,
                    updated_at  timestamptz NOT NULL DEFAULT now()
                );
                """);
            db.Ado.ExecuteCommand(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS ux_memory_embeddings_source
                    ON memory_embeddings (user_id, source_type, source_id);
                """);
            db.Ado.ExecuteCommand(
                """
                CREATE INDEX IF NOT EXISTS ix_memory_embeddings_user
                    ON memory_embeddings (user_id);
                """);
            // HNSW 无需训练且对增量插入友好，适合单用户数据量较小的场景。
            db.Ado.ExecuteCommand(
                """
                CREATE INDEX IF NOT EXISTS ix_memory_embeddings_vector
                    ON memory_embeddings USING hnsw (embedding vector_cosine_ops);
                """);

            VectorSearchAvailable = true;
            logger.LogInformation("Vector schema ready: Dimensions {Dimensions}.", dimensions);
        }
        catch (Exception exception)
        {
            // 最常见原因是 PostgreSQL 未安装 pgvector 扩展或当前账号无建扩展权限。
            logger.LogWarning(
                exception,
                "Vector schema unavailable; memory search falls back to keyword-only matching.");
        }
    }
}
