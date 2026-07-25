using System.ComponentModel;
using System.Text.Json;

namespace Echora.Api.Agents;

/// <summary>子报告与综合心迹共用的最小结构化内容。</summary>
[Description("一份有标题、总结、依据、反思问题和不确定性的报告内容。")]
public sealed class ReportContent
{
    /// <summary>报告卡的一句话标题。</summary>
    [Description("一句话标题。")]
    public string Headline { get; set; } = string.Empty;

    /// <summary>简短、温和且忠于依据的总结。</summary>
    [Description("简短、温和且忠于依据的总结。")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>最多三条带依据的观察。</summary>
    [Description("最多三条带依据的观察。")]
    public ReportFinding[] Findings { get; set; } = [];

    /// <summary>最多两个供用户自行思考的问题。</summary>
    [Description("最多两个供用户自行思考的问题。")]
    public string[] ReflectionQuestions { get; set; } = [];

    /// <summary>样本或解释上的限制。</summary>
    [Description("样本或解释上的限制。")]
    public string[] Uncertainties { get; set; } = [];

    /// <summary>由至少两个不同日期来源支持的可能循环。</summary>
    [Description("由至少两个不同日期来源支持的可能循环；没有时为空数组。")]
    public CbtCycle[] CbtCycles { get; set; } = [];

    /// <summary>记录中已经出现的有帮助应对。</summary>
    [Description("记录中已经出现的有帮助应对；没有时为空数组。")]
    public HelpfulResponse[] HelpfulResponses { get; set; } = [];

    /// <summary>基于已有循环或应对提出的一个小实验。</summary>
    [Description("最多一个具体、低风险、可执行的小实验；没有时为空。")]
    public SmallExperiment? SmallExperiment { get; set; }

    /// <summary>解析模型 JSON，并只保留真实 evidenceRef 支持的最小内容。</summary>
    public static ReportContent Parse(
        string raw,
        IReadOnlySet<string> allowedEvidenceRefs,
        string userDisplayName,
        IReadOnlyDictionary<string, DateOnly>? evidenceDates = null)
    {
        var content = JsonSerializer.Deserialize<ReportContent>(raw, JsonOptions)
            ?? throw new InvalidDataException("报告 JSON 为空。");
        content.Headline = Personalize(content.Headline, userDisplayName);
        content.Summary = Personalize(content.Summary, userDisplayName);
        if (content.Headline.Length == 0 || content.Summary.Length == 0)
            throw new InvalidDataException("报告缺少标题或总结。");

        content.Findings = (content.Findings ?? [])
            .Where(item => item is not null)
            .Select(item =>
            {
                item.Title = Personalize(item.Title, userDisplayName);
                item.Observation = Personalize(item.Observation, userDisplayName);
                item.EvidenceRefs = (item.EvidenceRefs ?? [])
                    .Where(allowedEvidenceRefs.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                item.Confidence = string.Equals(item.Confidence, "High", StringComparison.OrdinalIgnoreCase)
                    ? "High"
                    : "Medium";
                return item;
            })
            .Where(item => item.Title.Length > 0
                && item.Observation.Length > 0
                && item.EvidenceRefs.Length > 0)
            .Take(3)
            .ToArray();
        if (content.Findings.Length == 0)
            throw new InvalidDataException("报告没有包含真实依据的观察。");

        content.ReflectionQuestions = Clean(content.ReflectionQuestions, 2, userDisplayName);
        content.Uncertainties = Clean(content.Uncertainties, 3, userDisplayName);
        content.CbtCycles = (content.CbtCycles ?? [])
            .Select(item =>
            {
                item.Title = Personalize(item.Title, userDisplayName);
                item.Observation = Personalize(item.Observation, userDisplayName);
                item.EvidenceRefs = (item.EvidenceRefs ?? [])
                    .Where(allowedEvidenceRefs.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return item;
            })
            .Where(item => item.Title.Length > 0
                && item.Observation.Length > 0
                && item.EvidenceRefs.Length >= 2
                && evidenceDates is not null
                && item.EvidenceRefs
                    .Where(evidenceDates.ContainsKey)
                    .Select(reference => evidenceDates[reference])
                    .Distinct()
                    .Count() >= 2)
            .Take(2)
            .ToArray();
        content.HelpfulResponses = (content.HelpfulResponses ?? [])
            .Select(item =>
            {
                item.Observation = Personalize(item.Observation, userDisplayName);
                item.EvidenceRefs = (item.EvidenceRefs ?? [])
                    .Where(allowedEvidenceRefs.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return item;
            })
            .Where(item => item.Observation.Length > 0 && item.EvidenceRefs.Length > 0)
            .Take(2)
            .ToArray();
        if (content.SmallExperiment is not null)
        {
            content.SmallExperiment.Title = Personalize(content.SmallExperiment.Title, userDisplayName);
            content.SmallExperiment.Action = Personalize(content.SmallExperiment.Action, userDisplayName);
            content.SmallExperiment.ReflectionQuestion = Personalize(content.SmallExperiment.ReflectionQuestion, userDisplayName);
            if (content.CbtCycles.Length == 0 && content.HelpfulResponses.Length == 0
                || content.SmallExperiment.Title.Length == 0
                || content.SmallExperiment.Action.Length == 0
                || content.SmallExperiment.ReflectionQuestion.Length == 0)
                content.SmallExperiment = null;
        }
        return content;
    }

    /// <summary>序列化可持久化的最终结构。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>清理模型返回的空白和重复短文本。</summary>
    private static string[] Clean(
        IEnumerable<string>? values,
        int limit,
        string userDisplayName) =>
        (values ?? [])
            .Select(value => Personalize(value, userDisplayName))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

    /// <summary>把模型偶发使用的泛称改成启动页保存的真实称呼。</summary>
    private static string Personalize(string? value, string userDisplayName) =>
        (value ?? string.Empty)
            .Trim()
            .Replace("用户", userDisplayName, StringComparison.Ordinal)
            .Replace("当事人", userDisplayName, StringComparison.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
