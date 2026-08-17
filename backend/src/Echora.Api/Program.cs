using Echora.Api.Authentication;
using Echora.Api.Data;
using Echora.Api.Entities;
using Echora.Api.ModelRuntime;
using Echora.Api.Serialization;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using System.Threading.RateLimiting;
using SqlSugar;

var builder = WebApplication.CreateBuilder(args);
// ASP.NET Core 在 Windows 默认启用 EventLog；普通私有部署账号可能没有写入 `.NET Runtime` 源的权限。
// 明确使用 stdout，避免日志后端权限错误反过来中断业务请求，也便于 Docker / 服务管理器统一采集。
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
});
if (builder.Environment.IsDevelopment())
{
    // 本机调试默认值放在 Git 忽略文件中；生产环境不会加载它。
    builder.Configuration.AddJsonFile("appsettings.Development.Local.json", optional: true, reloadOnChange: true);
}
var config = builder.Configuration;

if (!builder.Environment.IsDevelopment())
{
    // 生产靠环境变量注入密钥；缺失时必须启动失败，否则会带着空密钥或空模型凭据对外服务。
    string[] requiredKeys = ["ConnectionStrings:Default", "AI:ApiKey", "AI:Embedding:ApiKey"];
    var missing = requiredKeys.Where(key => string.IsNullOrWhiteSpace(config[key])).ToArray();
    if (missing.Length > 0)
        throw new InvalidOperationException(
            $"Missing required production configuration: {string.Join(", ", missing)}. Provide them as environment variables.");
}

// --- Infrastructure ---
builder.Services.AddEchoraDatabase(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<Echora.Api.Services.AuthService>();
var imessageOptions = config.GetSection(Echora.Api.Services.IMessageOptions.SectionName)
    .Get<Echora.Api.Services.IMessageOptions>() ?? new Echora.Api.Services.IMessageOptions();
builder.Services.AddSingleton(imessageOptions);
builder.Services.AddScoped<Echora.Api.Services.IMessageBindingService>();
builder.Services.AddHttpClient<Echora.Api.Services.IMessageOutboundService>();
var cosOptions = config.GetSection(Echora.Api.Services.CosOptions.SectionName)
    .Get<Echora.Api.Services.CosOptions>() ?? new Echora.Api.Services.CosOptions();
builder.Services.AddSingleton(cosOptions);
builder.Services.AddSingleton<Echora.Api.Services.CosObjectStorage>();
builder.Services.AddSingleton<Echora.Api.Services.LocalObjectStorage>();
builder.Services.AddSingleton<Echora.Api.Services.IObjectStorage>(services =>
{
    var provider = (config["Storage:Provider"] ?? "Local").Trim();
    if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        return services.GetRequiredService<Echora.Api.Services.LocalObjectStorage>();
    if (provider.Equals("COS", StringComparison.OrdinalIgnoreCase))
        return services.GetRequiredService<Echora.Api.Services.CosObjectStorage>();
    throw new InvalidOperationException("Storage:Provider must be Local or COS.");
});
builder.Services.AddSingleton<Echora.Api.Services.ImageMetadataService>();
builder.Services.AddScoped<Echora.Api.Services.AttachmentService>();
builder.Services.AddScoped<Echora.Api.BackgroundJobs.CleanupJob>();
builder.Services.AddScoped<Echora.Api.BackgroundJobs.AccountCleanupJob>();
var aiOptions = config.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
builder.Services.AddSingleton(aiOptions);
builder.Services.AddSingleton<IModelChatClientFactory, ModelChatClientFactory>();
var embeddingOptions = config.GetSection(EmbeddingOptions.SectionName).Get<EmbeddingOptions>()
    ?? new EmbeddingOptions();
// 当前只实现了 MiniMax 的私有 embedding 协议；指向其他供应商会得到无法解析的响应，因此直接视为未配置。
if (embeddingOptions.IsEnabled
    && Uri.TryCreate(embeddingOptions.Endpoint, UriKind.Absolute, out var embeddingEndpoint)
    && ModelVendorResolver.Resolve(embeddingEndpoint) != ModelVendor.MiniMax)
{
    embeddingOptions = new EmbeddingOptions
    {
        Endpoint = embeddingOptions.Endpoint,
        ModelId = embeddingOptions.ModelId,
        GroupId = embeddingOptions.GroupId,
        Dimensions = embeddingOptions.Dimensions,
        BatchSize = embeddingOptions.BatchSize,
        ApiKey = string.Empty,
    };
}
builder.Services.AddSingleton(embeddingOptions);
// embedding 供应商错误必须立刻如实暴露，重试由 Hangfire 在有状态的向量化任务层承担。
builder.Services.AddHttpClient<IMemoryEmbeddingClient, MiniMaxEmbeddingClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    if (!string.IsNullOrWhiteSpace(embeddingOptions.ApiKey))
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", embeddingOptions.ApiKey);
});
builder.Services.AddScoped<Echora.Api.Agents.ConversationAgent>();
builder.Services.AddScoped<Echora.Api.Agents.StructuredAgentRunner>();
builder.Services.AddScoped<Echora.Api.Agents.LifeRecordSubagent>();
builder.Services.AddScoped<Echora.Api.Agents.RecognitionSubagent>();
builder.Services.AddScoped<Echora.Api.Agents.EmotionSubagent>();
builder.Services.AddScoped<Echora.Api.Agents.MomentAgent>();
builder.Services.AddScoped<Echora.Api.Agents.LifeReportAgent>();
builder.Services.AddScoped<Echora.Api.Agents.EmotionReportAgent>();
builder.Services.AddScoped<Echora.Api.Agents.RelationshipReportAgent>();
builder.Services.AddScoped<Echora.Api.Agents.RecognitionReportAgent>();
builder.Services.AddScoped<Echora.Api.Agents.ReportComposerAgent>();
builder.Services.AddScoped<Echora.Api.Workflows.AnalysisWorkflow>();
builder.Services.AddScoped<Echora.Api.Workflows.HeartReportWorkflow>();
builder.Services.AddScoped<Echora.Api.Services.AnalysisService>();
builder.Services.AddScoped<Echora.Api.Services.MemoryEmbeddingService>();
builder.Services.AddScoped<Echora.Api.Services.MemoryRetrievalService>();
builder.Services.AddScoped<Echora.Api.Services.MemoryDigestRenderer>();
builder.Services.AddScoped<Echora.Api.Services.ArchiveViewService>();
builder.Services.AddScoped<Echora.Api.Services.SelfViewService>();
builder.Services.AddScoped<Echora.Api.Services.EmotionSummaryService>();
builder.Services.AddScoped<Echora.Api.Services.HeartReportContextService>();
builder.Services.AddScoped<Echora.Api.Services.HeartReportService>();
builder.Services.AddScoped<Echora.Api.Services.RunLogService>();
builder.Services.AddSingleton<Echora.Api.Realtime.DataUpdateNotifier>();
builder.Services.AddScoped<Echora.Api.Services.ConversationDeletionService>();
builder.Services.AddScoped<Echora.Api.Services.ConversationService>();
builder.Services.AddScoped<Echora.Api.Services.MomentService>();
builder.Services.AddScoped<Echora.Api.Jobs.AnalysisJob>();
builder.Services.AddScoped<Echora.Api.Jobs.MomentJob>();
builder.Services.AddScoped<Echora.Api.Jobs.EmotionSummaryJob>();
builder.Services.AddScoped<Echora.Api.Jobs.MaintenanceJob>();
builder.Services.AddScoped<Echora.Api.Jobs.MemoryEmbeddingJob>();
builder.Services.AddScoped<Echora.Api.Jobs.HeartReportJob>();
builder.Services.AddScoped<Echora.Api.Jobs.HeartReportScheduleJob>();
builder.Services.AddScoped<Echora.Api.Demo.DemoSnapshotSeeder>();
builder.Services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();

