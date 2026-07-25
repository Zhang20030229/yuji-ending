using Echora.Api.Agents;
using Microsoft.Agents.AI.Workflows;

namespace Echora.Api.Workflows;

/// <summary>使用 MAF fan-out/fan-in 并行运行三个固定后台 Subagent。</summary>
public sealed class AnalysisWorkflow(
    LifeRecordSubagent lifeRecord,
    RecognitionSubagent recognition,
    EmotionSubagent emotion,
    ILogger<AnalysisWorkflow> logger)
{
    /// <summary>只编排本次需要运行的分支并汇总结果。</summary>
    public async Task<AnalysisResult> RunAsync(AnalysisInput input, CancellationToken cancellationToken)
    {
        Func<AnalysisInput, ValueTask<AnalysisInput>> startHandler = value => ValueTask.FromResult(value);
        Func<AnalysisInput, CancellationToken, ValueTask<AnalysisBranchResult>> lifeHandler = lifeRecord.RunAsync;
        Func<AnalysisInput, CancellationToken, ValueTask<AnalysisBranchResult>> recognitionHandler = recognition.RunAsync;
        Func<AnalysisInput, CancellationToken, ValueTask<AnalysisBranchResult>> emotionHandler = emotion.RunAsync;
        var start = startHandler.BindAsExecutor("analysis_start");
        var branches = new List<ExecutorBinding>();
        if (input.Branches.Contains("LifeRecord")) branches.Add(lifeHandler.BindAsExecutor("life_record_subagent"));
        if (input.Branches.Contains("Recognition")) branches.Add(recognitionHandler.BindAsExecutor("recognition_subagent"));
        if (input.Branches.Contains("Emotion")) branches.Add(emotionHandler.BindAsExecutor("emotion_subagent"));
        if (branches.Count == 0) return new AnalysisResult([]);

        var collector = new ResultCollector(branches.Count);
        var workflow = new WorkflowBuilder(start)
            .AddFanOutEdge(start, branches)
            .AddFanInBarrierEdge(branches, collector)
            .WithOutputFrom(collector)
            .Build();
        logger.LogInformation(
            "Analysis Workflow started: AnalysisRunId {AnalysisRunId}, TargetMessageId {TargetMessageId}, MomentId {MomentId}, Branches {Branches}",
            input.AnalysisRunId,
            input.TargetMessageId,
            input.MomentId,
            input.Branches);
        await using var run = await InProcessExecution.RunAsync(workflow, input, cancellationToken: cancellationToken);
        var events = run.NewEvents.ToArray();
        var failure = events.OfType<WorkflowErrorEvent>().LastOrDefault();
        if (failure is not null) throw failure.Exception ?? new InvalidOperationException("分析 Workflow 失败。");
        if (events.OfType<WorkflowOutputEvent>().LastOrDefault()?.Data is not AnalysisResult result)
            throw new InvalidOperationException("分析 Workflow 没有返回分支结果。");
        logger.LogInformation(
            "Analysis Workflow completed: AnalysisRunId {AnalysisRunId}, SuccessCount {SuccessCount}, FailureCount {FailureCount}",
            input.AnalysisRunId,
            result.Branches.Count(item => item.Succeeded),
            result.Branches.Count(item => !item.Succeeded));
        return result;
    }

    /// <summary>等待本次分支全部结束后产生唯一 Workflow 输出。</summary>
    [YieldsOutput(typeof(AnalysisResult))]
    private sealed class ResultCollector(int expectedCount) : Executor<AnalysisBranchResult>("analysis_collector")
    {
        private readonly List<AnalysisBranchResult> _results = [];

        /// <summary>收集一个分支结果。</summary>
        public override async ValueTask HandleAsync(
            AnalysisBranchResult message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            _results.Add(message);
            if (_results.Count != expectedCount) return;
            await context.YieldOutputAsync(
                new AnalysisResult(_results.OrderBy(item => item.Branch).ToArray()),
                cancellationToken);
            _results.Clear();
        }
    }
}
