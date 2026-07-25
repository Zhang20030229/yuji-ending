namespace Echora.Api.Contracts;

/// <summary>WHO-5 五道题分别选择的 0～5 分。</summary>
public sealed record WellbeingAssessmentRequest(int[] Answers);