// 存储与 IBackgroundJobClient 始终注册：入队只是往 Hangfire 表写一行，不要求本实例运行工作进程。
var hangfireSchema = config["Hangfire:Schema"] ?? "hangfire";
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        options => options.UseNpgsqlConnection(config.GetConnectionString("Default")),
        new PostgreSqlStorageOptions { SchemaName = hangfireSchema }));

// Hangfire:Enabled=false 表示本实例只入队不执行，任务积压在库里等其他实例或下次启动消费。
var hangfireWorkerEnabled = config.GetValue("Hangfire:Enabled", true);
if (hangfireWorkerEnabled)
{
    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = 1;
        options.Queues = ["default"];
    });
}

// --- Multi-user authentication ---
var jwtOptions = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
    throw new InvalidOperationException(
        "Jwt:SigningKey must contain at least 32 UTF-8 bytes and come from local or deployment configuration.");
if (jwtOptions.LifetimeMinutes is < 5 or > 10080)
    throw new InvalidOperationException("Jwt:LifetimeMinutes must be between 5 and 10080.");

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<UserTokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token)
                    && context.HttpContext.Request.Path.StartsWithSegments("/api/updates"))
                    context.Token = token;
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (!long.TryParse(subject, out var userId))
                {
                    context.Fail("JWT user claims are invalid.");
                    return;
                }

                var tokenVersionClaim = context.Principal?.FindFirst("token_version")?.Value;
                if (!int.TryParse(tokenVersionClaim, out var tokenVersion))
                {
                    context.Fail("JWT token version is invalid.");
                    return;
                }

                // 用户存在性与 TokenVersion 由认证服务验证；成功结果短暂缓存并在密码修改、账号删除时主动失效。
                var auth = context.HttpContext.RequestServices.GetRequiredService<Echora.Api.Services.AuthService>();
                if (!await auth.IsTokenValidAsync(userId, tokenVersion, context.HttpContext.RequestAborted))
                    context.Fail("JWT user no longer exists or token was revoked.");
            },
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (context, _) =>
    {
        context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Echora.Api.RateLimiting")
            .LogWarning(
                "Request rate limited: Path {Path}, StatusCode {StatusCode}",
                context.HttpContext.Request.Path,
                StatusCodes.Status429TooManyRequests);
        return ValueTask.CompletedTask;
    };
    o.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// --- CORS ---
