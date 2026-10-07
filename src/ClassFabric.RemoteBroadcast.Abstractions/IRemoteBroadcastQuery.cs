namespace ClassFabric.RemoteBroadcast.Abstractions;

/// <summary>
/// 查询类接口：状态、当前播报、呼叫历史。全部是同步快照读取——
/// 数据都在内存里，异步包装只会让调用点多写一个 await。
/// </summary>
public interface IRemoteBroadcastQuery
{
    /// <summary>服务状态：绑定模式、端口、局域网地址与降级原因。</summary>
    ServiceStatusInfo GetServiceStatus();

    /// <summary>当前正在进行的播报；空闲时为 null。</summary>
    CurrentBroadcastInfo? GetCurrentBroadcast();

    /// <summary>呼叫历史，最新在前。</summary>
    IReadOnlyList<CallHistoryEntry> GetHistory();
}

/// <summary>服务绑定模式。</summary>
public enum ServiceBindMode
{
    /// <summary>未运行。</summary>
    Stopped,

    /// <summary>降级运行：仅本机可访问（通常是缺 urlacl 授权）。</summary>
    LocalOnly,

    /// <summary>正常运行：局域网可访问。</summary>
    Lan,
}

/// <summary>服务状态快照。</summary>
public sealed record ServiceStatusInfo
{
    /// <summary>绑定模式。</summary>
    public required ServiceBindMode BindMode { get; init; }

    /// <summary>监听端口。</summary>
    public int Port { get; init; }

    /// <summary>局域网访问地址（形如 http://192.168.1.10:5212/）；不可用时为 null。</summary>
    public string? LanAddress { get; init; }

    /// <summary>降级/失败时的人话说明；正常运行时为 null。</summary>
    public string? Hint { get; init; }

    /// <summary>是否因为缺少 urlacl 授权而降级（设置页据此亮出「一键授权」）。</summary>
    public bool NeedsUrlAcl { get; init; }
}

/// <summary>当前播报的快照。</summary>
public sealed record CurrentBroadcastInfo
{
    /// <summary>发起人。</summary>
    public required string Caller { get; init; }

    /// <summary>被呼叫的学生名单；自定义广播为空。</summary>
    public IReadOnlyList<string> Students { get; init; } = [];

    /// <summary>目的地；自定义广播时记标题。</summary>
    public string Destination { get; init; } = "";

    /// <summary>接洽人；空串 = 未指定。</summary>
    public string Contact { get; init; } = "";

    /// <summary>开始时刻（UTC）。</summary>
    public DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>最近一次阶段。</summary>
    public BroadcastStage Stage { get; init; }
}

/// <summary>一条呼叫历史。</summary>
public sealed record CallHistoryEntry
{
    /// <summary>发生时刻（UTC）。</summary>
    public DateTimeOffset AtUtc { get; init; }

    /// <summary>被呼叫的学生名单；自定义广播为空。</summary>
    public IReadOnlyList<string> Students { get; init; } = [];

    /// <summary>目的地；自定义广播时记标题。</summary>
    public string Destination { get; init; } = "";

    /// <summary>发起人。</summary>
    public string Caller { get; init; } = "";

    /// <summary>接洽人（3.3.1）；空串 = 未指定，网页端显示为「不指定」。</summary>
    public string Contact { get; init; } = "";

    /// <summary>结果文案（已完成 / 已取消 / 失败原因）。</summary>
    public string Result { get; init; } = "";
}
