# 遇己 / Echora 论文阅读索引

生成日期：2026-07-30

## 项目判断

`yuji-ending` 更像一个“个人生活与心理自我理解系统”，而不是单纯聊天应用。它的产品内核由四块组成：

- 私人 AI 伙伴：`ConversationAgent` 面向用户做自然对话，并按需调用时间、生活记录、遇己记录查询工具。
- 自动生活档案：后台 `AnalysisWorkflow` fan-out 运行 `LifeRecordSubagent`、`RecognitionSubagent`、`EmotionSubagent`，从聊天和“一刻”中整理人物、地点、事件、认识、情绪和 CBT 自我观察。
- 情绪与心理回看：前端有日/月/年情绪洞察，后端用确定性投影生成情绪统计；心迹报告还接入 WHO-5 自评，但明确不做诊断。
- 长期记忆与数字分身：`MomentAgent`、`DigitalTwinContextPlugin`、`HeartReportWorkflow` 把保存过的经历、人物、地点、情绪、认识和报告作为后续理解与主动关怀的上下文。

因此论文选择优先覆盖：personal informatics / lifelogging、reflection design、情绪对话数据、心理健康聊天代理、CBT 结构化问答、长期记忆型 LLM Agent。

## 建议阅读顺序

1. 先读 personal informatics 三篇，校准“记录-整合-反思-行动”的产品框架。
2. 再读心理健康聊天代理三篇，校准安全边界、评价方式和临床/非临床措辞。
3. 然后读情绪对话数据两篇，反推当前情绪抽取词表、证据原话和多模态限制。
4. 最后读 Generative Agents 和 MemGPT，用来升级长期记忆、检索、反思和主动关怀架构。

## 已下载论文

