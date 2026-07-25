using Echora.Api.Agents;
using Microsoft.Agents.AI.Workflows;

namespace Echora.Api.Workflows;

/// <summary>使用固定 Fan-out/Fan-in 运行四个报告子智能体，再按条件生成综合心迹。</summary>
public sealed class HeartReportWorkflow(
    LifeReportAgent life,
    EmotionReportAgent emotion,
    RelationshipReportAgent relationship,
    RecognitionReportAgent recognition,
    ReportComposerAgent composer,
    ILogger<HeartReportWorkflow> logger)
{
    /// <summary>只运行达到门槛的分区；每个分支自己捕获异常并返回结果。</summary>
    public async Task<HeartReportResult> RunAsync(
        HeartReportInput input,
        CancellationToken cancellationToken)
    {
        Func<HeartReportInput, ValueTask<HeartReportInput>> startHandler =
            value => ValueTask.FromResult(value);
        Func<HeartReportInput, CancellationToken, ValueTask<ReportBranchResult>> lifeHandler = life.RunAsync;
        Func<HeartReportInput, CancellationToken, ValueTask<ReportBranchResult>> emotionHandler = emotion.RunAsync;
        Func<HeartReportInput, CancellationToken, ValueTask<ReportBranchResult>> relationshipHandler = relationship.RunAsync;
        Func<HeartReportInput, CancellationToken, ValueTask<ReportBranchResult>> recognitionHandler = recognition.RunAsync;
        var start = startHandler.BindAsExecutor("heart_report_start");
        var branches = new List<ExecutorBinding>();
        foreach (var section in input.Sections.Where(item => item.Eligible))
        {
            branches.Add(section.Kind switch
            {
                "Life" => lifeHandler.BindAsExecutor("life_report_agent"),
                "Emotion" => emotionHandler.BindAsExecutor("emotion_report_agent"),
                "Relationship" => relationshipHandler.BindAsExecutor("relationship_report_agent"),
                "Recognition" => recognitionHandler.BindAsExecutor("recognition_report_agent"),
                _ => throw new InvalidDataException($"未知报告分区：{section.Kind}。"),
            });
        }
        if (branches.Count == 0) return new HeartReportResult([], null);

        var collector = new ReportCollector(input, branches.Count);
        Func<ReportBatch, CancellationToken, ValueTask<HeartReportResult>> composeHandler =
            async (batch, token) =>
            {
                var successful = batch.Sections.Where(item => item.Succeeded).ToArray();
                var overall = successful.Length >= 2
                    ? await composer.RunAsync(batch.Input, batch.Sections, token)
                    : null;
                return new HeartReportResult(
                    batch.Sections.OrderBy(item => Array.IndexOf(SectionOrder, item.Kind)).ToArray(),
                    overall);
            };
        var compose = composeHandler.BindAsExecutor("report_composer");
        var workflow = new WorkflowBuilder(start)
            .AddFanOutEdge(start, branches)
            .AddFanInBarrierEdge(branches, collector)
            .AddEdge(collector, compose)
            .WithOutputFrom(compose)
            .Build();

        logger.LogInformation(
            "Heart report Workflow started: ReportPackId {ReportPackId}, Branches {Branches}",
            input.ReportPackId,
            input.Sections.Where(item => item.Eligible).Select(item => item.Kind).ToArray());
        await using var run = await InProcessExecution.RunAsync(
            workflow,
            input,
            cancellationToken: cancellationToken);
        var events = run.NewEvents.ToArray();
        var failure = events.OfType<WorkflowErrorEvent>().LastOrDefault();
        if (failure is not null)
            throw failure.Exception ?? new InvalidOperationException("心迹报告 Workflow 失败。");
        if (events.OfType<WorkflowOutputEvent>().LastOrDefault()?.Data is not HeartReportResult result)
            throw new InvalidOperationException("心迹报告 Workflow 没有返回结果。");
        logger.LogInformation(
            "Heart report Workflow completed: ReportPackId {ReportPackId}, SuccessCount {SuccessCount}, FailureCount {FailureCount}, OverallSucceeded {OverallSucceeded}",
            input.ReportPackId,
            result.Sections.Count(item => item.Succeeded),
            result.Sections.Count(item => !item.Succeeded),
            result.Overall?.Succeeded);
        return result;
    }

    /// <summary>等待全部分支后，把唯一批次传给综合节点。</summary>
    [SendsMessage(typeof(ReportBatch))]
    private sealed class ReportCollector(HeartReportInput input, int expectedCount)
        : Executor<ReportBranchResult>("heart_report_collector")
    {
        private readonly List<ReportBranchResult> _results = [];

        /// <summary>收集一个独立分支结果；达到数量后只发送一次。</summary>
        public override async ValueTask HandleAsync(
            ReportBranchResult message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            _results.Add(message);
            if (_results.Count != expectedCount) return;
            await context.SendMessageAsync(
                new ReportBatch(input, _results.ToArray()),
                cancellationToken: cancellationToken);
            _results.Clear();
        }
    }

    /// <summary>汇总节点与综合节点之间的唯一消息。</summary>
    private sealed record ReportBatch(
        HeartReportInput Input,
        IReadOnlyList<ReportBranchResult> Sections);

    private static readonly string[] SectionOrder =
        ["Life", "Emotion", "Relationship", "Recognition"];
}
