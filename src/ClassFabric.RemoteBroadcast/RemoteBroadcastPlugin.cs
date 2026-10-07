using ClassFabric.RemoteBroadcast.Abstractions;
using ClassFabric.RemoteBroadcast.Models;
using ClassFabric.RemoteBroadcast.Services;
using ClassFabric.RemoteBroadcast.UI;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Icons;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClassFabric.RemoteBroadcast;

/// <summary>
/// Remote Broadcast 插件入口。
/// 注意：Initialize 阶段宿主还没起，这里只做服务注册；所有依赖宿主运行态的东西（IAppHost、
/// 提醒发送等）都推迟到被解析/启动之后才碰。
/// </summary>
public class RemoteBroadcastPlugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        var configFolder = PluginConfigFolder;
        var pluginFolder = Info.PluginFolderPath;
        var version = Info.Manifest?.Version ?? "0.1.0";

        services.AddSingleton(sp =>
        {
            var store = new ConfigStore(configFolder, sp.GetRequiredService<ILogger<ConfigStore>>());
            store.Load();
            return store;
        });
        services.AddSingleton<EdgeTtsClient>();
        services.AddSingleton<AccessCodeManager>();
        services.AddSingleton<QuickCallService>();
        // AddNotificationProvider 内部走 AddHostedService<T>（TryAddEnumerable），不会把 T 注册成可解析的服务；
        // QuickCallService 要直接拿它发通知，所以先手动补一个单例注册。
        services.AddSingleton<QuickCallNotificationProvider>();
        services.AddNotificationProvider<QuickCallNotificationProvider>();
        // 状态流服务先注册（HTTP 服务构造时要它）；它自己订阅状态机的阶段事件。
        services.AddSingleton<BroadcastStatusStream>();
        services.AddSingleton(sp => new BroadcastHttpServer(
            sp.GetRequiredService<ConfigStore>(),
            sp.GetRequiredService<AccessCodeManager>(),
            sp.GetRequiredService<QuickCallService>(),
            sp.GetRequiredService<BroadcastStatusStream>(),
            sp.GetServices<ITeachersDirectoryProvider>(),
            pluginFolder,
            version,
            sp.GetRequiredService<ILogger<BroadcastHttpServer>>()));
        services.AddHostedService(sp => sp.GetRequiredService<BroadcastHttpServer>());
        services.AddSingleton(new RemoteBroadcastDescriptor(version));

        // 插件间 API：三类契约共用同一实例——消费方解析到哪一个，拿到的都是同一份状态与同一条事件流。
        // 注册进宿主 DI 后，其它插件用 IAppHost.GetService<IRemoteBroadcastSender>() 就能拿到。
        services.AddSingleton<RemoteBroadcastApi>();
        services.AddSingleton<IRemoteBroadcastSender>(sp => sp.GetRequiredService<RemoteBroadcastApi>());
        services.AddSingleton<IRemoteBroadcastQuery>(sp => sp.GetRequiredService<RemoteBroadcastApi>());
        services.AddSingleton<IRemoteBroadcastEvents>(sp => sp.GetRequiredService<RemoteBroadcastApi>());

        // 设置页拆成「主设置／人员与目的地／呼叫记录／关于」四页，统一挂在一个插件分组下。
        // 分组图标用 ChannelRegular：这个插件在设置导航里代表的就是「一条对外广播通道」。
        services.AddSettingsPageGroup("remoteBroadcast", FluentIcons.ChannelRegular, "远程广播");
        services.AddSettingsPage<RemoteBroadcastSettingsPage>();
        services.AddSettingsPage<RemoteBroadcastRosterSettingsPage>();
        services.AddSettingsPage<RemoteBroadcastHistorySettingsPage>();
        services.AddSettingsPage<RemoteBroadcastAboutSettingsPage>();

        AppBase.Current.AppStarted += (_, _) =>
        {
            IAppHost.GetService<ILogger<RemoteBroadcastPlugin>>()
                ?.LogInformation("[RemoteBroadcast] 插件已加载，Quick Call 全链路就绪");

            // O3 连接预热：App 起来就把 EdgeTTS 的 DNS/TCP/TLS 路径跑热（静音合成一次，
            // TLS 会话票证留供后续握手复用）。后台跑、失败静默，冷启动的 0.5–1.5 秒握手税
            // 从「老师点呼叫时」挪到「宿主启动时」——课堂里没人等宿主启动，但人人等呼叫出声。
            if (IAppHost.GetService<EdgeTtsClient>() is { } ttsClient)
            {
                _ = ttsClient.WarmUpAsync();
            }
        };
    }
}
