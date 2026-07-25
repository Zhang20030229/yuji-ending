using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Echora.Api.Entities;
using Echora.Api.Services;
using SqlSugar;

namespace Echora.Api.Demo;

/// <summary>在 Code First 建表后把已经验证的演示数据和附件原样恢复到数据库。</summary>
public sealed partial class DemoSnapshotSeeder(
    ISqlSugarClient db,
    IObjectStorage storage,
    IWebHostEnvironment environment,
    ILogger<DemoSnapshotSeeder> logger)
{
    /// <summary>快照中的原始 demo 用户 ID；导入非空数据库时会整体映射到新的 ID 空间。</summary>
    private const long SnapshotDemoUserId = 6;

    /// <summary>demo 不存在时恢复快照；已有数据不会被覆盖。</summary>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (await db.Queryable<UserAccount>()
                .AnyAsync(item => item.NormalizedUsername == "DEMO", cancellationToken))
            return;

        var snapshotPath = Path.Combine(environment.ContentRootPath, "Demo", "demo-snapshot.json");
        var snapshot = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath, cancellationToken))?.AsObject()
            ?? throw new InvalidDataException("演示快照不是有效的 JSON 对象。");
        var idOffset = Tables.Max(table =>
            db.Ado.GetLong($"SELECT COALESCE(MAX(id), 0) FROM {table}"));
        RemapIds(snapshot, idOffset);

        // 对象存储先幂等写入；若数据库事务失败，下次启动可安全重试。
        foreach (var attachment in snapshot["attachments"]!.AsArray().Select(item => item!.AsObject()))
        {
            var objectKey = attachment["object_key"]?.GetValue<string>()
                ?? throw new InvalidDataException("演示附件缺少 object_key。");
            var mimeType = attachment["mime_type"]?.GetValue<string>() ?? "image/jpeg";
            var sourcePath = Path.Combine(
                environment.ContentRootPath,
                "Demo",
                "SeedAttachments",
                objectKey.Replace('/', Path.DirectorySeparatorChar));
            await storage.UploadAsync(
                objectKey,
                await File.ReadAllBytesAsync(sourcePath, cancellationToken),
                mimeType,
                cancellationToken);
        }

        db.Ado.BeginTran();
        try
        {
            foreach (var table in Tables)
            {
                var rows = snapshot[table]!.ToJsonString();
                if (rows == "[]") continue;

                // 表名来自服务端常量；PostgreSQL 按目标表类型恢复时间、数组和 JSON 字段。
                db.Ado.ExecuteCommand(
                    $"INSERT INTO {table} SELECT * FROM jsonb_populate_recordset(NULL::{table}, CAST(@rows AS jsonb))",
                    new SugarParameter("@rows", rows));
            }

            // 显式 ID 导入后把序列推进到最大值，后续普通自增插入不会发生冲突。
            foreach (var table in Tables)
                db.Ado.ExecuteCommand(
                    $"SELECT setval(pg_get_serial_sequence('{table}', 'id'), "
                    + $"COALESCE((SELECT MAX(id) FROM {table}), 1), "
                    + $"EXISTS(SELECT 1 FROM {table}))");

            db.Ado.CommitTran();
            logger.LogInformation(
                "Demo snapshot restored: UserId {UserId}, IdOffset {IdOffset}, TableCount {TableCount}",
                SnapshotDemoUserId + idOffset,
                idOffset,
                Tables.Length);
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    /// <summary>把整套快照平移到未占用的 ID 空间，并同步改写所有关系引用。</summary>
    private static void RemapIds(JsonObject snapshot, long offset)
    {
        if (offset == 0) return;

        foreach (var table in Tables)
        foreach (var row in snapshot[table]!.AsArray().Select(item => item!.AsObject()))
        {
            Shift(row, "id", offset);
            foreach (var property in ScalarIdProperties)
                Shift(row, property, offset);
            foreach (var property in ArrayIdProperties)
                ShiftArray(row, property, offset);

            if (row["content_json"] is JsonValue content
                && content.TryGetValue<string>(out var contentJson))
                row["content_json"] = AttachmentReferencePattern().Replace(
                    contentJson,
                    match => $"echora-attachment:{long.Parse(match.Groups[1].Value) + offset}");

            if (row["evidence_json"] is JsonValue evidence
                && evidence.TryGetValue<string>(out var evidenceJson))
                row["evidence_json"] = RemapEmbeddedJson(evidenceJson, offset);
        }
    }

    /// <summary>平移一个可空 long 属性。</summary>
    private static void Shift(JsonObject row, string property, long offset)
    {
        if (row[property] is JsonValue value && value.TryGetValue<long>(out var id))
            row[property] = id + offset;
    }

    /// <summary>平移一个 long 数组属性。</summary>
    private static void ShiftArray(JsonObject row, string property, long offset)
    {
        if (row[property] is not JsonArray ids) return;
        for (var index = 0; index < ids.Count; index++)
            if (ids[index] is JsonValue value && value.TryGetValue<long>(out var id))
                ids[index] = id + offset;
    }

    /// <summary>改写报告证据 JSON 中指向消息、动态和附件的引用。</summary>
    private static string RemapEmbeddedJson(string json, long offset)
    {
        var root = JsonNode.Parse(json);
        RemapEmbeddedNode(root, offset);
        return root?.ToJsonString() ?? json;
    }

    /// <summary>递归处理报告证据对象；统计数值不在允许名单内，不会被误改。</summary>
    private static void RemapEmbeddedNode(JsonNode? node, long offset)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array) RemapEmbeddedNode(item, offset);
            return;
        }
        if (node is not JsonObject obj) return;

        foreach (var property in EmbeddedScalarIdProperties)
            Shift(obj, property, offset);
        foreach (var property in EmbeddedArrayIdProperties)
            ShiftArray(obj, property, offset);
        foreach (var child in obj.Select(pair => pair.Value).ToArray())
            RemapEmbeddedNode(child, offset);
    }

    [GeneratedRegex(@"echora-attachment:(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex AttachmentReferencePattern();

    /// <summary>数据库行中直接指向其他业务表的 long 字段。</summary>
    private static readonly string[] ScalarIdProperties =
    [
        "user_id",
        "conversation_id",
        "continued_from_conversation_id",
        "reply_to_message_id",
        "moment_id",
        "message_id",
        "attachment_id",
        "target_message_id",
        "cover_attachment_id",
        "person_id",
        "place_id",
        "source_moment_id",
        "source_message_id",
        "resolved_entity_id",
        "report_pack_id",
    ];

    /// <summary>数据库行中保存其他业务主键的数组字段。</summary>
    private static readonly string[] ArrayIdProperties = ["attachment_ids", "person_ids", "place_ids"];

    /// <summary>报告证据 JSON 中保存业务主键的字段。</summary>
    private static readonly string[] EmbeddedScalarIdProperties =
        ["userId", "conversationId", "messageId", "momentId", "attachmentId", "personId", "placeId"];

    /// <summary>报告证据 JSON 中保存业务主键数组的字段。</summary>
    private static readonly string[] EmbeddedArrayIdProperties = ["attachmentIds", "personIds", "placeIds"];

    /// <summary>按依赖顺序导入的固定业务表；表名绝不接收外部输入。</summary>
    private static readonly string[] Tables =
    [
        "user_accounts",
        "conversations",
        "conversation_messages",
        "moments",
        "attachments",
        "analysis_runs",
        "people",
        "person_aliases",
        "person_records",
        "places",
        "place_aliases",
        "place_records",
        "life_events",
        "recognitions",
        "emotion_records",
        "cbt_observations",
        "emotion_summaries",
        "unresolved_mentions",
        "report_packs",
        "report_sections",
        "wellbeing_assessments",
    ];
}
