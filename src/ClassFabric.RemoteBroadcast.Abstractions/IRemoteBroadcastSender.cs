namespace ClassFabric.RemoteBroadcast.Abstractions;

/// <summary>
/// 发送类接口：让其它插件用代码发起一次播报，等价于老师在手机网页上点「立即呼叫」。
/// 实现方在内部完成校验与排队，不抛业务异常——忙碌、参数非法都通过 <see cref="BroadcastRequestResult"/> 回报，
/// 插件的调用点不用为了「可能被拒」写一层 try/catch。
/// </summary>
public interface IRemoteBroadcastSender
{
    /// <summary>发起一次学生呼叫。</summary>
    Task<BroadcastRequestResult> SendQuickCallAsync(QuickCallRequest request, CancellationToken cancellationToken = default);

    /// <summary>发起一次自定义内容广播（标题走遮罩，正文走悬浮条）。</summary>
    Task<BroadcastRequestResult> SendCustomAsync(CustomBroadcastRequest request, CancellationToken cancellationToken = default);

    /// <summary>中止当前正在进行的播报；没有播报在进行时返回 false。</summary>
    Task<bool> CancelCurrentAsync(CancellationToken cancellationToken = default);
}

/// <summary>学生呼叫的请求参数。</summary>
public sealed record QuickCallRequest
{
    /// <summary>发起人（登记教师本人）。用于遮罩「{Caller} 正在呼叫」与语音提示。</summary>
    public required string Caller { get; init; }

    /// <summary>被呼叫的学生名单，至少一人。</summary>
    public required IReadOnlyList<string> Students { get; init; }

    /// <summary>目的地（如「年级组」）。</summary>
    public required string Destination { get; init; }

    /// <summary>接洽人。留空表示就是发起人本人；该字段随网页端「接洽人」语义拆分同批启用。</summary>
    public string? Contact { get; init; }

    /// <summary>播报循环遍数；留空跟随白板端配置。</summary>
    public int? LoopCount { get; init; }
}

/// <summary>自定义内容广播的请求参数。</summary>
public sealed record CustomBroadcastRequest
{
    /// <summary>发起人（登记教师本人）。</summary>
    public required string Caller { get; init; }

    /// <summary>标题：遮罩阶段展示的那一行。</summary>
    public required string Title { get; init; }

    /// <summary>正文：悬浮条阶段展示并朗读的内容。</summary>
    public required string Body { get; init; }

    /// <summary>标题展示秒数。</summary>
    public int TitleSeconds { get; init; } = 3;

    /// <summary>正文展示秒数；留空表示按语音时长自动决定。</summary>
    public int? BodySeconds { get; init; }

    /// <summary>播报循环遍数；留空跟随白板端配置。</summary>
    public int? LoopCount { get; init; }
}

/// <summary>播报受理结果。被拒时 <see cref="RejectReason"/> 是可以直接展示给老师的人话。</summary>
public sealed record BroadcastRequestResult
{
    /// <summary>是否已受理。</summary>
    public required bool Accepted { get; init; }

    /// <summary>被拒原因（忙碌/参数非法/服务未启用）；受理成功时为 null。</summary>
    public string? RejectReason { get; init; }

    /// <summary>实际将播报的文本（便于调用方回显核对）；被拒时为 null。</summary>
    public string? SpokenText { get; init; }

    public static BroadcastRequestResult Reject(string reason) => new() { Accepted = false, RejectReason = reason };

    public static BroadcastRequestResult Ok(string spokenText) => new() { Accepted = true, SpokenText = spokenText };
}
