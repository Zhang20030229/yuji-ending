using System.ComponentModel.DataAnnotations;

namespace Echora.Api.Contracts;

/// <summary>修改会话标题的请求。</summary>
public sealed record RenameConversationSessionRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Title);
