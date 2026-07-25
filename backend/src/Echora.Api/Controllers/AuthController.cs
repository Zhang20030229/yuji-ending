using Echora.Api.Authentication;
using Echora.Api.Contracts;
using Echora.Api.Entities;
using Echora.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Echora.Api.Controllers;

/// <summary>提供注册、登录和当前用户资料 API。</summary>
[ApiController]
[Route("api")]
public sealed class AuthController(AuthService auth, UserTokenService tokens) : ControllerBase
{
    /// <summary>创建用户并签发 JWT。</summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("auth/register")]
    public async Task<ActionResult<AuthTokenResponse>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await auth.RegisterAsync(request, cancellationToken);
            return Created("/api/me", tokens.Create(user));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(CreateProblem(exception.Message, StatusCodes.Status400BadRequest));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(CreateProblem(exception.Message, StatusCodes.Status409Conflict));
        }
    }

    /// <summary>验证用户名和密码并签发 JWT。</summary>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("auth/login")]
    public async Task<ActionResult<AuthTokenResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await auth.AuthenticateAsync(request, cancellationToken);
        return user is null
            ? Unauthorized(CreateProblem("用户名或密码错误。", StatusCodes.Status401Unauthorized))
            : Ok(tokens.Create(user));
    }

    /// <summary>JWT 无状态注销；前端清除本地令牌即可。</summary>
    [Authorize]
    [HttpPost("auth/logout")]
    public IActionResult Logout() => NoContent();

    /// <summary>读取当前 JWT 对应的用户资料。</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> GetMe(CancellationToken cancellationToken)
    {
        var user = await auth.GetUserAsync(User.GetRequiredId(), cancellationToken);
        return user is null ? Unauthorized() : Ok(ToResponse(user));
    }

    /// <summary>完成首次资料或修改当前资料。</summary>
    [Authorize]
    [HttpPut("me/profile")]
    public async Task<ActionResult<UserResponse>> SaveProfile(
        ProfileRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(ToResponse(await auth.SaveProfileAsync(
                User.GetRequiredId(),
                request,
                cancellationToken)));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(CreateProblem(exception.Message, StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>修改当前用户密码并返回新 JWT。</summary>
    [Authorize]
    [HttpPut("me/password")]
    public async Task<ActionResult<AuthTokenResponse>> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = await auth.ChangePasswordAsync(User.GetRequiredId(), request, cancellationToken);
            return Ok(tokens.Create(user));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(CreateProblem(exception.Message, StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>删除当前账号；前端必须先展示不可恢复的二次确认。</summary>
    [Authorize]
    [HttpDelete("me")]
    public async Task<IActionResult> DeleteMe(CancellationToken cancellationToken)
    {
        try
        {
            return await auth.DeleteAccountAsync(User.GetRequiredId(), cancellationToken)
                ? Accepted()
                : NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, CreateProblem(
                exception.Message,
                StatusCodes.Status503ServiceUnavailable));
        }
    }

    /// <summary>把数据库实体映射为前端最小资料。</summary>
    private static UserResponse ToResponse(UserAccount user) => new()
    {
        Username = user.Username,
        DisplayName = user.DisplayName,
        Gender = user.Gender,
        BirthYear = user.BirthYear,
        BirthMonth = user.BirthMonth,
        Age = AuthService.CalculateAge(user.BirthYear, user.BirthMonth),
        AiName = user.AiName,
        ProfileCompleted = user.ProfileCompleted,
    };

    /// <summary>创建不暴露内部异常的标准错误。</summary>
    private ProblemDetails CreateProblem(string title, int status) => new()
    {
        Title = title,
        Status = status,
        Instance = Request.Path,
    };
}
