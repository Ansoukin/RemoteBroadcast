namespace ClassFabric.RemoteBroadcast.Abstractions;

/// <summary>
/// 事件类接口：播报阶段变化。与网页端的实时状态流（WebSocket）同源——
/// 同一套状态机，两路分发，订阅方拿到的阶段序列完全一致。
/// </summary>
/// <remarks>
/// 事件在后台线程上触发。订阅方若要碰 UI，记得自己切回界面线程。
/// 回调里不要做耗时操作：它跑在播报链路上，拖慢了会直接影响播报时序。
/// </remarks>
public interface IRemoteBroadcastEvents
{
    /// <summary>播报阶段变化（受理 / 上屏 / 语音起 / 完成 / 失败 / 取消）。</summary>
    event EventHandler<BroadcastStateChangedEventArgs>? BroadcastStateChanged;
}
