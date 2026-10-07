using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using ClassFabric.RemoteBroadcast.Abstractions;
using ClassFabric.RemoteBroadcast.Models;
using ClassIsland.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClassFabric.RemoteBroadcast.Services;

/// <summary>
/// Quick Call 手机端 HTTP 服务（R1–R5、R8、R10–R11 的服务端半边）。
/// 所有业务路由都在 /&lt;接入码&gt;/ 前缀下，无效接入码一律 404——不暴露「这里有个服务」的存在感（R2）。
/// 网页端双形态（PC / 移动）按浏览器 UA 分流（?ui= 可手动覆盖），静态资源一并托管。
/// </summary>
public class BroadcastHttpServer(
    ConfigStore store,
    AccessCodeManager codes,
    QuickCallService quickCall,
    BroadcastStatusStream statusStream,
    IEnumerable<ITeachersDirectoryProvider> teacherProviders,
    string pluginFolder,
    string version,
    ILogger<BroadcastHttpServer> logger) : IHostedService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ConcurrentDictionary<string, (DateTime Start, int Count)> _rateLimits = new();
    private readonly object _listenerLock = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    /// <summary>绑定模式：Lan（局域网可访问）/ LocalOnly（降级，仅本机）/ Stopped。</summary>
    public string BindMode { get; private set; } = "Stopped";

    /// <summary>绑定失败时的人话说明（设置页展示，含下一步指引）。</summary>
    public string? BindHint { get; private set; }

    /// <summary>
    /// 当前降级/失败是否因「缺少 urlacl 授权」而起（AccessDenied 5/1015）。
    /// 一键授权按钮据此显隐（4.4：常态零文案，只在需要时出现）。
    /// </summary>
    public bool NeedsUrlAcl { get; private set; }

    /// <summary>当前监听的端口（一键授权拼 netsh 命令用；未监听时取配置端口）。</summary>
    public int BoundPort { get; private set; } = 5212;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var config = store.Snapshot();
        if (config.ServiceEnabled)
        {
            StartCore(config.Port);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopCore();
        return Task.CompletedTask;
    }

    /// <summary>设置页改端口/开关后调用：整体重启一次监听。</summary>
    public void Restart()
    {
        StopCore();
        var config = store.Snapshot();
        if (config.ServiceEnabled)
        {
            StartCore(config.Port);
        }
        else
        {
            BindMode = "Stopped";
        }
    }

    private void StartCore(int port)
    {
        lock (_listenerLock)
        {
            StopCore();
            port = Math.Clamp(port, 1024, 65535);
            BoundPort = port;
            _listener = new HttpListener();
            _cts = new CancellationTokenSource();
            NeedsUrlAcl = false;

            try
            {
                // 全地址绑定需要一次性的 urlacl 授权（管理员执行一次即可）：
                //   netsh http add urlacl url=http://+:{端口}/ user=Everyone
                // 没授权就会 AccessDenied——这是 Windows HTTP.sys 的规矩，不是 bug，别想着绕。
                _listener.Prefixes.Add($"http://+:{port}/");
                _listener.Start();
                BindMode = "Lan";
                BindHint = null;
                logger.LogInformation("[RemoteBroadcast] HTTP 服务已监听 http://+:{Port}/（局域网可访问）", port);
            }
            catch (HttpListenerException e) when (e.ErrorCode is 5 or 1015)
            {
                // 5 = AccessDenied：典型原因是没配 urlacl。降级到 localhost 保住「本机能调通」的验证能力，
                // 手机端则要等 urlacl 配好——设置页会亮出「一键授权」条（4.4），不再让管理员手抄命令。
                NeedsUrlAcl = true;
                BindHint = "局域网监听失败（需要一次性 urlacl 授权），手机暂时无法访问。点击「一键授权」由系统代为执行，当前已降级为仅本机可访问。";
                logger.LogWarning("[RemoteBroadcast] {Hint}（ErrorCode={Code}）", BindHint, e.ErrorCode);
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{port}/");
                    _listener.Start();
                    BindMode = "LocalOnly";
                }
                catch (Exception inner)
                {
                    BindMode = "Stopped";
                    logger.LogError(inner, "[RemoteBroadcast] 降级监听 localhost 也失败");
                }
            }
            catch (HttpListenerException e)
            {
                // R1：端口被占/冲突必须给明确错误和修改指引，不许静默失败。
                BindMode = "Stopped";
                BindHint = $"端口 {port} 监听失败（{e.Message}）。请在设置页换一个端口（1024–65535）后重启服务。";
                logger.LogError("[RemoteBroadcast] {Hint}", BindHint);
                _listener = null;
            }

            if (_listener?.IsListening == true)
            {
                _ = AcceptLoopAsync(_cts!.Token);
            }
        }
    }

    /// <summary>
    /// 4.4 一键授权：以管理员身份补上当前端口的 urlacl（UAC 提权由 runas 触发）。
    /// 返回 (成功与否, 给设置页看的结果文案)。已存在（183）也按成功算——目标就是「这条授权在」。
    /// </summary>
    public static async Task<(bool Ok, string Message)> AddUrlAclAsync(int port)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = $"http add urlacl url=http://+:{port}/ user=Everyone",
            Verb = "runas", // UAC 提权：netsh 写 urlacl 需要管理员令牌
            UseShellExecute = true,
            CreateNoWindow = true,
        };
        try
        {
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null)
            {
                return (false, "未能启动授权命令。");
            }

            await p.WaitForExitAsync();
            // 0 = 成功；183 = urlacl 已存在（重复添加），同样视为达标
            return p.ExitCode is 0 or 183
                ? (true, $"端口 {port} 的局域网授权已就绪。")
                : (false, $"授权命令退出码 {p.ExitCode}，未生效。可改用「复制命令」由管理员手动执行。");
        }
        catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            // 1223 = 用户在 UAC 弹窗里点了「否」，不算错误，只算没授权
            return (false, "已取消授权。");
        }
        catch (Exception e)
        {
            return (false, "授权命令执行失败：" + e.Message);
        }
    }

    private void StopCore()
    {
        lock (_listenerLock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            // 先礼貌关掉在线的实时状态流客户端：页面检测到断开会回退轮询并自动重连。
            statusStream.CloseAll();
            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch (Exception e)
                {
                    logger.LogDebug(e, "[RemoteBroadcast] 关闭监听时的收尾异常（可忽略）");
                }
            }

            _listener = null;
            BindMode = "Stopped";
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        // GetContextAsync 在 Stop 时会抛 ObjectDisposed/异常，这里当作正常退出信号处理。
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener!.GetContextAsync();
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException e)
            {
                logger.LogWarning("[RemoteBroadcast] Accept 异常（{Code}），继续接受下一个连接", e.ErrorCode);
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleAsync(ctx);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "[RemoteBroadcast] 处理请求时出现未捕获异常（{Path}）", ctx.Request.Url?.AbsolutePath);
                    TryWriteJson(ctx, 500, new { error = "服务器内部错误" });
                }
            }, CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        var response = ctx.Response;
        response.Headers["Cache-Control"] = "no-store";

        // 路由第一刀：切出接入码。码不对直接 404，后面的 api/静态都不会存在。
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || codes.TryGet(segments[0]) == null)
        {
            TryWriteJson(ctx, 404, new { error = "Not Found" });
            return;
        }

        var subPath = "/" + string.Join("/", segments.Skip(1));
        switch (subPath)
        {
            case "/" or "/index.html" or "/mobile.html" or "/pc.html" or "/legacy.html":
                ServeUi(ctx, subPath);
                return;
            case { } p when p.StartsWith("/assets/"):
                ServeAsset(ctx, p);
                return;
            case "/api/register" when ctx.Request.HttpMethod == "POST":
                await HandleRegisterAsync(ctx);
                return;
            case "/api/config":
                HandleConfig(ctx);
                return;
            case "/api/quickcall" when ctx.Request.HttpMethod == "POST":
                await HandleQuickCallAsync(ctx);
                return;
            case "/api/status":
                HandleStatus(ctx);
                return;
            case "/api/history" when ctx.Request.HttpMethod == "POST":
                await HandleHistoryDeleteAsync(ctx);
                return;
            case "/api/history":
                HandleHistory(ctx);
                return;
            case "/ws":
                // P-A 实时状态流：升级为 WebSocket 后只推不收，接入码已在路由层验过。
                await statusStream.HandleAsync(ctx, _cts!.Token);
                return;
            default:
                TryWriteJson(ctx, 404, new { error = "Not Found" });
                return;
        }
    }

    // ---- 各端点实现 ----

    private async Task HandleRegisterAsync(HttpListenerContext ctx)
    {
        var body = await ReadJsonBodyAsync<RegisterRequest>(ctx);
        if (body == null)
        {
            return;
        }

        // R10：姓名简称 2–20 字符。首尾空格是手滑，帮忙去掉；去掉后不达标就是真不达标。
        var name = body.TeacherName?.Trim() ?? "";
        if (name.Length is < 2 or > 20)
        {
            TryWriteJson(ctx, 400, new { error = "姓名简称需为 2–20 个字符" });
            return;
        }

        var code = GetCodeSegment(ctx)!;
        if (!codes.Bind(code, name))
        {
            // 理论上走不到（路由层已验过码），但万一在两步之间码被撤销了，还是按 404 处理干净。
            TryWriteJson(ctx, 404, new { error = "Not Found" });
            return;
        }

        logger.LogInformation("[RemoteBroadcast] 教师登记绑定：{Name} → 接入码 {Code}…", name, code[..8]);
        TryWriteJson(ctx, 200, new { bound = true });
    }

    private void HandleConfig(HttpListenerContext ctx)
    {
        var config = store.Snapshot();
        TryWriteJson(ctx, 200, new
        {
            // 学号 = 名单位序（1 起，按总人数补零对齐）。不落盘、纯推导：名单增删后编号自动重排，
            // 正是「数量变动动态调整」想要的效果，也省掉一份会过期的持久化字段。
            roster = config.Roster.Select((s, i) => new
            {
                id = s.Id,
                name = s.Name,
                group = s.Group,
                no = FormatStudentNo(i, config.Roster.Count),
            }),
            destinations = config.Destinations.Select(d => new { id = d.Id, name = d.Name }),
            // 接洽人名单：教师目录提供方优先（谁有能力谁提供），没有时回退手动名单。
            // 来源说明原样下发，网页端 InfoBar 标明「名单从哪来」，不让人猜。
            teachers = ResolveTeachers(config, out var teachersSource),
            teachersSource,
            loopCount = config.AnnouncementLoopCount,
            version,
            hostVersion = AppBase.AppVersion,
        });
    }

    /// <summary>接洽人名单合并：目录提供方在且返回非空名单 → 用目录；否则回退手动维护名单。</summary>
    private List<object> ResolveTeachers(RemoteBroadcastConfig config, out string source)
    {
        foreach (var provider in teacherProviders)
        {
            try
            {
                var result = provider.GetTeachersAsync().GetAwaiter().GetResult();
                if (result.Teachers.Count > 0)
                {
                    source = result.Source is { Length: > 0 } ? result.Source : provider.DisplayName;
                    return result.Teachers
                        .Select(t => (object)new { name = t.Name, group = t.Group ?? "" })
                        .ToList();
                }
            }
            catch (Exception e)
            {
                // 提供方坏了不能拖垮整个 config 接口：跳过它继续走兜底名单。
                logger.LogWarning(e, "[RemoteBroadcast] 教师目录提供方 {Name} 查询失败，回退手动名单", provider.DisplayName);
            }
        }

        source = config.ManualTeachers.Count > 0 ? "白板端手动维护" : "未接入教师目录";
        return config.ManualTeachers
            .Select(t => (object)new { name = t.Name, group = t.Group })
            .ToList();
    }

    /// <summary>学号格式化：按名单总人数决定补零宽度（至少两位），百人名单自然变三位。</summary>
    private static string FormatStudentNo(int index, int total)
    {
        var width = Math.Max(2, total.ToString().Length);
        return (index + 1).ToString().PadLeft(width, '0');
    }

    private async Task HandleQuickCallAsync(HttpListenerContext ctx)
    {
        // R8：同 IP 对呼叫接口限速 2 次/秒，超限 429——防手抖连点，也防有人的「自动化」脚本。
        var ip = ctx.Request.RemoteEndPoint?.Address.ToString() ?? "";
        if (!AllowRequest(ip))
        {
            TryWriteJson(ctx, 429, new { error = "请求太频繁，请稍候再试" });
            return;
        }

        var body = await ReadJsonBodyAsync<QuickCallRequest>(ctx);
        if (body == null)
        {
            return;
        }

        var caller = (body.Caller ?? "").Trim();
        if (caller.Length is < 2 or > 20)
        {
            TryWriteJson(ctx, 400, new { error = "呼叫人姓名需为 2–20 个字符" });
            return;
        }

        // 接洽人随请求下发。三种取值严格区分：
        //   缺省 null = 跟随发起人（老版网页没这个字段，行为与升级前完全一致）；
        //   空串     = 明确选了「不指定」（播报切无接洽人模板）；
        //   非空     = 「找该人」。
        // 合并动作发生在状态机，HTTP 层只做长度护栏——提前合并会把两种语义搅在一起。
        var contact = body.Contact == null ? null : body.Contact.Trim();
        if (contact is { Length: > 20 })
        {
            TryWriteJson(ctx, 400, new { error = "接洽人姓名需为 2–20 个字符或不指定" });
            return;
        }

        // 双模式分叉：mode=custom 走自定义内容广播（标题/正文/时长），缺省仍是学生呼叫，
        // 老版本手机页不带 mode 字段也照常工作——请求体的向后兼容就在这一个判断上。
        if (string.Equals(body.Mode, "custom", StringComparison.OrdinalIgnoreCase))
        {
            await HandleCustomBroadcastCoreAsync(ctx, body, caller);
            return;
        }

        await HandleStudentCallCoreAsync(ctx, body, caller, contact);
    }

    private async Task HandleStudentCallCoreAsync(HttpListenerContext ctx, QuickCallRequest body, string caller, string? contact)
    {
        var destination = (body.Destination ?? "").Trim();
        if (destination.Length is < 1 or > 50)
        {
            TryWriteJson(ctx, 400, new { error = "目的地需为 1–50 个字符" });
            return;
        }

        // 手机端送来的是学生 id（来自 /api/config），这里翻译成名字；翻译不出来的按参数错误处理。
        var rosterMap = store.Snapshot().Roster.ToDictionary(s => s.Id, s => s.Name);
        var studentNames = new List<string>();
        foreach (var id in body.Students ?? [])
        {
            if (!rosterMap.TryGetValue(id ?? "", out var name))
            {
                TryWriteJson(ctx, 400, new { error = "名单中包含未知学生，请刷新页面后重试" });
                return;
            }

            studentNames.Add(name);
        }

        if (studentNames.Count == 0)
        {
            TryWriteJson(ctx, 400, new { error = "请至少选择一名学生" });
            return;
        }

        try
        {
            quickCall.AcceptCall(caller, studentNames, destination, contact, body.LoopCount);
            TryWriteJson(ctx, 202, new { accepted = true, queuePosition = 1 });
        }
        catch (BroadcastBusyException)
        {
            TryWriteJson(ctx, 409, new { error = "有播报正在进行，请稍候" });
        }
        catch (AnnouncementException e)
        {
            TryWriteJson(ctx, 400, new { error = e.Message });
        }
    }

    private async Task HandleCustomBroadcastCoreAsync(HttpListenerContext ctx, QuickCallRequest body, string caller)
    {
        var title = (body.Title ?? "").Trim();
        if (title.Length == 0)
        {
            title = "通知";
        }

        if (title.Length > 30)
        {
            TryWriteJson(ctx, 400, new { error = "通知标题需为 1–30 个字符" });
            return;
        }

        try
        {
            quickCall.AcceptCustomCall(caller, title, body.Body ?? "",
                body.TitleDuration ?? 3, body.BodyDuration, body.LoopCount);
            TryWriteJson(ctx, 202, new { accepted = true, queuePosition = 1 });
        }
        catch (BroadcastBusyException)
        {
            TryWriteJson(ctx, 409, new { error = "有播报正在进行，请稍候" });
        }
        catch (AnnouncementException e)
        {
            TryWriteJson(ctx, 400, new { error = e.Message });
        }
    }

    private void HandleStatus(HttpListenerContext ctx)
    {
        var (state, current, lastError) = quickCall.GetStatus();
        object payload = current == null
            ? new { state = state.ToString(), error = lastError == "" ? null : lastError }
            : new
            {
                state = state.ToString(),
                current = new
                {
                    caller = current.Caller,
                    students = current.Students,
                    destination = current.Destination,
                    contact = current.Contact == "" ? null : current.Contact,
                    startedAt = current.StartedAtUtc,
                },
                error = lastError == "" ? null : lastError,
            };
        TryWriteJson(ctx, 200, payload);
    }

    /// <summary>删除呼叫历史：body 带 ids 只删命中记录，不带或空数组 = 清空全部。</summary>
    private async Task HandleHistoryDeleteAsync(HttpListenerContext ctx)
    {
        var body = await ReadJsonBodyAsync<HistoryDeleteRequest>(ctx);
        if (body == null)
        {
            return;
        }

        var removed = quickCall.RemoveHistory(body.Ids?.ToArray());
        logger.LogInformation("[RemoteBroadcast] 呼叫历史删除 {Count} 条", removed);
        TryWriteJson(ctx, 200, new { removed });
    }

    private void HandleHistory(HttpListenerContext ctx)
    {
        var records = quickCall.GetHistory();
        TryWriteJson(ctx, 200, new
        {
            records = records.Select(r => new
            {
                id = r.Id,
                at = r.AtUtc,
                students = r.Students,
                destination = r.Destination,
                caller = r.Caller,
                contact = r.Contact == "" ? null : r.Contact,
                result = r.Result,
            }),
        });
    }

    /// <summary>资源扩展名 → Content-Type（Vite 产物就这几种，映射表点到为止）。</summary>
    private static readonly Dictionary<string, string> AssetMime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
        [".json"] = "application/json; charset=utf-8",
        [".html"] = "text/html; charset=utf-8",
    };

    /// <summary>
    /// 按浏览器 UA 分流——PC/iPad/平板进 PC 端（独立信息架构），
    /// iPhone/Android 手机进移动端（启动器形态）。?ui=pc|mobile 手动覆盖优先，
    /// 选择落在 URL 上而非 localStorage，扫码链接怎么发都不会分错形态。
    /// </summary>
    private void ServeUi(HttpListenerContext ctx, string subPath)
    {
        var query = ctx.Request.Url?.Query ?? "";
        var overrideParam = System.Web.HttpUtility.ParseQueryString(query)["ui"]?.ToLowerInvariant();
        string page;
        if (overrideParam is "pc" or "mobile")
        {
            page = overrideParam;
        }
        else
        {
            page = IsMobileBrowser(ctx.Request.UserAgent) ? "mobile" : "pc";
        }

        var file = Path.Combine(pluginFolder, "Assets", "wwwroot", page + ".html");
        ServeFileBytes(ctx, file, "text/html; charset=utf-8");
    }

    /// <summary>
    /// 手机浏览器判定：带 Mobile 记号的 UA 是移动端（iPhone、Android 手机、浏览器的移动模式），
    /// 其余——桌面浏览器、iPad、Android 平板——都进 PC 端。iPadOS 13+ 默认请求桌面 UA（"Macintosh"），
    /// 经典 iPad UA 里的 Mobile/15E148 构建号靠 iPad 排除项挡掉；Android 平板的 UA 本就不带 Mobile 记号。
    /// </summary>
    private static bool IsMobileBrowser(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
        {
            return false; // 拿不到 UA 的场景（桌面工具、脚本）按 PC 端处理
        }

        return (userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
                && !userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
            || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPod", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Windows Phone", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Vite 构建产物的静态资源（/assets/*.js|css|…）。路径白名单化：只允许单层文件名，
    /// 反斜杠与「..」直接 404——接入码前缀后的静态面不需要任何目录穿越能力。
    /// </summary>
    private void ServeAsset(HttpListenerContext ctx, string subPath)
    {
        var name = subPath["/assets/".Length..].TrimStart('/');
        if (name.Length == 0 || name.Contains("..") || name.Contains('/') || name.Contains('\\'))
        {
            TryWriteJson(ctx, 404, new { error = "Not Found" });
            return;
        }

        var ext = Path.GetExtension(name);
        if (!AssetMime.TryGetValue(ext, out var mime))
        {
            TryWriteJson(ctx, 404, new { error = "Not Found" });
            return;
        }

        var file = Path.Combine(pluginFolder, "Assets", "wwwroot", "assets", name);
        ServeFileBytes(ctx, file, mime);
    }

    /// <summary>读文件并写响应；资源缺失一律 404（老版本网页被裁掉 assets 目录时不至于吐半截页面）。</summary>
    private void ServeFileBytes(HttpListenerContext ctx, string file, string mime)
    {
        if (!File.Exists(file))
        {
            TryWriteJson(ctx, 404, new { error = "Not Found" });
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(file);
            ctx.Response.ContentType = mime;
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes);
            ctx.Response.OutputStream.Close();
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "[RemoteBroadcast] 静态资源读取失败：{File}", file);
            TryWriteJson(ctx, 404, new { error = "Not Found" });
        }
    }

    // ---- 小工具 ----

    private string? GetCodeSegment(HttpListenerContext ctx)
    {
        var segments = (ctx.Request.Url?.AbsolutePath ?? "/").Trim('/').Split('/');
        return segments.Length > 0 ? segments[0] : null;
    }

    private async Task<T?> ReadJsonBodyAsync<T>(HttpListenerContext ctx) where T : class
    {
        try
        {
            // 64KB 上限：手机端这点 JSON 用不满，设上限纯粹是防呆。
            if (ctx.Request.ContentLength64 is < 0 or > 64 * 1024)
            {
                TryWriteJson(ctx, 400, new { error = "请求体不合法" });
                return null;
            }

            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            var json = await reader.ReadToEndAsync();
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception)
        {
            TryWriteJson(ctx, 400, new { error = "请求体不是合法的 JSON" });
            return null;
        }
    }

    private bool AllowRequest(string ip)
    {
        var now = DateTime.UtcNow;
        var allowed = true;
        _rateLimits.AddOrUpdate(ip,
            _ => (now, 1),
            (_, entry) =>
            {
                // 窗口 1 秒、上限 2 次（R8）。到点重开窗，窗口内超了就拒。
                if ((now - entry.Start).TotalSeconds >= 1)
                {
                    return (now, 1);
                }

                entry.Count++;
                allowed = entry.Count <= 2;
                return entry;
            });
        return allowed;
    }

    private void TryWriteJson(HttpListenerContext ctx, int status, object payload)
    {
        try
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes);
            ctx.Response.OutputStream.Close();
        }
        catch (Exception e)
        {
            // 手机端中途断开（扫一半锁屏）是日常，写不回去就算了。
            logger.LogDebug(e, "[RemoteBroadcast] 响应写入失败（客户端可能已断开）");
        }
    }

    private sealed record RegisterRequest(string? TeacherName);

    private sealed record HistoryDeleteRequest(List<Guid>? Ids);

    // 学生呼叫与自定义广播共用一个请求体：mode 缺省 = 学生呼叫（Students/Destination），
    // mode=custom 时取 Title/Body/TitleDuration/BodyDuration；LoopCount 两种模式都可带（缺省跟白板端配置）。
    // Contact 三态：缺省 null = 跟随发起人（老版网页兼容）；空串 = 「不指定」；非空 = 「找该人」。
    private sealed record QuickCallRequest(
        List<string>? Students, string? Destination, string? Caller,
        string? Mode, string? Title, string? Body,
        int? TitleDuration, int? BodyDuration, int? LoopCount, string? Contact);
}
