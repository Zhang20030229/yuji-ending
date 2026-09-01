using System.Collections.Concurrent;
using System.Threading.Channels;
using Echora.Api.Agents;
using Echora.Api.Contracts;
using Echora.Api.Entities;
using Echora.Api.Jobs;
using Hangfire;
using Microsoft.Extensions.AI;
using SqlSugar;

namespace Echora.Api.Services;

/// <summary>维护会话、完整 MAF 消息、流式回答和分析版本。</summary>
public sealed class ConversationService(
    ISqlSugarClient db,
    AttachmentService attachments,
    ConversationAgent agent,
    IBackgroundJobClient backgroundJobs,
    ILogger<ConversationService> logger)
{
    /// <summary>单实例 MVP 中每个会话共用一个发送锁。</summary>
    private static readonly ConcurrentDictionary<long, SemaphoreSlim> ConversationLocks = new();

    /// <summary>单实例内串行创建和归档会话，避免多个浏览器标签同时产生多个 Current 会话。</summary>
    private static readonly SemaphoreSlim SessionMutationLock = new(1, 1);

    /// <summary>会话能否继续发送统一按产品当前使用的中国时区判断。</summary>
    private static readonly TimeZoneInfo ChinaTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    /// <summary>读取全部会话；首次进入聊天时建立唯一当前会话。</summary>
    public async Task<IReadOnlyList<ConversationSessionResponse>> GetSessionsAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        if (!await db.Queryable<Conversation>()
                .AnyAsync(item => item.UserId == userId && item.Channel == "Web", cancellationToken))
            await CreateSessionAsync(userId, new CreateConversationRequest(), cancellationToken);

        await NormalizeCurrentSessionsAsync(userId, cancellationToken);

        var items = await db.Queryable<Conversation>()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.Status == "Current" ? 0 : 1)
            .OrderBy(item => item.LastMessageAt, OrderByType.Desc)
            .OrderBy(item => item.CreatedAt, OrderByType.Desc)
            .ToListAsync(cancellationToken);
        return items.Select(ToSessionResponse).ToArray();
    }

    /// <summary>归档当前会话并创建新的可写会话。</summary>
    public async Task<ConversationSessionResponse> CreateSessionAsync(
        long userId,
        CreateConversationRequest request,
        CancellationToken cancellationToken)
    {
        await SessionMutationLock.WaitAsync(cancellationToken);
        try
        {
            if (request.ContinuedFromConversationId is long sourceId
                && !await db.Queryable<Conversation>()
                    .Where(item => item.Id == sourceId
                        && item.UserId == userId
                        && item.Channel == "Web")
                    .AnyAsync(cancellationToken))
                throw new ArgumentException("引用的历史会话不存在。");

            var now = DateTimeOffset.UtcNow;

            var current = await db.Queryable<Conversation>()
                .Where(item => item.UserId == userId
                    && item.Channel == "Web"
                    && item.Status == "Current")
                .OrderBy(item => item.LastMessageAt == null ? 1 : 0)
                .OrderBy(item => item.LastMessageAt, OrderByType.Desc)
                .OrderBy(item => item.CreatedAt, OrderByType.Desc)
                .FirstAsync(cancellationToken);
            // 只有今天创建的空会话才能复用；跨天的空会话已经只读，复用会让用户点「继续聊」也拿不到可写会话。
            if (current is not null
                && IsWritable(current, now)
                && !await db.Queryable<ConversationMessage>().AnyAsync(item => item.ConversationId == current.Id, cancellationToken))
                return ToSessionResponse(current);

            db.Ado.BeginTran();
            try
            {
                await db.Updateable<Conversation>()
                    .SetColumns(item => new Conversation { Status = "Archived", UpdatedAt = now })
                    .Where(item => item.UserId == userId
                        && item.Channel == "Web"
                        && item.Status == "Current")
                    .ExecuteCommandAsync(cancellationToken);
                var conversation = new Conversation
                {
                    UserId = userId,
                    Channel = "Web",
                    Title = "新对话",
                    Status = "Current",
                    ContinuedFromConversationId = request.ContinuedFromConversationId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                conversation.Id = await db.Insertable(conversation).ExecuteReturnBigIdentityAsync(cancellationToken);
                db.Ado.CommitTran();
                return ToSessionResponse(conversation);
            }
            catch
            {
                db.Ado.RollbackTran();
                throw;
            }
        }
        finally
        {
            SessionMutationLock.Release();
        }
    }

    /// <summary>修复旧版本或并发请求遗留的多个 Current，会话有最新消息者优先保留。</summary>
    private async Task NormalizeCurrentSessionsAsync(long userId, CancellationToken cancellationToken)
    {
        await SessionMutationLock.WaitAsync(cancellationToken);
        try
        {
            var current = await db.Queryable<Conversation>()
                .Where(item => item.UserId == userId
                    && item.Channel == "Web"
                    && item.Status == "Current")
                .OrderBy(item => item.LastMessageAt == null ? 1 : 0)
                .OrderBy(item => item.LastMessageAt, OrderByType.Desc)
                .OrderBy(item => item.CreatedAt, OrderByType.Desc)
                .ToListAsync(cancellationToken);
            var duplicateIds = current.Skip(1).Select(item => item.Id).ToArray();
            if (duplicateIds.Length == 0) return;
            await db.Updateable<Conversation>()
                .SetColumns(item => new Conversation { Status = "Archived", UpdatedAt = DateTimeOffset.UtcNow })
                .Where(item => item.UserId == userId
                    && item.Channel == "Web"
                    && duplicateIds.Contains(item.Id))
                .ExecuteCommandAsync(cancellationToken);
        }
        finally
        {
            SessionMutationLock.Release();
        }
    }

    /// <summary>用户主动修改会话标题；后台整理后不得覆盖。</summary>
    public async Task<ConversationSessionResponse?> RenameSessionAsync(
        long userId,
        long conversationId,
        string title,
        CancellationToken cancellationToken)
    {
        title = title.Trim();
        if (title.Length is 0 or > 120) throw new ArgumentException("会话标题长度应为 1 到 120 个字符。");
        var item = await db.Queryable<Conversation>()
            .Where(row => row.Id == conversationId && row.UserId == userId)
            .FirstAsync(cancellationToken);
        if (item is null) return null;
        item.Title = title;
        item.IsTitleEdited = true;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        await db.Updateable(item).ExecuteCommandAsync(cancellationToken);
        return ToSessionResponse(item);
    }

    /// <summary>可靠保存 User Message，并在同一事务中绑定附件。</summary>
    public async Task<TurnReceipt> CaptureAsync(
        long userId,
        long conversationId,
        string clientMessageId,
        CaptureTurnRequest request,
        CancellationToken cancellationToken) =>
        await CaptureCoreAsync(
            userId,
            conversationId,
            clientMessageId,
            request,
            cancellationToken);

    /// <summary>校验并保存一条用户消息。</summary>
    private async Task<TurnReceipt> CaptureCoreAsync(
        long userId,
        long conversationId,
        string clientMessageId,
        CaptureTurnRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        clientMessageId = clientMessageId.Trim();
        if (clientMessageId.Length is 0 or > 80) throw new ArgumentException("消息幂等标识无效。");
        var text = request.Text?.Trim();
        var attachmentIds = (request.AssetIds ?? []).Distinct().ToArray();
        if (string.IsNullOrWhiteSpace(text) && attachmentIds.Length == 0) throw new ArgumentException("消息内容不能为空。");
        if (text?.Length > 20000) throw new ArgumentException("单条消息不能超过 20000 个字符。");

        var gate = ConversationLocks.GetOrAdd(conversationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var duplicate = await db.Queryable<ConversationMessage>()
                .Where(item => item.UserId == userId
                    && item.ConversationId == conversationId
                    && item.ClientMessageId == clientMessageId)
                .FirstAsync(cancellationToken);
            if (duplicate is not null)
                return new TurnReceipt(duplicate.Id, conversationId, duplicate.CreatedAt, true);

            var conversation = await db.Queryable<Conversation>()
                .Where(item => item.Id == conversationId && item.UserId == userId)
                .FirstAsync(cancellationToken);
            if (conversation is null) throw new KeyNotFoundException("会话不存在。");
            if (conversation.Channel != "Web")
                throw new InvalidOperationException("iMessage 会话只能在 iMessage 中继续。");
            if (!IsWritable(conversation, now))
                throw new InvalidOperationException("以前日期的会话只读，请创建今天的新会话继续聊天。");

            var latest = await db.Queryable<ConversationMessage>()
                .Where(item => item.UserId == userId && item.ConversationId == conversationId)
                .OrderBy(item => item.Sequence, OrderByType.Desc)
                .FirstAsync(cancellationToken);
            if (latest?.Role == "User") throw new InvalidOperationException("上一条消息仍在等待回答。");

            var linkedAttachments = attachmentIds.Length == 0
                ? []
                : await db.Queryable<Attachment>()
                    .Where(item => item.UserId == userId
                        && attachmentIds.Contains(item.Id)
                        && item.MomentId == null)
                    .ToListAsync(cancellationToken);
            if (linkedAttachments.Count != attachmentIds.Length || linkedAttachments.Any(item => item.MessageId is not null))
                throw new ArgumentException("附件不存在、已经绑定或重复提交。");

            var contents = new List<AIContent>();
            if (!string.IsNullOrWhiteSpace(text)) contents.Add(new TextContent(text));
            contents.AddRange(linkedAttachments.Select(item =>
                (AIContent)new UriContent($"echora-attachment:{item.Id}", item.MimeType)));
            var message = new ConversationMessage
            {
                UserId = userId,
                ConversationId = conversationId,
                ClientMessageId = clientMessageId,
                Sequence = (latest?.Sequence ?? 0) + 1,
                Role = "User",
                Text = text,
                ContentJson = ChatMessageJson.Serialize(new ChatMessage(ChatRole.User, contents)),
                Status = "Completed",
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                AccuracyMeters = request.AccuracyMeters,
                LocationCapturedAt = request.LocationCapturedAt,
                Province = request.Province,
                City = request.City,
                LocationName = request.LocationName,
                LocationAddress = request.LocationAddress,
                CreatedAt = now,
            };

            db.Ado.BeginTran();
            try
            {
                message.Id = await db.Insertable(message).ExecuteReturnBigIdentityAsync(cancellationToken);
                if (attachmentIds.Length > 0)
                {
                    await db.Updateable<Attachment>()
                        .SetColumns(item => item.MessageId == message.Id)
                        .Where(item => item.UserId == userId
                            && attachmentIds.Contains(item.Id)
                            && item.MessageId == null
                            && item.MomentId == null)
                        .ExecuteCommandAsync(cancellationToken);
                }
                await db.Updateable<Conversation>()
                    .SetColumns(item => new Conversation { LastMessageAt = now, UpdatedAt = now })
                    .Where(item => item.Id == conversationId && item.UserId == userId)
                    .ExecuteCommandAsync(cancellationToken);
                db.Ado.CommitTran();
            }
            catch
            {
                db.Ado.RollbackTran();
                throw;
            }

            logger.LogInformation(
                "User message saved: ConversationId {ConversationId}, MessageId {MessageId}, Sequence {Sequence}, AttachmentCount {AttachmentCount}",
                conversationId,
                message.Id,
                message.Sequence,
                attachmentIds.Length);
            return new TurnReceipt(message.Id, conversationId, now, false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>可靠保存一条 Photon iMessage 文字消息，并按发件日期归入对应会话。</summary>
    public async Task<TurnReceipt> CaptureIMessageAsync(
        long userId,
        string spaceId,
        string clientMessageId,
        string? text,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken,
        IReadOnlyList<long>? attachmentIds = null)
    {
        spaceId = spaceId.Trim();
        clientMessageId = clientMessageId.Trim();
        text = text?.Trim();
        var assetIds = (attachmentIds ?? []).Distinct().ToArray();
        if (spaceId.Length is 0 or > 500) throw new ArgumentException("iMessage 会话标识无效。");
        if (clientMessageId.Length is 0 or > 80) throw new ArgumentException("iMessage 消息标识无效。");
        if (string.IsNullOrWhiteSpace(text) && assetIds.Length == 0)
            throw new ArgumentException("iMessage 消息内容为空。");
        if (text?.Length > 20000) throw new ArgumentException("iMessage 文字长度无效。");

        await SessionMutationLock.WaitAsync(cancellationToken);
        try
        {
            var duplicate = await db.Queryable<ConversationMessage>()
                .Where(item => item.UserId == userId && item.ClientMessageId == clientMessageId)
                .FirstAsync(cancellationToken);
            if (duplicate is not null)
                return new TurnReceipt(duplicate.Id, duplicate.ConversationId, duplicate.CreatedAt, true);

            var localDate = TimeZoneInfo.ConvertTime(sentAt, ChinaTimeZone).Date;
            var start = new DateTimeOffset(localDate, ChinaTimeZone.GetUtcOffset(localDate)).ToUniversalTime();
            var end = start.AddDays(1);
            var conversation = await db.Queryable<Conversation>()
                .Where(item => item.UserId == userId
                    && item.Channel == "IMessage"
                    && item.ExternalSpaceId == spaceId
                    && item.CreatedAt >= start
                    && item.CreatedAt < end)
                .FirstAsync(cancellationToken);
            if (conversation is null)
            {
                conversation = new Conversation
                {
                    UserId = userId,
                    Channel = "IMessage",
                    ExternalSpaceId = spaceId,
                    Title = "iMessage 对话",
                    Status = "Archived",
                    CreatedAt = sentAt,
                    UpdatedAt = sentAt,
                };
                conversation.Id = await db.Insertable(conversation)
                    .ExecuteReturnBigIdentityAsync(cancellationToken);
            }

            var gate = ConversationLocks.GetOrAdd(conversation.Id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                var latest = await db.Queryable<ConversationMessage>()
                    .Where(item => item.UserId == userId && item.ConversationId == conversation.Id)
                    .OrderBy(item => item.Sequence, OrderByType.Desc)
                    .FirstAsync(cancellationToken);
                if (latest?.Role == "User")
                    throw new InvalidOperationException("上一条 iMessage 仍在等待回答。");

                var linkedAttachments = assetIds.Length == 0
                    ? []
                    : await db.Queryable<Attachment>()
                        .Where(item => item.UserId == userId
                            && assetIds.Contains(item.Id)
                            && item.MessageId == null
                            && item.MomentId == null)
                        .ToListAsync(cancellationToken);
                if (linkedAttachments.Count != assetIds.Length)
                    throw new ArgumentException("iMessage 附件不存在、已经绑定或重复提交。");

                var contents = new List<AIContent>();
                if (!string.IsNullOrWhiteSpace(text)) contents.Add(new TextContent(text));
                contents.AddRange(linkedAttachments.Select(item =>
                    (AIContent)new UriContent($"echora-attachment:{item.Id}", item.MimeType)));
                var message = new ConversationMessage
                {
                    UserId = userId,
                    ConversationId = conversation.Id,
                    ClientMessageId = clientMessageId,
                    Sequence = (latest?.Sequence ?? 0) + 1,
                    Role = "User",
                    Text = text,
                    ContentJson = ChatMessageJson.Serialize(new ChatMessage(ChatRole.User, contents)),
                    Status = "Completed",
                    CreatedAt = sentAt,
                };
                db.Ado.BeginTran();
                try
                {
                    message.Id = await db.Insertable(message)
                        .ExecuteReturnBigIdentityAsync(cancellationToken);
                    if (assetIds.Length > 0)
                    {
                        await db.Updateable<Attachment>()
                            .SetColumns(item => item.MessageId == message.Id)
                            .Where(item => item.UserId == userId
                                && assetIds.Contains(item.Id)
                                && item.MessageId == null
                                && item.MomentId == null)
                            .ExecuteCommandAsync(cancellationToken);
                    }
                    await db.Updateable<Conversation>()
                        .SetColumns(item => new Conversation
                        {
                            LastMessageAt = sentAt,
                            UpdatedAt = DateTimeOffset.UtcNow,
                        })
                        .Where(item => item.Id == conversation.Id && item.UserId == userId)
                        .ExecuteCommandAsync(cancellationToken);
                    db.Ado.CommitTran();
                }
                catch
                {
                    db.Ado.RollbackTran();
                    throw;
                }

                logger.LogInformation(
                    "iMessage saved: ConversationId {ConversationId}, MessageId {MessageId}, Sequence {Sequence}, AttachmentCount {AttachmentCount}",
                    conversation.Id,
                    message.Id,
                    message.Sequence,
                    assetIds.Length);
                return new TurnReceipt(message.Id, conversation.Id, sentAt, false);
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            SessionMutationLock.Release();
        }
    }

    /// <summary>把已经由 Photon 成功发送的主动消息保存到当天 iMessage 历史。</summary>
    public async Task<long> CaptureIMessageOutboundAsync(
        long userId,
        string spaceId,
        string text,
        DateTimeOffset sentAt,
        CancellationToken cancellationToken)
    {
        spaceId = spaceId.Trim();
        text = text.Trim();
        if (spaceId.Length is 0 or > 500) throw new ArgumentException("iMessage 会话标识无效。");
        if (text.Length is 0 or > 20000) throw new ArgumentException("iMessage 文字长度无效。");

        await SessionMutationLock.WaitAsync(cancellationToken);
        try
        {
            var localDate = TimeZoneInfo.ConvertTime(sentAt, ChinaTimeZone).Date;
            var start = new DateTimeOffset(localDate, ChinaTimeZone.GetUtcOffset(localDate)).ToUniversalTime();
            var end = start.AddDays(1);
            var conversation = await db.Queryable<Conversation>()
                .Where(item => item.UserId == userId
                    && item.Channel == "IMessage"
                    && item.ExternalSpaceId == spaceId
                    && item.CreatedAt >= start
                    && item.CreatedAt < end)
                .FirstAsync(cancellationToken);
            if (conversation is null)
            {
                conversation = new Conversation
                {
                    UserId = userId,
                    Channel = "IMessage",
                    ExternalSpaceId = spaceId,
                    Title = "iMessage 对话",
                    Status = "Archived",
                    CreatedAt = sentAt,
                    UpdatedAt = sentAt,
                };
                conversation.Id = await db.Insertable(conversation)
                    .ExecuteReturnBigIdentityAsync(cancellationToken);
            }

            var latest = await db.Queryable<ConversationMessage>()
                .Where(item => item.UserId == userId && item.ConversationId == conversation.Id)
                .OrderBy(item => item.Sequence, OrderByType.Desc)
                .FirstAsync(cancellationToken);
            var message = new ConversationMessage
            {
                UserId = userId,
                ConversationId = conversation.Id,
                Sequence = (latest?.Sequence ?? 0) + 1,
                Role = "Assistant",
                Text = text,
                ContentJson = ChatMessageJson.Serialize(new ChatMessage(ChatRole.Assistant, text)),
                Status = "Completed",
                CreatedAt = sentAt,
            };

            db.Ado.BeginTran();
            try
            {
                message.Id = await db.Insertable(message)
                    .ExecuteReturnBigIdentityAsync(cancellationToken);
                await db.Updateable<Conversation>()
                    .SetColumns(item => new Conversation
                    {
                        LastMessageAt = sentAt,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    })
                    .Where(item => item.Id == conversation.Id && item.UserId == userId)
                    .ExecuteCommandAsync(cancellationToken);
                db.Ado.CommitTran();
            }
            catch
            {
                db.Ado.RollbackTran();
                throw;
            }

            logger.LogInformation(
                "Proactive iMessage saved: ConversationId {ConversationId}, MessageId {MessageId}",
                conversation.Id,
                message.Id);
            return message.Id;
        }
        finally
        {
            SessionMutationLock.Release();
        }
    }

    /// <summary>运行 ConversationAgent，保存完整响应消息并发送可见增量。</summary>
    public IAsyncEnumerable<ConversationStreamEvent> RespondAsync(
        long userId,
        long userMessageId,
        CancellationToken cancellationToken)
        => RespondCoreAsync(userId, userMessageId, cancellationToken);

    /// <summary>创建流式通道并启动唯一回答生产者。</summary>
    private IAsyncEnumerable<ConversationStreamEvent> RespondCoreAsync(
        long userId,
        long userMessageId,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<ConversationStreamEvent>(new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _ = ProduceResponseAsync(userId, userMessageId, channel.Writer, cancellationToken);
        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>在单一写入者中处理异常和持久化，避免流式枚举吞掉错误。</summary>
    private async Task ProduceResponseAsync(
        long userId,
        long userMessageId,
        ChannelWriter<ConversationStreamEvent> output,
        CancellationToken cancellationToken)
    {
        ConversationMessage? userMessage = null;
        SemaphoreSlim? gate = null;
        var gateHeld = false;
        var visibleText = new System.Text.StringBuilder();
        var visibleReasoning = new System.Text.StringBuilder();
        try
        {
            userMessage = await db.Queryable<ConversationMessage>()
                .Where(item => item.Id == userMessageId
                    && item.UserId == userId
                    && item.Role == "User")
                .FirstAsync(cancellationToken);
            if (userMessage is null)
            {
                await output.WriteAsync(
                    new ConversationStreamEvent("failed", ErrorCode: "message.not_found", ErrorMessage: "消息不存在。"),
                    cancellationToken);
                return;
            }

            gate = ConversationLocks.GetOrAdd(userMessage.ConversationId, _ => new SemaphoreSlim(1, 1));
            gateHeld = await gate.WaitAsync(0, cancellationToken);
            if (!gateHeld)
            {
                await output.WriteAsync(
                    new ConversationStreamEvent("failed", ErrorCode: "conversation.busy", ErrorMessage: "当前会话正在生成回答。"),
                    cancellationToken);
                return;
            }

            var existing = await db.Queryable<ConversationMessage>()
                // 兼容修复前由 ChatRole.ToString() 写入的小写 assistant。
                .Where(item => item.UserId == userId
                    && item.ReplyToMessageId == userMessageId
                    && (item.Role == "Assistant" || item.Role == "assistant")
                    && item.Status == "Completed")
                .OrderBy(item => item.Sequence, OrderByType.Desc)
                .FirstAsync(cancellationToken);
            if (existing is not null)
            {
                await output.WriteAsync(new ConversationStreamEvent("completed", AssistantTurnId: existing.Id), cancellationToken);
                return;
            }

            var conversation = await db.Queryable<Conversation>()
                .Where(item => item.Id == userMessage.ConversationId && item.UserId == userId)
                .FirstAsync(cancellationToken)
                ?? throw new KeyNotFoundException("会话不存在。");
            var user = await db.Queryable<UserAccount>()
                .Where(item => item.Id == userId)
                .FirstAsync(cancellationToken)
                ?? throw new InvalidOperationException("用户不存在。");
            var history = await LoadModelHistoryAsync(userId, conversation, userMessage.Sequence, cancellationToken);

            Microsoft.Agents.AI.AgentResponse? completedResponse = null;
            var reasoningStarted = false;
            var request = new ConversationAgentRequest(
                user,
                history,
                TextOnly: conversation.Channel == "IMessage",
                ReferenceTime: userMessage.CreatedAt,
                SessionOpening: history.Count <= 1);
            var updates = agent.RunAsync(request, cancellationToken);
            await foreach (var update in updates)
            {
                if (update.Kind == "reasoning_delta" && !string.IsNullOrEmpty(update.Text))
                {
                    if (!reasoningStarted)
                    {
                        reasoningStarted = true;
                        await output.WriteAsync(new ConversationStreamEvent("reasoning_started"), cancellationToken);
                    }
                    visibleReasoning.Append(update.Text);
                    await output.WriteAsync(new ConversationStreamEvent("reasoning_delta", update.Text), cancellationToken);
                }
                else if (update.Kind == "text_delta" && !string.IsNullOrEmpty(update.Text))
                {
                    if (reasoningStarted)
                    {
                        reasoningStarted = false;
                        await output.WriteAsync(new ConversationStreamEvent("reasoning_completed"), cancellationToken);
                    }
                    visibleText.Append(update.Text);
                    await output.WriteAsync(new ConversationStreamEvent("text_delta", update.Text), cancellationToken);
                }
                else if (update.Kind == "tool_started" && update.ToolCallId is not null)
                {
                    await output.WriteAsync(new ConversationStreamEvent(
                        "tool_started",
                        ToolCallId: update.ToolCallId,
                        ToolName: update.ToolName), cancellationToken);
                }
                else if (update.Kind == "tool_completed" && update.ToolCallId is not null)
                {
                    await output.WriteAsync(new ConversationStreamEvent(
                        "tool_completed",
                        ToolCallId: update.ToolCallId), cancellationToken);
                }
                else if (update.Kind == "completed")
                {
                    completedResponse = update.Response;
                }
            }
            if (reasoningStarted)
                await output.WriteAsync(new ConversationStreamEvent("reasoning_completed"), cancellationToken);
            if (completedResponse is null || completedResponse.Messages.Count == 0)
                throw new InvalidOperationException("主模型没有返回可保存的消息。");

            var assistantId = await SaveCompletedResponseAsync(
                conversation,
                userMessage,
                completedResponse.Messages.ToArray(),
                cancellationToken);
            await output.WriteAsync(new ConversationStreamEvent("completed", AssistantTurnId: assistantId), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && userMessage is not null)
        {
            await SaveIncompleteResponseAsync(
                userMessage,
                visibleText.ToString(),
                visibleReasoning.ToString(),
                "Interrupted",
                "回答连接已中断。",
                CancellationToken.None);
        }
        catch (Exception exception) when (userMessage is not null)
        {
            logger.LogError(
                exception,
                "Conversation response failed: ConversationId {ConversationId}, UserMessageId {UserMessageId}",
                userMessage.ConversationId,
                userMessage.Id);
            var message = SafeError(exception);
            await SaveIncompleteResponseAsync(
                userMessage,
                visibleText.ToString(),
                visibleReasoning.ToString(),
                "Failed",
                message,
                CancellationToken.None);
            await output.WriteAsync(
                new ConversationStreamEvent("failed", ErrorCode: "conversation.response_failed", ErrorMessage: message),
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Conversation response could not start: UserMessageId {UserMessageId}", userMessageId);
            await output.WriteAsync(
                new ConversationStreamEvent("failed", ErrorCode: "conversation.response_failed", ErrorMessage: SafeError(exception)),
                CancellationToken.None);
        }
        finally
        {
            if (gateHeld) gate!.Release();
            output.TryComplete();
        }
    }

    /// <summary>按稳定 Sequence 游标读取聊天界面可见消息。</summary>
    public async Task<ConversationTurnPage> GetTurnsAsync(
        long userId,
        long conversationId,
        int limit,
        int? beforeSequence,
        CancellationToken cancellationToken)
    {
        // 一个 MAF 回答可能包含 reasoning → Function Call → Function Result → final text 多条数据库消息，
        // 界面必须按 ReplyToMessageId 合并成一个逻辑 Assistant Turn。
        var rows = await db.Queryable<ConversationMessage>()
            .Where(item => item.UserId == userId && item.ConversationId == conversationId)
            .OrderBy(item => item.Sequence)
            .ToListAsync(cancellationToken);
        var groups = new List<IReadOnlyList<ConversationMessage>>();
        groups.AddRange(rows.Where(item => string.Equals(item.Role, "User", StringComparison.OrdinalIgnoreCase))
            .Select(item => (IReadOnlyList<ConversationMessage>)new[] { item }));
        groups.AddRange(rows.Where(item => !string.Equals(item.Role, "User", StringComparison.OrdinalIgnoreCase) && item.ReplyToMessageId.HasValue)
            .GroupBy(item => item.ReplyToMessageId!.Value)
            .Select(group => (IReadOnlyList<ConversationMessage>)group.OrderBy(item => item.Sequence).ToArray()));
        var ordered = groups
            .Where(group => beforeSequence is null || group[0].Sequence < beforeSequence.Value)
            .OrderBy(group => group[0].Sequence)
            .ToList();
        var hasMore = ordered.Count > limit;
        ordered = ordered.TakeLast(limit).ToList();
        var userRows = ordered.Where(group => string.Equals(group[0].Role, "User", StringComparison.OrdinalIgnoreCase)).Select(group => group[0]).ToArray();
        var messageIds = userRows.Select(item => item.Id).ToArray();
        var assets = messageIds.Length == 0
            ? []
            : await db.Queryable<Attachment>().Where(item => item.UserId == userId && messageIds.Contains(item.MessageId!.Value)).ToListAsync(cancellationToken);
        var assetsByMessage = assets.GroupBy(item => item.MessageId!.Value).ToDictionary(group => group.Key, group => group.ToArray());
        var items = ordered.Select(group => string.Equals(group[0].Role, "User", StringComparison.OrdinalIgnoreCase)
            ? ToOwnerTurnResponse(group[0], assetsByMessage.GetValueOrDefault(group[0].Id) ?? [])
            : ToAssistantTurnResponse(group)).ToArray();
        return new ConversationTurnPage(items, hasMore ? ordered[0][0].Sequence : null, hasMore);
    }

    /// <summary>从数据库恢复当前会话和可选只读来源会话的完整模型历史。</summary>
    private async Task<IReadOnlyList<ChatMessage>> LoadModelHistoryAsync(
        long userId,
        Conversation conversation,
        int throughSequence,
        CancellationToken cancellationToken)
    {
        var result = new List<ChatMessage>();
        if (conversation.ContinuedFromConversationId is long sourceId)
        {
            result.Add(new ChatMessage(ChatRole.System, "以下是只读历史会话，仅用于理解上下文。"));
            result.AddRange(await LoadConversationMessagesAsync(userId, sourceId, null, cancellationToken));
            result.Add(new ChatMessage(ChatRole.System, "只读历史会话到此结束，以下是当前会话。"));
        }
        result.AddRange(await LoadConversationMessagesAsync(userId, conversation.Id, throughSequence, cancellationToken));
        return result;
    }

    /// <summary>恢复一个会话中所有成功消息，并把附件引用替换成模型可读字节。</summary>
    private async Task<IReadOnlyList<ChatMessage>> LoadConversationMessagesAsync(
        long userId,
        long conversationId,
        int? throughSequence,
        CancellationToken cancellationToken)
    {
        var query = db.Queryable<ConversationMessage>()
            .Where(item => item.UserId == userId
                && item.ConversationId == conversationId
                && item.Status == "Completed"
                && item.ContentJson != null);
        if (throughSequence is int sequence) query = query.Where(item => item.Sequence <= sequence);
        var rows = await query.OrderBy(item => item.Sequence).ToListAsync(cancellationToken);
        var messageIds = rows.Select(item => item.Id).ToArray();
        var assetRows = messageIds.Length == 0
            ? []
            : await db.Queryable<Attachment>().Where(item => item.UserId == userId && messageIds.Contains(item.MessageId!.Value)).ToListAsync(cancellationToken);
        var assetsById = assetRows.ToDictionary(item => item.Id);

        var messages = new List<ChatMessage>(rows.Count);
        foreach (var row in rows)
        {
            var restored = ChatMessageJson.Deserialize(row.ContentJson!);
            if (!restored.Contents.OfType<UriContent>().Any(content => content.Uri.Scheme == "echora-attachment"))
            {
                messages.Add(restored);
                continue;
            }

            var contents = new List<AIContent>(restored.Contents.Count);
            foreach (var content in restored.Contents)
            {
                if (content is not UriContent uri || uri.Uri.Scheme != "echora-attachment")
                {
                    contents.Add(content);
                    continue;
                }
                if (!long.TryParse(uri.Uri.AbsolutePath.Trim('/'), out var attachmentId)
                    && !long.TryParse(uri.Uri.OriginalString[(uri.Uri.OriginalString.IndexOf(':') + 1)..], out attachmentId))
                    throw new InvalidDataException("聊天消息中的附件引用无效。");
                if (!assetsById.TryGetValue(attachmentId, out var attachment))
                    throw new FileNotFoundException("聊天消息引用的附件不存在。");
                contents.Add(new DataContent(
                    await attachments.ReadBytesAsync(attachment, cancellationToken),
                    AttachmentService.GetReadableMimeType(attachment)));
            }
            messages.Add(new ChatMessage(restored.Role, contents)
            {
                AuthorName = restored.AuthorName,
                CreatedAt = restored.CreatedAt,
                MessageId = restored.MessageId,
            });
        }
        return messages;
    }

    /// <summary>原子保存 MAF 返回的全部消息，并建立一个待执行分析版本。</summary>
    private async Task<long> SaveCompletedResponseAsync(
        Conversation conversation,
        ConversationMessage userMessage,
        IReadOnlyList<ChatMessage> responseMessages,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var sequence = await db.Queryable<ConversationMessage>()
            .Where(item => item.UserId == userMessage.UserId && item.ConversationId == conversation.Id)
            .MaxAsync(item => item.Sequence, cancellationToken);
        long assistantId = 0;
        long analysisRunId = 0;
        db.Ado.BeginTran();
        try
        {
            foreach (var responseMessage in responseMessages)
            {
                var row = new ConversationMessage
                {
                    UserId = userMessage.UserId,
                    ConversationId = conversation.Id,
                    Sequence = ++sequence,
                    Role = ToStoredRole(responseMessage.Role),
                    Text = responseMessage.Text,
                    ContentJson = ChatMessageJson.Serialize(responseMessage),
                    Status = "Completed",
                    ReplyToMessageId = userMessage.Id,
                    CreatedAt = now,
                };
                row.Id = await db.Insertable(row).ExecuteReturnBigIdentityAsync(cancellationToken);
                if (responseMessage.Role == ChatRole.Assistant && responseMessage.Contents.OfType<TextContent>().Any())
                    assistantId = row.Id;
            }
            if (assistantId == 0) throw new InvalidOperationException("主模型没有返回最终文本消息。");

            conversation.LastMessageAt = now;
            conversation.UpdatedAt = now;
            await db.Updateable(conversation).ExecuteCommandAsync(cancellationToken);
            analysisRunId = await db.Insertable(new AnalysisRun
            {
                UserId = userMessage.UserId,
                ConversationId = conversation.Id,
                TargetMessageId = userMessage.Id,
                CreatedAt = now,
                UpdatedAt = now,
            }).ExecuteReturnBigIdentityAsync(cancellationToken);
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }

        logger.LogInformation(
            "Assistant response saved: ConversationId {ConversationId}, UserMessageId {UserMessageId}, AssistantMessageId {AssistantMessageId}, ResponseMessageCount {ResponseMessageCount}, AnalysisRunId {AnalysisRunId}",
            conversation.Id,
            userMessage.Id,
            assistantId,
            responseMessages.Count,
            analysisRunId);
        try
        {
            var jobId = backgroundJobs.Enqueue<AnalysisJob>(
                job => job.ExecuteAsync(analysisRunId, CancellationToken.None));
            await db.Updateable<AnalysisRun>()
                .SetColumns(run => run.HangfireJobId == jobId)
                .Where(run => run.Id == analysisRunId && run.UserId == userMessage.UserId)
                .ExecuteCommandAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            // 消息和回答已经可靠保存；补投任务会处理没有 HangfireJobId 的 Pending 记录。
            logger.LogError(
                exception,
                "Analysis enqueue failed: AnalysisRunId {AnalysisRunId}",
                analysisRunId);
        }
        return assistantId;
    }

    /// <summary>保存失败或中断时已经产生的可见内容，但不进入后续上下文和分析。</summary>
    private async Task SaveIncompleteResponseAsync(
        ConversationMessage userMessage,
        string text,
        string reasoning,
        string status,
        string error,
        CancellationToken cancellationToken)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(reasoning)) contents.Add(new TextReasoningContent(reasoning));
        if (!string.IsNullOrEmpty(text)) contents.Add(new TextContent(text));
        var sequence = await db.Queryable<ConversationMessage>()
            .Where(item => item.UserId == userMessage.UserId && item.ConversationId == userMessage.ConversationId)
            .MaxAsync(item => item.Sequence, cancellationToken);
        var row = new ConversationMessage
        {
            UserId = userMessage.UserId,
            ConversationId = userMessage.ConversationId,
            Sequence = sequence + 1,
            Role = "Assistant",
            Text = string.IsNullOrEmpty(text) ? null : text,
            ContentJson = contents.Count == 0 ? null : ChatMessageJson.Serialize(new ChatMessage(ChatRole.Assistant, contents)),
            Status = status,
            ReplyToMessageId = userMessage.Id,
            ErrorMessage = error,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await db.Insertable(row).ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>把 User Message 转换成界面消息。</summary>
    private static ConversationTurnResponse ToOwnerTurnResponse(
        ConversationMessage row,
        IReadOnlyList<Attachment> rowAttachments)
    {
        var parts = new List<ConversationPartResponse>();
        if (!string.IsNullOrWhiteSpace(row.ContentJson))
        {
            var message = ChatMessageJson.Deserialize(row.ContentJson);
            var ordinal = 0;
            foreach (var content in message.Contents)
            {
                if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                    parts.Add(new ConversationPartResponse($"{row.Id}-{ordinal++}", "reasoning", reasoning.Text));
                if (content is TextContent text && !string.IsNullOrEmpty(text.Text))
                    parts.Add(new ConversationPartResponse($"{row.Id}-{ordinal++}", "text", text.Text));
            }
        }
        if (parts.Count == 0 && !string.IsNullOrEmpty(row.ErrorMessage))
            parts.Add(new ConversationPartResponse($"{row.Id}-error", "text", row.ErrorMessage));
        parts.AddRange(rowAttachments.Select(item => new ConversationPartResponse(
            $"{row.Id}-asset-{item.Id}",
            "asset",
            AssetId: item.Id,
            ContentUrl: $"/api/assets/{item.Id}/content",
            MediaType: AttachmentService.GetReadableMimeType(item))));
        return new ConversationTurnResponse(
            row.Id,
            row.Sequence,
            "owner",
            row.CreatedAt,
            parts,
            row.Status,
            row.ErrorMessage);
    }

    /// <summary>把同一次 MAF 回答的推理、Tool 和最终文本合并成一条界面消息。</summary>
    private static ConversationTurnResponse ToAssistantTurnResponse(IReadOnlyList<ConversationMessage> rows)
    {
        var parts = new List<ConversationPartResponse>();
        var results = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.ContentJson))
            .SelectMany(row => ChatMessageJson.Deserialize(row.ContentJson!).Contents.OfType<FunctionResultContent>())
            .Select(item => item.CallId)
            .ToHashSet(StringComparer.Ordinal);
        var ordinal = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ContentJson)) continue;
            foreach (var content in ChatMessageJson.Deserialize(row.ContentJson).Contents)
            {
                if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                    parts.Add(new ConversationPartResponse($"{row.Id}-{ordinal++}", "reasoning", reasoning.Text));
                else if (content is FunctionCallContent call)
                    parts.Add(new ConversationPartResponse(
                        $"{row.Id}-{ordinal++}",
                        "tool",
                        ToolCallId: call.CallId,
                        ToolName: call.Name,
                        ToolStatus: results.Contains(call.CallId) ? "completed" : "failed"));
                else if (content is TextContent text && !string.IsNullOrEmpty(text.Text))
                    parts.Add(new ConversationPartResponse($"{row.Id}-{ordinal++}", "text", text.Text));
            }
        }
        var final = rows.LastOrDefault(row => string.Equals(row.Role, "Assistant", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(row.Text)) ?? rows[^1];
        var error = rows.Select(row => row.ErrorMessage).LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (parts.Count == 0 && error is not null)
            parts.Add(new ConversationPartResponse($"{final.Id}-error", "text", error));
        return new ConversationTurnResponse(
            final.Id,
            rows[0].Sequence,
            "assistant",
            rows[0].CreatedAt,
            parts,
            final.Status,
            error);
    }

    /// <summary>把 MAF 角色统一成数据库约定的首字母大写值。</summary>
    private static string ToStoredRole(ChatRole role)
    {
        if (role == ChatRole.User) return "User";
        if (role == ChatRole.Assistant) return "Assistant";
        if (role == ChatRole.System) return "System";
        return role.ToString();
    }

    /// <summary>判断会话是否在今天创建；内部 Current 状态不再充当发送权限。</summary>
    private static bool IsWritable(Conversation item, DateTimeOffset now) =>
        item.Channel == "Web"
        && TimeZoneInfo.ConvertTime(item.CreatedAt, ChinaTimeZone).Date
        == TimeZoneInfo.ConvertTime(now, ChinaTimeZone).Date;

    /// <summary>把数据库会话转换成公开 DTO，并返回服务端计算的发送权限。</summary>
    private static ConversationSessionResponse ToSessionResponse(Conversation item) =>
        new(
            item.Id,
            item.Title,
            item.CreatedAt,
            item.UpdatedAt,
            item.Status,
            item.Channel,
            IsWritable(item, DateTimeOffset.UtcNow));

    /// <summary>只向用户返回稳定错误，不暴露模型原始响应或密钥。</summary>
    private static string SafeError(Exception exception) => exception switch
    {
        ArgumentException or InvalidOperationException or KeyNotFoundException => exception.Message,
        _ => "主模型回答失败，请在运行日志中查看原因后重试。",
    };
}
