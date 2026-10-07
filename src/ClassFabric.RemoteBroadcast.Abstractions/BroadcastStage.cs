namespace ClassFabric.RemoteBroadcast.Abstractions;

/// <summary>
/// 一条播报的生命周期阶段。网页端的实时状态流（WebSocket）与插件间事件用的是同一套阶段，
/// 一处状态机两路分发——网页看到的和插件收到的永远是同一个事实，不会各说各话。
/// </summary>
public enum BroadcastStage
{
    /// <summary>已受理：参数校验通过，进入播报队列。</summary>
    Accepted,

    /// <summary>已上屏：遮罩（标题）出现在大屏上。</summary>
    Displayed,

    /// <summary>语音起：话音开始播（纯视觉播报不会出现这个阶段）。</summary>
    SpeechStarted,

    /// <summary>已完成：整条播报自然播满、画面正常收起。</summary>
    Completed,

    /// <summary>失败：合成或链路出错，原因见 <see cref="BroadcastStateChangedEventArgs.Message"/>。</summary>
    Failed,

    /// <summary>已取消：被外部主动中止（网页/插件调用取消，或白板服务停用）。</summary>
    Cancelled,
}

/// <summary>
/// 阶段变化的完整载荷。带上发起人、名单、目的地，订阅方不用再回查一次接口就能直接渲染。
/// </summary>
public sealed record BroadcastStateChangedEventArgs
{
    /// <summary>当前阶段。</summary>
    public required BroadcastStage Stage { get; init; }

    /// <summary>发起人（登记的教师本人）。</summary>
    public string Caller { get; init; } = "";

    /// <summary>被呼叫的学生名单；自定义广播为空。</summary>
    public IReadOnlyList<string> Students { get; init; } = [];

    /// <summary>目的地；自定义广播时记标题。</summary>
    public string Destination { get; init; } = "";

    /// <summary>
    /// 接洽人：和发起人（{Caller}）是两个角色，这里记正文里「找谁」。
    /// 空串 = 未指定接洽人；自定义广播恒为空串。
    /// </summary>
    public string Contact { get; init; } = "";

    /// <summary>阶段发生时刻（UTC）。</summary>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>失败/取消时的可读原因；其余阶段为 null。</summary>
    public string? Message { get; init; }
}
