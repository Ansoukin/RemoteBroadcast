using ClassFabric.RemoteBroadcast.Abstractions;

namespace ClassFabric.RemoteBroadcast.Services;

/// <summary>
/// 插件间 API 的实现：把播报状态机、状态查询与阶段事件包成契约接口，给别的插件用。
/// 三类接口共用同一个实例——消费方解析到哪一个，拿到的都是同一份事实与同一条事件流。
///
/// 刻意不做的事：接入码管理（那等于把广播发起权外借）、学生名单与插件配置的写操作。
/// 管理面收口在设置页与受接入码鉴权的手机接口，插件间只开放「发起播报 + 查询 + 订阅」。
/// </summary>
public class RemoteBroadcastApi(
    ConfigStore store,
    BroadcastHttpServer server,
    QuickCallService quickCall)
    : IRemoteBroadcastSender, IRemoteBroadcastQuery, IRemoteBroadcastEvents
{
    /// <summary>
    /// 阶段事件直接转接状态机的那一路信号：不改阶段、不重排顺序、不补发，
    /// 插件订阅者与网页实时状态流看到的就是同一条时间线。
    /// </summary>
    public event EventHandler<BroadcastStateChangedEventArgs>? BroadcastStateChanged
    {
        add => quickCall.BroadcastStateChanged += value;
        remove => quickCall.BroadcastStateChanged -= value;
    }

    // ---- 发送类 ----

    /// <inheritdoc />
    public Task<BroadcastRequestResult> SendQuickCallAsync(
        QuickCallRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Caller))
        {
            return Task.FromResult(BroadcastRequestResult.Reject("发起人不能为空。"));
        }

        if (request.Students.Count == 0)
        {
            return Task.FromResult(BroadcastRequestResult.Reject("至少选择一名学生。"));
        }

        if (request.Students.Any(string.IsNullOrWhiteSpace))
        {
            return Task.FromResult(BroadcastRequestResult.Reject("学生名单里有空名字，请检查后再发。"));
        }

        if (string.IsNullOrWhiteSpace(request.Destination))
        {
            return Task.FromResult(BroadcastRequestResult.Reject("目的地不能为空。"));
        }

        // 接洽人正式生效：非空 =「找该人」；null/空 = 跟发起人（老行为）。
        // 「不指定」语义（切无接洽人模板对）不向插件 API 开放——契约上缺省即跟发起人，网页端独有。
        // （if 块不再需要：合并语义在状态机统一裁决，此前「收下不消费」的调试日志随之下线。）
        try
        {
            var announcement = quickCall.AcceptCall(
                request.Caller, [.. request.Students], request.Destination,
                string.IsNullOrWhiteSpace(request.Contact) ? null : request.Contact.Trim(),
                request.LoopCount);
            return Task.FromResult(BroadcastRequestResult.Ok(announcement.SpeechText));
        }
        catch (BroadcastBusyException)
        {
            return Task.FromResult(BroadcastRequestResult.Reject("白板正在播报，请等这一次播完再发。"));
        }
        catch (ArgumentException e)
        {
            // 参数类问题（超长、个数越界等）由状态机抛出来，原样转成可读拒因。
            return Task.FromResult(BroadcastRequestResult.Reject(e.Message));
        }
    }

    /// <inheritdoc />
    public Task<BroadcastRequestResult> SendCustomAsync(
        CustomBroadcastRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Caller))
        {
            return Task.FromResult(BroadcastRequestResult.Reject("发起人不能为空。"));
        }

        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length < 2)
        {
            return Task.FromResult(BroadcastRequestResult.Reject("正文至少要有两个字。"));
        }

        try
        {
            // 标题留空时用「通知」兜底：遮罩上总得有个东西，不然那三秒是一片空白。
            var title = string.IsNullOrWhiteSpace(request.Title) ? "通知" : request.Title;
            var announcement = quickCall.AcceptCustomCall(
                request.Caller, title, request.Body, request.TitleSeconds, request.BodySeconds, request.LoopCount);
            return Task.FromResult(BroadcastRequestResult.Ok(announcement.SpeechText));
        }
        catch (BroadcastBusyException)
        {
            return Task.FromResult(BroadcastRequestResult.Reject("白板正在播报，请等这一次播完再发。"));
        }
        catch (ArgumentException e)
        {
            return Task.FromResult(BroadcastRequestResult.Reject(e.Message));
        }
    }

    /// <inheritdoc />
    public Task<bool> CancelCurrentAsync(CancellationToken cancellationToken = default)
    {
        var (state, _, _) = quickCall.GetStatus();
        if (state != BroadcastState.Broadcasting)
        {
            return Task.FromResult(false);
        }

        // 状态机自己会把取消落到历史与阶段事件上，这里只负责发令。
        quickCall.Stop();
        return Task.FromResult(true);
    }

    // ---- 查询类 ----

    /// <inheritdoc />
    public ServiceStatusInfo GetServiceStatus()
    {
        var config = store.Snapshot();
        var address = LanUtils.PickPreferredAddress(LanUtils.GetLanAddresses(), config.PreferredLanAddress);
        var mode = server.BindMode switch
        {
            "Lan" => ServiceBindMode.Lan,
            "LocalOnly" => ServiceBindMode.LocalOnly,
            _ => ServiceBindMode.Stopped,
        };

        return new ServiceStatusInfo
        {
            BindMode = mode,
            Port = server.BoundPort,
            // 只有真正局域网可达时才给地址：降级到仅本机时给个 IP 反而会误导调用方去连。
            LanAddress = mode == ServiceBindMode.Lan && address != null
                ? $"http://{address}:{server.BoundPort}/"
                : null,
            Hint = server.BindHint,
            NeedsUrlAcl = server.NeedsUrlAcl,
        };
    }

    /// <inheritdoc />
    public CurrentBroadcastInfo? GetCurrentBroadcast()
    {
        var (state, current, _) = quickCall.GetStatus();
        if (state != BroadcastState.Broadcasting || current == null)
        {
            return null;
        }

        return new CurrentBroadcastInfo
        {
            Caller = current.Caller,
            Students = current.Students,
            Destination = current.Destination,
            Contact = current.Contact,
            StartedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(current.StartedAtUtc, DateTimeKind.Utc)),
            Stage = quickCall.CurrentStage,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<CallHistoryEntry> GetHistory()
    {
        return quickCall.GetHistory()
            .Select(record => new CallHistoryEntry
            {
                AtUtc = new DateTimeOffset(DateTime.SpecifyKind(record.AtUtc, DateTimeKind.Utc)),
                Students = record.Students,
                Destination = record.Destination,
                Caller = record.Caller,
                Contact = record.Contact,
                Result = record.Result,
            })
            .ToList();
    }
}
