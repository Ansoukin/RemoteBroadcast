using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassFabric.RemoteBroadcast.Abstractions;
using ClassFabric.RemoteBroadcast.Models;
using ClassFabric.RemoteBroadcast.UI;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Notification;
using Microsoft.Extensions.Logging;

namespace ClassFabric.RemoteBroadcast.Services;

/// <summary>有播报正在进行，新呼叫被拒（映射 HTTP 409）。</summary>
public class BroadcastBusyException : Exception;

public enum BroadcastState
{
    Idle,
    Broadcasting,
    Error,
}

/// <summary>呼叫记录：内存环形 20 条，重启即清、不落盘（呼叫数据不出白板）。
/// Contact 是接洽人字段：空 = 不指定；和发起人是两个角色，历史记录分两栏。
/// Id 是本运行期唯一的随机标识，网页端删除接口按它定位记录——重启后历史清空，Id 随之失效。</summary>
public sealed record CallRecord(Guid Id, DateTime AtUtc, List<string> Students, string Destination, string Caller, string Contact, string Result);

/// <summary>/api/status 的 current 段。</summary>
public sealed record CurrentBroadcast(string Caller, List<string> Students, string Destination, string Contact, DateTime StartedAtUtc);

/// <summary>
/// Quick Call 状态机：同一时刻只允许一个播报任务（R8）。
/// 一条完整链路 = 先合成 → 通知先行（Mask 3s → Overlay 与语音同起）→ Overlay 按语音时长自然收起。
/// 失败不外抛给 HTTP（那时早已 202），而是写进状态机，让手机端轮询 /api/status 拿到可读错误（验收 5）。
/// </summary>
public class QuickCallService(
    ConfigStore store,
    EdgeTtsClient tts,
    IAudioService audio,
    QuickCallNotificationProvider provider,
    ILogger<QuickCallService> logger)
{
    private const int HistoryCapacity = 20;
    private static readonly TimeSpan ErrorDisplayWindow = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OverlayHardCap = TimeSpan.FromSeconds(90);

    private readonly object _lock = new();
    private BroadcastState _state = BroadcastState.Idle;
    private CurrentBroadcast? _current;
    private string _lastError = "";
    private DateTime _errorSinceUtc;
    private readonly List<CallRecord> _history = [];
    private CancellationTokenSource? _playbackCts;
    private BroadcastStage _currentStage = BroadcastStage.Accepted;

    /// <summary>
    /// 播报阶段变化。网页端的实时状态流（WebSocket）与插件间 API 事件都挂在这一路信号上——
    /// 一套状态机两路分发，网页看到的和插件收到的永远是同一个事实。
    /// 事件在后台线程触发，订阅方要碰界面得自己切回界面线程。
    /// </summary>
    public event EventHandler<BroadcastStateChangedEventArgs>? BroadcastStateChanged;

    /// <summary>当前阶段，供查询接口回显。</summary>
    public BroadcastStage CurrentStage => _currentStage;

    /// <summary>
    /// 受理一次呼叫：参数校验与播报文本构造（失败=400）→ 忙碌检查（失败=409）→
    /// 置为播报中并立刻返回，后续链路在后台跑。
    /// loopCount 传 null 时跟随白板端配置——手机端想临时多念几遍就带上显式值。
    /// contact 是接洽人：null = 跟随发起人（老版网页/插件 API 缺省），"" = 不指定，
    /// 其它值 = 「找该人」；为空时播报构造自动切到无接洽人模板对。
    /// </summary>
    public AnnouncementResult AcceptCall(string caller, List<string> studentNames, string destination, string? contact = null, int? loopCount = null)
    {
        var config = store.Snapshot();
        var announcement = AnnouncementComposer.Compose(
            studentNames, destination, caller, contact,
            config.TemplateSingle, config.TemplateMultiGuide,
            config.TemplateSingleNoContact, config.TemplateMultiGuideNoContact,
            loopCount ?? config.AnnouncementLoopCount);

        lock (_lock)
        {
            if (_state == BroadcastState.Broadcasting)
            {
                throw new BroadcastBusyException();
            }

            _state = BroadcastState.Broadcasting;
            // null 在模板层已按「跟发起人」补齐，这里存进当前播报与历史的是补齐后的名字——
            // 历史页「接洽人」栏因此能如实显示「找的是谁」，空串只留给明确选的「不指定」。
            _current = new CurrentBroadcast(caller, studentNames, destination, contact ?? caller, DateTime.UtcNow);
            _lastError = "";
        }

        RaiseStage(BroadcastStage.Accepted);
        _ = RunAnnouncementAsync(announcement, caller, studentNames, destination, contact ?? caller,
            $"{caller} 正在呼叫", TimeSpan.FromSeconds(3));
        return announcement;
    }

    /// <summary>
    /// 受理一次自定义内容广播：标题走 Mask（时长 1–15 秒），正文走 Overlay。
    /// Overlay 时长默认仍按「语音时长 + 2 秒」收尾；老师手动指定了展示时长就以其为准——
    /// 这时语音可能比画面长而被宿主截断，这是「时长优先」语义的自觉代价，不是缺陷。
    /// 历史记录里自定义广播的「目的地」栏记标题、名单留空，方便和呼叫记录一眼区分。
    /// </summary>
    public AnnouncementResult AcceptCustomCall(string caller, string title, string body,
        int titleSeconds, int? bodySeconds, int? loopCount = null)
    {
        var config = store.Snapshot();
        var announcement = AnnouncementComposer.ComposeCustom(body, loopCount ?? config.AnnouncementLoopCount);

        lock (_lock)
        {
            if (_state == BroadcastState.Broadcasting)
            {
                throw new BroadcastBusyException();
            }

            _state = BroadcastState.Broadcasting;
            // 自定义广播无「找某人」语义，接洽人栏落空串（网页历史页显示「不指定」）。
            _current = new CurrentBroadcast(caller, [], title, "", DateTime.UtcNow);
            _lastError = "";
        }

        RaiseStage(BroadcastStage.Accepted);
        var maskDuration = TimeSpan.FromSeconds(Math.Clamp(titleSeconds, 1, 15));
        TimeSpan? overlayOverride = bodySeconds is null
            ? null
            : TimeSpan.FromSeconds(Math.Clamp(bodySeconds.Value, 3, 60));
        _ = RunAnnouncementAsync(announcement, caller, [], title, "",
            title, maskDuration, overlayOverride);
        return announcement;
    }

    private async Task RunAnnouncementAsync(
        AnnouncementResult announcement, string caller, List<string> students, string destination, string contact,
        string maskText, TimeSpan maskDuration, TimeSpan? overlayDurationOverride = null)
    {
        var config = store.Snapshot();

        // 总保险丝：哪怕音频设备卡死，95 秒后状态机也必须归位，否则整个插件从此 409 到天荒地老。
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(95));
        _playbackCts = cts;
        NotificationRequest? request = null;
        try
        {
            // ====== V0.1.1 性能专项重排（方案 O1/O2/O6 + 插件端专项「语音总开关/单链 EdgeTTS」）======
            // 真机两轮教训沉淀（写在最显眼处，别再踩第三次）：
            //   1) 「Mask 请求 + Overlay 请求」两段式行不通。宿主 MainWindowLine 把正文的显示逻辑整个嵌在
            //      `if (request.MaskContent.Duration > TimeSpan.Zero)` 分支里，而且 NotificationRequest.MaskContent
            //      的默认值是 NotificationContent.Empty（自带 5 秒时长）：纯 Overlay 请求会先顶着一张没有内容的
            //      空遮罩干等 5 秒（真机日志里的 "MaskContent.Content ... Value is null" 就是它），把 Mask 时长
            //      真设成 0 则连正文一起不显示。Mask 与 Overlay 必须同属一个请求。
            //   2) 正常路径绝不 Cancel 请求：宿主 CompletedToken 只在自然播满时触发，Cancel 会拖出僵尸票据
            //      把提醒通道挂死（这个坑踩过两次了）。
            //   3) 绝不把空流塞进播放列表：空 MemoryStream 喂给 MiniAudio 会抛 InvalidFile，整条播报被自己打死。
            // 于是时序是：受理即上屏（Mask 与 Overlay 一次提交）→ 合成两路并行 → 合成回来趁 Mask 还没播完把
            // Overlay 的真实时长校正进去（宿主是正文会话开始时才读 Duration，这个窗口期正好够）→
            // Mask 播满后正文与语音同起。
            var speechOn = config.SpeechEnabled;

            // 合成没回来之前先按「字数 ÷ 语速」估一个时长把请求发出去——画面不能等合成（O1 的全部意义）。
            var provisionalSeconds = EstimateSpeechSeconds(announcement.VisualText);
            var provisionalDuration = overlayDurationOverride
                ?? TimeSpan.FromSeconds(Math.Min(provisionalSeconds + 2, OverlayHardCap.TotalSeconds));
            provisionalDuration = TimeSpan.FromMinutes(Math.Min(provisionalDuration.TotalMinutes, OverlayHardCap.TotalMinutes));

            var barWidth = await Dispatcher.UIThread.InvokeAsync(() => ResolveOverlayBarWidth(config));
            // 宽度是「取大」语义，调了没变化时先看这行：实际生效值和当时的模式一目了然。
            logger.LogInformation("[RemoteBroadcast] 播报条宽度：{Width:F0}px（{Mode}）",
                barWidth, config.IsCustomOverlayWidthEnabled ? "自定义" : "跟随岛条+屏宽兜底");
            NotificationContent? overlayContent = null;
            request = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 正文内容在提交时就要建好（宿主不支持事后替换 Content），滚动周期先按估时给，
                // 合成回来再连 Duration 一起校正（正文要到 Mask 播完才布局，这个窗口期改得动）。
                overlayContent = new NotificationContent
                {
                    Content = new QuickCallOverlayContent(
                        announcement.VisualText, provisionalDuration, config.AnnouncementLoopCount, barWidth),
                    // 单链 EdgeTTS：话音只从插件自己的合成链出，宿主代念通道一律不喂内容。
                    SpeechContent = "",
                    IsSpeechEnabled = false,
                    Duration = provisionalDuration,
                };
                return new NotificationRequest
                {
                    // Mask 不走宿主的 CreateTwoIconsMask：那个模板不给宽度入口，「正在呼叫」要跟着加宽，
                    // 不然正文一上来岛条突然变宽，肉眼可见地跳一下。maskText 由调用方给定：
                    // 学生呼叫报「谁在呼叫」，自定义广播报标题。
                    MaskContent = new NotificationContent
                    {
                        Content = new QuickCallMaskContent(maskText, barWidth),
                        SpeechContent = "",
                        IsSpeechEnabled = false,
                        Duration = maskDuration,
                    },
                    OverlayContent = overlayContent,
                };
            });
            var submittedAt = DateTime.UtcNow;
            var showTask = provider.ShowNotificationAsync(request);
            logger.LogInformation("[RemoteBroadcast] 播报已上屏（{Mask}），语音合成并行启动", maskText);
            // 「上屏」阶段的诚实口径：请求已提交、画面随即出现（宿主接管约百毫秒级），
            // 宿主没有给「像素真的亮了」的回调，这里以提交时刻为准。
            RaiseStage(BroadcastStage.Displayed);

            // -- 合成两路并行（O2）。语音总开关关闭时直接走纯视觉快路径，一个字节都不合成。
            //    只把真正合成出来的段放进播放列表——提示音关掉时不留空占位（教训 3）。
            List<Stream> segments = [];
            if (speechOn)
            {
                try
                {
                    var synthesisTasks = new List<Task<Stream>>();
                    if (config.SpeechHintEnabled)
                    {
                        synthesisTasks.Add(tts.SynthesizeAsync(
                            AnnouncementComposer.FillHint(config.TemplateSpeechHint, caller), config.VoiceName, cts.Token));
                    }
                    synthesisTasks.Add(tts.SynthesizeAsync(announcement.SpeechText, config.VoiceName, cts.Token));

                    var synthesized = await Task.WhenAll(synthesisTasks);
                    segments = synthesized.Where(s => !s.CanSeek || s.Length > 0).ToList();

                    // 48kbps 的 MP3 每秒约 6000 字节，拿字节数反推时长够用了——给 Overlay 收起留余量的。
                    // 老师手动指定了展示时长（自定义广播）就以指定值为准，不再校正。
                    var speechSeconds = segments.Sum(s => s.CanSeek ? s.Length : 0) / 6000.0;
                    if (overlayDurationOverride == null && speechSeconds > 0)
                    {
                        var realDuration = TimeSpan.FromSeconds(Math.Min(speechSeconds + 2, OverlayHardCap.TotalSeconds));
                        var visual = overlayContent!.Content as QuickCallOverlayContent;
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            overlayContent!.Duration = realDuration;
                            visual?.UpdateScrollDuration(realDuration);
                        });
                        logger.LogInformation("[RemoteBroadcast] 正文时长已按语音校正为 {Seconds:F1}s", realDuration.TotalSeconds);
                    }
                }
                catch (SpeechSynthesisException e)
                {
                    // O6 失败快速路径：合成挂了画面也不能没有——已上屏的正文按估时正常走完，
                    // 只是没声音；错误照旧回手机端。绝不因为没合成出来就把提醒取消掉。
                    logger.LogWarning("[RemoteBroadcast] 语音合成失败，降级为纯视觉播报：{Message}", e.Message);
                    segments = [];
                }
            }

            // -- 语音与正文同起：等 Mask 那 3 秒播完再开口。
            //    队列繁忙时宿主可能晚于这 3 秒才轮到本请求，会有些许错位——教室里白板不会同时被别的提醒抢。
            var remainingMask = maskDuration - (DateTime.UtcNow - submittedAt);
            if (remainingMask > TimeSpan.Zero)
            {
                await Task.Delay(remainingMask, cts.Token);
            }

            if (segments.Count > 0)
            {
                RaiseStage(BroadcastStage.SpeechStarted);
            }

            foreach (var segment in segments)
            {
                try
                {
                    await audio.PlayAudioAsync(segment, config.AnnouncementVolume, cts.Token);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // 音频设备/解码器出问题只让这一段哑掉，不能把已经上屏的播报一起带走（教训 3 的另一半）。
                    logger.LogError(e, "[RemoteBroadcast] 音频播放失败，本段跳过");
                }
                finally
                {
                    segment.Dispose();
                }
            }

            // 正文播满 Duration 后宿主触发 CompletedToken，这里才会返回。保险丝兜住宿主侧的意外
            // （比如提醒被别的通知反复打断重排），超时也得让状态机归位，绝不能 409 到天荒地老。
            await showTask.WaitAsync(cts.Token);

            AddHistory(students, destination, caller, contact, "已完成");
            // 先把「完成」发出去再清当前播报——反过来的话事件载荷里的名单/目的地已经空了。
            RaiseStage(BroadcastStage.Completed);
            ResetIfBroadcasting();
            logger.LogInformation("[RemoteBroadcast] 播报完成（{Caller} → {Destination}，{Count} 人）", caller, destination, students.Count);
        }
        catch (SpeechSynthesisException e)
        {
            // 合成异常若仍发生（理论上 O6 已兜住 TTS 失败），按旧口径落历史、进错误态。
            AddHistory(students, destination, caller, contact, "失败：" + e.Message);
            RaiseStage(BroadcastStage.Failed, e.Message);
            SetError(e.Message);
            logger.LogError("[RemoteBroadcast] 语音合成失败：{Message}", e.Message);
        }
        catch (OperationCanceledException)
        {
            // 停用或保险丝触发：立即把画面收掉是第一位的。代价是 CompletedToken 可能永远不来、
            // ShowNotificationAsync 的等待任务挂在后台出不来——中止路径上这点泄漏比挂着假通知强。
            TryCancelRequest(request);
            AddHistory(students, destination, caller, contact, "已取消");
            RaiseStage(BroadcastStage.Cancelled, "播报被取消");
            ResetIfBroadcasting();
            logger.LogInformation("[RemoteBroadcast] 播报被取消");
        }
        catch (Exception e)
        {
            // 内部错误只给手机端一句人话，细节留日志——堆栈发到老师手机上没有任何意义。
            // 画面不主动收：请求已经上屏且带着有限时长，让它自然播完，宿主才能干净地触发完成令牌；
            // 这里再 Cancel 一次只会制造僵尸票据把通道挂死（真机踩过，见方法开头的教训 2）。
            AddHistory(students, destination, caller, contact, "失败：内部错误");
            RaiseStage(BroadcastStage.Failed, "播报过程中出现内部错误");
            SetError("播报过程中出现内部错误，请查看白板日志");
            logger.LogError(e, "[RemoteBroadcast] 播报链路异常");
        }
        finally
        {
            _playbackCts = null;
        }
    }

    /// <summary>
    /// 纯视觉播报的 Overlay 时长估算：按「字数 ÷ 语速」算，语速取约 4.2 字/秒（EdgeTTS 晓晓级自然语速），
    /// 至少 4 秒、至多压 OverlayHardCap。语音关（快路径）或合成失败（O6 降级）时给画面收尾兜底。
    /// </summary>
    private static double EstimateSpeechSeconds(string text)
    {
        var effective = text.Count(char.IsLetterOrDigit) + text.Count(c => !char.IsLetterOrDigit(c) && c != ' ');
        return Math.Clamp(effective / 4.2, 4, OverlayHardCap.TotalSeconds);
    }

    /// <summary>/api/status：Error 状态挂 30 秒供手机端轮询到，过期自动回 Idle，避免卡死在错误态。</summary>
    public (BroadcastState State, CurrentBroadcast? Current, string LastError) GetStatus()
    {
        lock (_lock)
        {
            if (_state == BroadcastState.Error && DateTime.UtcNow - _errorSinceUtc > ErrorDisplayWindow)
            {
                _state = BroadcastState.Idle;
                _lastError = "";
            }

            return (_state, _current, _lastError);
        }
    }

    /// <summary>/api/history：最新在前，最多 20 条。</summary>
    public List<CallRecord> GetHistory()
    {
        lock (_lock)
        {
            return _history.Take(HistoryCapacity).ToList();
        }
    }

    /// <summary>
    /// 删除历史记录。ids 为空/null = 清空全部；非空 = 只删命中的（未命中的忽略，
    /// 不报错——重复点删除按钮、记录刚好环形挤掉时直接吞掉比抛 404 顺手）。
    /// </summary>
    public int RemoveHistory(Guid[]? ids)
    {
        lock (_lock)
        {
            if (ids == null || ids.Length == 0)
            {
                var n = _history.Count;
                _history.Clear();
                return n;
            }

            var set = new HashSet<Guid>(ids);
            var before = _history.Count;
            _history.RemoveAll(r => set.Contains(r.Id));
            return before - _history.Count;
        }
    }

    /// <summary>插件停用时掐断当前播报，端口释放由 HTTP 服务自己负责。</summary>
    public void Stop()
    {
        _playbackCts?.Cancel();
    }

    private void AddHistory(List<string> students, string destination, string caller, string contact, string result)
    {
        lock (_lock)
        {
            _history.Insert(0, new CallRecord(Guid.NewGuid(), DateTime.UtcNow, students, destination, caller, contact, result));
            if (_history.Count > HistoryCapacity)
            {
                _history.RemoveRange(HistoryCapacity, _history.Count - HistoryCapacity);
            }
        }
    }

    private void SetError(string message)
    {
        lock (_lock)
        {
            _state = BroadcastState.Error;
            _lastError = message;
            _errorSinceUtc = DateTime.UtcNow;
            _current = null;
        }
    }

    private void ResetIfBroadcasting()
    {
        lock (_lock)
        {
            if (_state == BroadcastState.Broadcasting)
            {
                _state = BroadcastState.Idle;
                _current = null;
            }
        }
    }

    private void TryCancelRequest(NotificationRequest? request)
    {
        try
        {
            request?.Cancel();
        }
        catch (Exception e)
        {
            // 请求可能已经被宿主收尾了，取消扑空无所谓，别让它在收尾路径上炸出新的异常。
            logger.LogDebug(e, "[RemoteBroadcast] 取消提醒请求失败（可忽略）");
        }
    }

    /// <summary>
    /// 广播一次阶段变化：先记进状态机（查询接口要回显），再逐个通知订阅者。
    /// 单个订阅者抛异常不能拖累播报链路——网页断线重连、插件回调写错，都不该让教室里的喇叭哑掉，
    /// 所以这里自己兜住异常只记日志。
    /// </summary>
    private void RaiseStage(BroadcastStage stage, string? message = null)
    {
        CurrentBroadcast? snapshot;
        lock (_lock)
        {
            _currentStage = stage;
            snapshot = _current;
        }

        var handler = BroadcastStateChanged;
        if (handler == null)
        {
            return;
        }

        var args = new BroadcastStateChangedEventArgs
        {
            Stage = stage,
            Caller = snapshot?.Caller ?? "",
            Students = snapshot?.Students ?? [],
            Destination = snapshot?.Destination ?? "",
            Contact = snapshot?.Contact ?? "",
            Message = message,
        };

        foreach (var subscriber in handler.GetInvocationList().Cast<EventHandler<BroadcastStateChangedEventArgs>>())
        {
            try
            {
                subscriber(this, args);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "[RemoteBroadcast] 播报阶段订阅者回调异常（已忽略）");
            }
        }
    }

    /// <summary>
    /// 算播报条的最小宽度（逻辑像素）。默认跟随播报前岛条的原始宽度——主线放了课表就直接用课表宽，
    /// 老师们熟悉的那条多宽，播报条就多宽；再拿屏幕宽度 20% 兜底，无组件/窄组件的裸机不至于塌缩成
    /// 一小截，高分屏上比例也撑得住。设置里开了自定义宽度就完全按配置来，不再叠兜底。
    /// 极端情况下拿不到主窗口就返回 0（不约束，退回宿主「内容多宽条多宽」的默认行为）。
    /// 只能在 UI 线程调用（要碰视觉树）。
    /// </summary>
    private double ResolveOverlayBarWidth(RemoteBroadcastConfig config)
    {
        // 自定义宽度是纯配置，不依赖视觉树，直读直返（O5 缓存对它无意义）。
        if (config.IsCustomOverlayWidthEnabled)
        {
            // 自定义也保个下限，手滑填个位数的话正文连遮罩都盖不住，那不是配置是事故。
            return Math.Max(200, config.CustomOverlayWidth);
        }

        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window == null)
        {
            return 0;
        }

        // O5 宽度缓存：视觉树遍历 + 反射读 BackgroundWidth 不便宜（组件越多越贵），而岛条宽度只在
        // 窗口尺寸变化时才会变。按窗口客户区尺寸做键缓存，命中时零视觉树操作；Resize 触发失效。
        var (cachedWidth, cachedClientSize) = _barWidthCache;
        if (cachedClientSize == window.ClientSize)
        {
            return cachedWidth;
        }

        // 插件不引用宿主主程序集，点不了 MainWindowLine 的名，只能按类型名在视觉树里找岛条，
        // 再反射读它的 BackgroundWidth（岛条背景宽 = 主线组件的实际宽度，组件播报期间照常参与测量，
        // 此时读到的就是「播报前原始值」）。宿主哪天改了类型名，这里会静默拿不到宽度——
        // 退化成屏宽兜底而不是崩掉，属于可接受的降级。
        double islandWidth = 0;
        var line = window.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(x => x.GetType().Name == "MainWindowLine");
        if (line?.GetType().GetProperty("BackgroundWidth")?.GetValue(line) is double w)
        {
            islandWidth = w;
        }

        // 主窗口被宿主钉成全屏宽（窗口 Width = 屏幕宽），ClientSize 就是屏幕的逻辑宽度。
        var screenFloor = window.ClientSize.Width * 0.2;
        var width = Math.Max(islandWidth, screenFloor);
        _barWidthCache = (width, window.ClientSize);
        return width;
    }

    /// <summary>O5：岛条宽度缓存，键 = 主窗口 ClientSize（UI 线程独占访问，无需加锁）。</summary>
    private (double Width, Avalonia.Size ClientSize) _barWidthCache;
}