| 优先级 | 文件 | 论文 | 为什么和项目有关 | 来源 |
| --- | --- | --- | --- | --- |
| P0 | `2010-li-stage-based-model-personal-informatics.pdf` | Li, Dey, Forlizzi. **A Stage-Based Model of Personal Informatics Systems**. CHI 2010. | 对应项目的采集、整合、反思和行动闭环。适合检查从消息/照片到情绪洞察、心迹报告、小实验的产品路径是否完整。 | https://www.ianli.com/publications/2010-ianli-chi-stage-based-model.pdf |
| P0 | `2010-sellen-whittaker-beyond-total-capture-lifelogging.web-archive-print.pdf` / `2010-sellen-whittaker-beyond-total-capture-lifelogging.html` | Sellen, Whittaker. **Beyond Total Capture: A Constructive Critique of Lifelogging**. Communications of the ACM 2010. | 这篇是 lifelogging 经典批判：系统不应迷信“捕获一切”，而应围绕人如何记忆、选择、回看和赋义来设计。非常适合校准遇己的生活记录、长期记忆和报告功能。 | https://web.archive.org/web/20100501123313/http://cacm.acm.org/magazines/2010/5/87249-beyond-total-capture/fulltext |
| P0 | `2015-epstein-lived-informatics-model.pdf` | Epstein et al. **A Lived Informatics Model of Personal Informatics**. UbiComp 2015. | 比阶段模型更贴近日常真实使用，适合指导“一刻”、iMessage、会话片段和长期回看如何融入生活，而不是变成填表工具。 | https://homes.cs.washington.edu/~jfogarty/publications/ubicomp2015.pdf |
| P0 | `2015-baumer-reflective-informatics.pdf` | Baumer. **Reflective Informatics: Conceptual Dimensions for Designing Technologies of Reflection**. CHI 2015. | 对应“遇己”的核心：系统不是给结论，而是帮助用户反思。特别适合审视报告措辞、uncertainties、reflectionQuestions 和非诊断边界。 | https://cpb-us-e1.wpmucdn.com/blogs.cornell.edu/dist/c/3483/files/2017/02/Baumer2015-Reflective-166qq82.pdf |
| P0 | `2017-fitzpatrick-woebot-cbt-rct.pdf` | Fitzpatrick, Darcy, Vierhile. **Delivering Cognitive Behavior Therapy to Young Adults With Symptoms of Depression and Anxiety Using a Fully Automated Conversational Agent (Woebot): A Randomized Controlled Trial**. JMIR Mental Health 2017. | 项目已经实现 CBT observation、helpfulResponses 和 smallExperiment，这篇是心理健康聊天代理的经典 RCT，可用来校准“陪伴但不诊断”的边界。 | https://mental.jmir.org/2017/2/e19/ |
| P0 | `2023-li-ai-conversational-agents-mental-health-meta-analysis.pdf` | Li et al. **Systematic review and meta-analysis of AI-based conversational agents for promoting mental health and well-being**. npj Digital Medicine 2023. | 比单一系统更适合作为证据地图：哪些 outcome 有证据、哪些评价不足、什么场景应谨慎。适合心迹报告和 WHO-5 设计。 | https://www.nature.com/articles/s41746-023-00979-5 |
| P0 | `2023-cho-integrative-survey-mental-health-conversational-agents.pdf` | Cho, Rai, Ungar, Sedoc, Guntuku. **An Integrative Survey on Mental Health Conversational Agents to Bridge Computer Science and Medical Perspectives**. EMNLP 2023. | 直接指出计算机科学指标和医学结果指标之间的断层。适合项目之后设计离线评测、人工审核和安全评估。 | https://arxiv.org/abs/2310.17017 |
| P1 | `2019-rashkin-empatheticdialogues.pdf` | Rashkin et al. **Towards Empathetic Open-domain Conversation Models: A New Benchmark and Dataset**. ACL 2019. | 对应 `ConversationAgent` 的回应风格：先回应感受、自然、不要生硬咨询化。可用于设计陪伴式回复评测集。 | https://aclanthology.org/P19-1534/ |
| P1 | `2019-poria-meld-multimodal-emotion-recognition.pdf` | Poria et al. **MELD: A Multimodal Multi-Party Dataset for Emotion Recognition in Conversations**. ACL 2019. | 当前项目从文字、图片和会话中抽情绪，但代码刻意要求“原话证据”。这篇可帮助比较多模态情绪识别和基于原话的保守抽取之间的取舍。 | https://aclanthology.org/P19-1050/ |
| P1 | `2024-na-cbt-llm-chinese-cbt-mental-health-qa.pdf` | Na. **CBT-LLM: A Chinese Large Language Model for Cognitive Behavioral Therapy-based Mental Health Question Answering**. LREC-COLING 2024. | 中文 CBT 问答方向，和项目中文提示词、CBT 结构化字段、非诊断心理支持都贴近。可借鉴结构化 CBT 回复模板，但需谨慎区分“问答模型”和“个人长期陪伴系统”。 | https://aclanthology.org/2024.lrec-main.261/ |
| P1 | `2023-park-generative-agents-interactive-simulacra.pdf` | Park et al. **Generative Agents: Interactive Simulacra of Human Behavior**. arXiv / CHI 2023. | 对应数字分身、长期记忆、反思总结和主动关怀。它的 observation-memory-reflection-planning 架构可作为 `DigitalTwinContextPlugin` 和心迹报告的升级参考。 | https://arxiv.org/abs/2304.03442 |
| P2 | `2023-packer-memgpt-llm-memory-os.pdf` | Packer et al. **MemGPT: Towards LLMs as Operating Systems**. arXiv 2023. | 当前检索插件主要是小规模内存 contains 匹配。MemGPT 可作为长期上下文管理、跨会话记忆分层和检索策略升级参考。 | https://arxiv.org/abs/2310.08560 |

## 直接落地建议

- 把论文映射到代码评审项：每篇只提炼 3 到 5 条设计准则，挂到对应模块，例如 `EmotionSubagent`、`HeartReportWorkflow`、`ConversationAgent`。
- 优先补评测，而不是先换模型：当前提示词和校验已经很保守，下一步更需要 gold cases，覆盖“无情绪不等于平静”“一次想法不是长期认识”“不能把相关写成因果”等规则。
- 升级检索前先明确数据规模：MVP 用 contains 可以，但一旦记录变多，建议引入 Postgres FTS / trigram / embedding 的分层检索，并继续保持证据引用。
- 心理健康能力只做支持性 self-reflection：从 Woebot、系统综述和 EMNLP survey 看，项目应继续避免诊断、治疗承诺和高风险危机场景自动处理。

## 下载说明

Sellen & Whittaker 的 ACM DL 原始 PDF 仍会返回 403，Microsoft Research 直链也不可用；本目录保存的是 Internet Archive 中 CACM 全文 HTML 快照，以及由本机 Chrome 打印生成的 8 页 PDF 阅读版。文件名中的 `web-archive-print` 用来区分它不是出版社原始 PDF。
