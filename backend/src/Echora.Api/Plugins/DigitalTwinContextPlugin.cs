using System.ComponentModel;
using System.Text.Json;
using Echora.Api.Entities;
using SqlSugar;

namespace Echora.Api.Plugins;

/// <summary>为 MomentAgent 提供一份紧凑、只读的用户生活资料快照。</summary>
public sealed class DigitalTwinContextPlugin(ISqlSugarClient db, long userId)
{
    /// <summary>读取生成一刻总结或主动慰问所需的近期用户资料。</summary>
    [DisplayName("get_digital_twin_context")]
    [Description("读取用户已经保存的个人资料、近期事件、人物、地点、认识、情绪和心迹报告。生成一刻总结或主动慰问前调用。")]
    public async Task<string> GetAsync(CancellationToken cancellationToken = default)
    {
        var user = await db.Queryable<UserAccount>()
            .Where(item => item.Id == userId)
            .FirstAsync(cancellationToken)
            ?? throw new InvalidOperationException("用户不存在。");
        var events = await db.Queryable<LifeEvent>()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.OccurredAt, OrderByType.Desc)
            .Take(20)
            .ToListAsync(cancellationToken);
        var people = await db.Queryable<Person>()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.UpdatedAt, OrderByType.Desc)
            .Take(50)
            .ToListAsync(cancellationToken);
        var places = await db.Queryable<Place>()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.UpdatedAt, OrderByType.Desc)
            .Take(50)
            .ToListAsync(cancellationToken);
        var recognitions = await db.Queryable<Recognition>()
            .Where(item => item.UserId == userId && item.RejectedAt == null)
            .OrderBy(item => item.CreatedAt, OrderByType.Desc)
            .Take(20)
            .ToListAsync(cancellationToken);
        var emotions = await db.Queryable<EmotionRecord>()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.OccurredAt, OrderByType.Desc)
            .Take(20)
            .ToListAsync(cancellationToken);
        var reports = await db.Queryable<ReportPack>()
            .Where(item => item.UserId == userId
                && item.Status == "Complete"
                && item.OverallContentJson != null)
            .OrderBy(item => item.UpdatedAt, OrderByType.Desc)
            .Take(3)
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            profile = new
            {
                name = user.DisplayName,
                aiName = user.AiName,
                gender = user.Gender,
                birthYear = user.BirthYear,
                birthMonth = user.BirthMonth,
            },
            recentEvents = events.Select(item => new
            {
                item.Title,
                item.Summary,
                item.OccurredAt,
            }),
            people = people.Select(item => new
            {
                item.Name,
                item.Relationship,
                item.RelationshipKeywords,
            }),
            places = places.Select(item => new
            {
                item.Name,
                item.Province,
                item.City,
            }),
            recognitions = recognitions.Select(item => new
            {
                item.Category,
                item.Content,
                item.Keywords,
                item.CreatedAt,
            }),
            recentEmotions = emotions.Select(item => new
            {
                item.Family,
                item.Subtype,
                item.Intensity,
                item.Summary,
                item.OccurredAt,
            }),
            recentReports = reports.Select(item => new
            {
                item.StartDate,
                item.EndDate,
                content = JsonSerializer.Deserialize<JsonElement>(item.OverallContentJson!),
            }),
        }, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