// 生产只放行显式配置的站点来源与 iOS 壳的固定自定义 scheme；开发环境额外保留本机与局域网调试入口。
var allowedOrigins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var allowLocalOrigins = builder.Environment.IsDevelopment();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(origin =>
        // Capacitor 打包后的页面来源固定为 capacitor://localhost，scheme 不是 http/https。
        origin is "capacitor://localhost" or "ionic://localhost"
        || allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)
        || (allowLocalOrigins && IsLocalDebugOrigin(origin)))
     .AllowAnyHeader().AllowAnyMethod()));

static bool IsLocalDebugOrigin(string origin) =>
    Uri.TryCreate(origin, UriKind.Absolute, out var uri)
    && uri.Scheme is "http" or "https"
    && (uri.Host is "localhost" or "127.0.0.1" || System.Net.IPAddress.TryParse(uri.Host, out _));

// --- API ---
builder.Services.AddProblemDetails();
builder.Services.AddResponseCompression(options => options.EnableForHttps = true);
var apiJsonResolver = new DefaultJsonTypeInfoResolver();
apiJsonResolver.Modifiers.Add(ApiIdentifierJsonContract.Configure);
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.TypeInfoResolver = apiJsonResolver);
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 生产镜像把 Vite 产物放进 wwwroot；本地开发仍由独立的 Vite 服务提供页面。
var servesSpa = app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists;

// 单实例项目直接以实体为结构事实来源；PostgreSQL 专属检索结构由同一初始化入口补齐。
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
    Echora.Api.Data.DatabaseInitializer.Initialize(
        db,
        embeddingOptions.Dimensions,
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Echora.Api.Data.VectorSchema"));
    if (config.GetValue("DemoData:Enabled", true))
    {
        // Code First 完成后直接恢复已验证快照；启动不再调用模型重新生成演示资料。
        var demo = scope.ServiceProvider.GetRequiredService<Echora.Api.Demo.DemoSnapshotSeeder>();
        await demo.RestoreAsync(CancellationToken.None);
    }
}

if (hangfireWorkerEnabled)
{
    // 固定补投没有成功入队的分析；同名任务在重启时只会被更新。
    var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
    recurringJobs.AddOrUpdate<Echora.Api.Jobs.MaintenanceJob>(
        "echora-analysis-recovery",
        job => job.RecoverAnalysisAsync(CancellationToken.None),
        "*/5 * * * *");
    recurringJobs.AddOrUpdate<Echora.Api.Jobs.MaintenanceJob>(
        "echora-daily-maintenance",
        job => job.ExecuteAsync(CancellationToken.None),
        "30 3 * * *",
        new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai") });
    recurringJobs.AddOrUpdate<Echora.Api.Jobs.MemoryEmbeddingJob>(
        "echora-memory-embedding-backfill",
        job => job.BackfillAsync(CancellationToken.None),
        "*/15 * * * *");
    recurringJobs.AddOrUpdate<Echora.Api.Jobs.HeartReportScheduleJob>(
        "echora-heart-report-schedule",
        job => job.ExecuteAsync(CancellationToken.None),
        "0 4 * * *",
        new RecurringJobOptions { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai") });
}
else
{
    app.Logger.LogWarning(
        "Hangfire worker disabled: this instance only enqueues jobs. Queued work waits for another worker instance.");
}

// --- Pipeline ---
// 反代终止 TLS，需要按 X-Forwarded-* 还原真实 scheme 与客户端 IP，否则 HSTS 判断与登录限流都按容器内网地址计算。
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
// 只信任 Compose 内网段的反代，不放开任意来源，避免伪造 X-Forwarded-For 绕过按 IP 计数的登录限流。
forwardedHeaders.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
app.UseForwardedHeaders(forwardedHeaders);
app.UseResponseCompression();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    // Docker 内由 Nginx 终止 TLS，因此 Compose 会关闭容器内部的 HTTPS 跳转。
    if (config.GetValue("Https:Redirect", true)) app.UseHttpsRedirection();
}
else
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
if (servesSpa)
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context =>
        {
            // Vite 的 assets 文件名含内容哈希，可长期缓存；入口 HTML 保持默认行为以便及时更新版本。
            if (context.Context.Request.Path.StartsWithSegments("/assets"))
                context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        },
    });
}
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<Echora.Api.Realtime.DataUpdatesHub>("/api/updates");
if (servesSpa)
{
    // 未知 API 必须保持 404，不能被 SPA fallback 伪装成一个成功的 HTML 响应。
    app.MapFallback("/api/{**path}", () => Results.NotFound());
    app.MapFallbackToFile("index.html");
}

app.Run();

public partial class Program;
