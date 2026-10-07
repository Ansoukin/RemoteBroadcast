using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassFabric.RemoteBroadcast.Abstractions;
using Microsoft.Extensions.Logging;

namespace ClassFabric.RemoteBroadcast.Services;

/// <summary>
/// P-A 实时状态流：手机网页连 /ws 订阅播报阶段，替代 2 秒轮询（HTTP 轮询保留为降级路径）。
/// 阶段信号直接转接状态机的那一路 BroadcastStateChanged——与插件间 API 事件同源，
/// 网页看到的和插件收到的永远是同一条时间线。所有路由都在接入码前缀下，
/// WebSocket 升级前已验码，天然鉴权，不暴露「这里有个服务」的存在感。
/// </summary>
public class BroadcastStatusStream : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly QuickCallService _quickCall;
    private readonly ILogger<BroadcastStatusStream> _logger;
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = [];

    public BroadcastStatusStream(QuickCallService quickCall, ILogger<BroadcastStatusStream> logger)
    {
        _quickCall = quickCall;
        _logger = logger;
        // 服务是单例、随插件活一辈子，订阅挂上就不再退（Dispose 只是形式上的对齐）。
        quickCall.BroadcastStateChanged += OnStageChanged;
    }

    public void Dispose() => _quickCall.BroadcastStateChanged -= OnStageChanged;

    /// <summary>
    /// 处理一次 WebSocket 连接：升级 → 推「现状」快照 → 挂住收包直到对方断开。
    /// 客户端发来的消息一概不处理——这是单向推送流，保活由协议层心跳自己负责。
    /// </summary>
    public async Task HandleAsync(HttpListenerContext ctx, CancellationToken serviceToken)
    {
        WebSocket socket;
        try
        {
            // HttpListener 的升级返回的是上下文，真身在 .WebSocket 里。
            socket = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
        }
        catch (Exception e)
        {
            // 升级失败（客户端不支持等）：400 收尾，别留半开的响应。
            _logger.LogDebug(e, "[RemoteBroadcast] WebSocket 升级失败");
            try
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.Close();
            }
            catch
            {
                // 收尾失败不追究。
            }

            return;
        }

        var id = Guid.NewGuid();
        _clients[id] = socket;
        _logger.LogInformation("[RemoteBroadcast] 实时状态流接入（在线 {Count} 路）", _clients.Count);

        try
        {
            // 连接即推一份「现状」：新开的页面不用等下一次事件才知道白板此刻在干什么。
            var hello = JsonSerializer.Serialize(BuildHelloPayload(), JsonOptions);
            var helloBytes = Encoding.UTF8.GetBytes(hello);
            await socket.SendAsync(helloBytes, WebSocketMessageType.Text, true, serviceToken);

            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open && !serviceToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), serviceToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 服务停了，正常收摊。
        }
        catch (Exception)
        {
            // 刷页面、网络抖动导致的断线是常态，安静清理即可。
        }
        finally
        {
            _clients.TryRemove(id, out _);
            _logger.LogInformation("[RemoteBroadcast] 实时状态流断开（在线 {Count} 路）", _clients.Count);
        }
    }

    /// <summary>
    /// 服务停/重启时收摊：把在线客户端礼貌关掉。页面侧检测到断开会自己回退轮询并重连。
    /// </summary>
    public void CloseAll()
    {
        foreach (var (_, socket) in _clients)
        {
            try
            {
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    _ = socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "服务重启", CancellationToken.None);
                }
            }
            catch
            {
                // 关不动的直接丢， socket 会随 GC 与超时自愈。
            }
        }

        _clients.Clear();
    }

    private void OnStageChanged(object? sender, BroadcastStateChangedEventArgs e)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "stage",
            stage = Camel(e.Stage.ToString()),
            caller = e.Caller,
            students = e.Students,
            destination = e.Destination,
            contact = e.Contact,
            message = e.Message,
            timestampUtc = e.TimestampUtc,
        }, JsonOptions);
        _ = PushToAllAsync(json);
    }

    private async Task PushToAllAsync(string json)
    {
        foreach (var (id, socket) in _clients)
        {
            if (socket.State != WebSocketState.Open)
            {
                continue;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception)
            {
                // 发不出去就是已经断了：移除等重连，不给主链路添堵。
                _clients.TryRemove(id, out _);
            }
        }
    }

    private object BuildHelloPayload()
    {
        var (state, current, lastError) = _quickCall.GetStatus();
        return new
        {
            type = "hello",
            state = state.ToString(),
            stage = Camel(_quickCall.CurrentStage.ToString()),
            error = lastError == "" ? null : lastError,
            current = current == null
                ? null
                : new
                {
                    caller = current.Caller,
                    students = current.Students,
                    destination = current.Destination,
                    contact = current.Contact,
                    startedAt = current.StartedAtUtc,
                },
        };
    }

    private static string Camel(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
