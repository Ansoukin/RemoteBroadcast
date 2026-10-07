using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Interactivity;
using ClassFabric.RemoteBroadcast.Models;
using ClassFabric.RemoteBroadcast.Services;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Core.Icons;
using FluentAvalonia.UI.Controls;

namespace ClassFabric.RemoteBroadcast.UI;

/// <summary>
/// 远程广播「主设置」页：服务开关/端口、播报参数与模板。
/// 播报参数改动即保存（配置颗粒度小，拆「保存按钮」反而容易让人怀疑没存上）；
/// 只有端口和服务开关例外——它们要重启监听，放在「应用并重启服务」里一起做。
 /// 服务状态按 GM 拍板升级为规格行卡（状态/访问地址/内核版本三行，4.2.3），
 /// urlacl 授权改为「一键授权 + 按需提示」（4.4，常态零文案）。
/// </summary>
[SettingsPageInfo("remoteBroadcast", "主设置", FluentIcons.SettingsRegular, FluentIcons.SettingsRegular,
    SettingsPageCategory.External)]
[Group("remoteBroadcast")]
public partial class RemoteBroadcastSettingsPage : SettingsPageBase
{
    private readonly ConfigStore _store;
    private readonly BroadcastHttpServer _server;
    private readonly RemoteBroadcastDescriptor _descriptor;
    private bool _loaded;

    public RemoteBroadcastSettingsPage(ConfigStore store, BroadcastHttpServer server, RemoteBroadcastDescriptor descriptor)
    {
        _store = store;
        _server = server;
        _descriptor = descriptor;
        InitializeComponent();
        // 拨到「自定义」才放开数值输入，跟随开关走，免得出现「开关关着改数值却没生效」的困惑。
        CustomWidthToggle.IsCheckedChanged += (_, _) =>
        {
            CustomWidthBox.IsEnabled = CustomWidthToggle.IsChecked == true;
            SaveWidthSetting();
        };
        // 宽度改动即存：这俩藏在「保存播报设置」后面时是静默无效的——开关没开/没点保存，
        // 调 200 还是 4000 播报条纹丝不动，实测就是「调整没有区别」的来源。
        CustomWidthBox.ValueChanged += (_, _) => SaveWidthSetting();
        // 语音总开关关闭时灰掉语音提示，两级开关的从属关系一眼可见。
        SpeechToggle.IsCheckedChanged += (_, _) => HintToggle.IsEnabled = SpeechToggle.IsChecked == true;
        Loaded += (_, _) => LoadFromConfig();
    }

    private void LoadFromConfig()
    {
        var c = _store.Snapshot();
        ServiceToggle.IsChecked = c.ServiceEnabled;
        PortBox.Value = Math.Clamp(c.Port, 1024, 65535);
        LoopBox.Value = Math.Clamp(c.AnnouncementLoopCount, 1, 5);
        SpeechToggle.IsChecked = c.SpeechEnabled;
        HintToggle.IsEnabled = c.SpeechEnabled;
        HintToggle.IsChecked = c.SpeechHintEnabled;
        VolumeBox.Value = Math.Clamp(c.AnnouncementVolume, 0f, 100f);
        VoiceBox.Text = c.VoiceName;
        CustomWidthToggle.IsChecked = c.IsCustomOverlayWidthEnabled;
        CustomWidthBox.Value = Math.Clamp(c.CustomOverlayWidth, 200, 4000);
        CustomWidthBox.IsEnabled = c.IsCustomOverlayWidthEnabled;
        TemplateSingleBox.Text = c.TemplateSingle;
        TemplateGuideBox.Text = c.TemplateMultiGuide;
        TemplateHintBox.Text = c.TemplateSpeechHint;
        TemplateSingleNoContactBox.Text = c.TemplateSingleNoContact;
        TemplateMultiGuideNoContactBox.Text = c.TemplateMultiGuideNoContact;
        _loaded = true;
        RefreshServiceStatus();
        RefreshAddresses();
    }

    /// <summary>播报条宽度改动即存；装载配置期间 Value 会回填触发本方法，_loaded 前一律忽略。</summary>
    private void SaveWidthSetting()
    {
        if (!_loaded)
        {
            return;
        }
        _store.Update(c =>
        {
            c.IsCustomOverlayWidthEnabled = CustomWidthToggle.IsChecked == true;
            c.CustomOverlayWidth = ReadDouble(CustomWidthBox, 800, 200, 4000);
        });
    }

    // ---- 服务 ----

    private void RefreshServiceStatus()
    {
        var ip = LanUtils.PickPreferredAddress(LanUtils.GetLanAddresses(), _store.Snapshot().PreferredLanAddress);
        var port = ReadInt(PortBox, 5212, 1024, 65535);

        // ---- 四态解析（4.2.3）：色点 + 状态词 + 辅助信息一次算齐 ----
        (IBrush Dot, string State, string Mode) status = _server.BindMode switch
        {
            "Lan" => (DotBrush("#2BB673"), "运行中", "局域网模式"),
            "LocalOnly" => (DotBrush("#E9A700"), "降级运行", "仅本机"),
            "Stopped" when _server.BindHint != null => (DotBrush("#E05B4D"), "未运行", "端口被占用"),
            _ => (DotBrush("#8A8A8A"), "已停用", ""),
        };
        StatusDot.Fill = status.Dot;
        StatusValueText.Text = status.State;
        StatusValueText.Foreground = status.State switch
        {
            "运行中" => status.Dot,
            "降级运行" => DotBrush("#E9A700"),
            "未运行" => DotBrush("#E05B4D"),
            _ => Foreground,
        };
        StatusModeText.Text = status.Mode;
        StatusModeText.IsVisible = status.Mode.Length > 0;

        var reachable = _server.BindMode == "Lan";
        AddressValueText.Text = reachable ? $"http://{ip}:{port}/" : "—";
        AddressValueText.Cursor = reachable ? new Cursor(StandardCursorType.Hand) : Cursor.Default;

        // 内核版本与插件版本同源（descriptor.Version，manifest 单一事实源——4.2.3 决议）
        KernelVersionText.Text = "v" + _descriptor.Version + "（与插件一致）";
        KernelHostText.Text = "ClassFabric";

        // ---- 4.4 URLACL 一键授权条：只在「缺授权」时出现，常态零文案 ----
        UrlAclBar.IsVisible = _server.NeedsUrlAcl;
        UrlAclButton.IsEnabled = _server.NeedsUrlAcl;
    }

    private async void UrlAcl_Click(object? sender, RoutedEventArgs e)
    {
        var port = ReadInt(PortBox, 5212, 1024, 65535);
        UrlAclButton.IsEnabled = false;
        UrlAclMsgText.Text = "等待管理员确认…";
        var (ok, message) = await BroadcastHttpServer.AddUrlAclAsync(port);
        UrlAclMsgText.Text = message;
        if (ok)
        {
            // 授权就绪立刻重启监听复测，把「授权 → 生效」闭环在同一个界面里走完。
            ApplyService_Click(null, e);
        }
        else
        {
            UrlAclButton.IsEnabled = true;
        }
    }

    private void CopyUrlAcl_Click(object? sender, RoutedEventArgs e)
    {
        var port = ReadInt(PortBox, 5212, 1024, 65535);
        CopyToClipboard($"netsh http add urlacl url=http://+:{port}/ user=Everyone");
        UrlAclMsgText.Text = "命令已复制，请在管理员终端执行后重启服务。";
    }

    private void CopyAddress_Click(object? sender, PointerReleasedEventArgs e)
    {
        var url = AddressValueText.Text;
        if (url is "—" or "" or null)
        {
            return;
        }
        CopyToClipboard(url);
        AddressCopyText.Text = "已复制";
    }

    private void CopyToClipboard(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            _ = clipboard.SetTextAsync(text);
        }
    }

    /// <summary>取主题资源画刷，失败退回固定色（色点这类小元素直接定色也可接受）。</summary>
    private static IBrush DotBrush(string fallbackHex)
    {
        var key = fallbackHex switch
        {
            "#2BB673" => "SystemFillColorSuccessBrush",
            "#E9A700" => "SystemFillColorCautionBrush",
            "#E05B4D" => "SystemFillColorCriticalBrush",
            _ => "SystemControlForegroundBaseMediumBrush",
        };
        if (Application.Current?.TryGetResource(key, null, out var value) == true && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    private void RefreshAddresses()
    {
        var c = _store.Snapshot();
        var addresses = LanUtils.GetLanAddresses();
        AddressBox.ItemsSource = addresses.Select(a => $"{a.Ip}（{a.AdapterName}）").ToList();
        var picked = addresses.FindIndex(a => a.Ip == c.PreferredLanAddress);
        AddressBox.SelectedIndex = picked >= 0 ? picked : 0;
    }

    private void ApplyService_Click(object? sender, RoutedEventArgs e)
    {
        var ip = AddressBox.SelectedItem as string ?? "";
        // ComboBox 文案是「IP（网卡名）」拼接，取前半段还原纯 IP。
        var chosenIp = ip.Split('（')[0];
        _store.Update(c =>
        {
            c.ServiceEnabled = ServiceToggle.IsChecked == true;
            c.Port = ReadInt(PortBox, 5212, 1024, 65535);
            c.PreferredLanAddress = chosenIp;
        });
        _server.Restart();
        RefreshServiceStatus();
    }

    // ---- 播报参数与模板 ----

    private void SaveAnnounceSettings_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            // 模板先校验再入库：非法占位符直接拒绝并提示，别让一条坏模板把播报链路带崩在运行时。
            AnnouncementComposer.ValidateTemplate(TemplateSingleBox.Text ?? "", "单学生模板");
            AnnouncementComposer.ValidateTemplate(TemplateGuideBox.Text ?? "", "多学生引导句");
            AnnouncementComposer.ValidateTemplate(TemplateHintBox.Text ?? "", "语音提示模板");
            AnnouncementComposer.ValidateTemplate(TemplateSingleNoContactBox.Text ?? "", "无接洽人单学生模板");
            AnnouncementComposer.ValidateTemplate(TemplateMultiGuideNoContactBox.Text ?? "", "无接洽人多学生引导句");
        }
        catch (AnnouncementException ex)
        {
            AnnounceMsgText.Text = ex.Message;
            return;
        }

        _store.Update(c =>
        {
            c.AnnouncementLoopCount = ReadInt(LoopBox, 2, 1, 5);
            c.SpeechEnabled = SpeechToggle.IsChecked == true;
            c.SpeechHintEnabled = HintToggle.IsChecked == true;
            c.AnnouncementVolume = (float)ReadDouble(VolumeBox, 80, 0, 100);
            c.VoiceName = (VoiceBox.Text ?? "").Trim();
            c.IsCustomOverlayWidthEnabled = CustomWidthToggle.IsChecked == true;
            c.CustomOverlayWidth = ReadDouble(CustomWidthBox, 800, 200, 4000);
            c.TemplateSingle = TemplateSingleBox.Text ?? "";
            c.TemplateMultiGuide = TemplateGuideBox.Text ?? "";
            c.TemplateSpeechHint = TemplateHintBox.Text ?? "";
            c.TemplateSingleNoContact = TemplateSingleNoContactBox.Text ?? "";
            c.TemplateMultiGuideNoContact = TemplateMultiGuideNoContactBox.Text ?? "";
        });
        AnnounceMsgText.Text = "已保存";
    }

    // ---- 小工具 ----

    /// <summary>
    /// FANumberBox 的 Value 是 double，输入框被清空或填了非法内容时会变成 NaN（宿主靠 NotNaNConverter
    /// 挡这个）。这里是命令式读值，不挡的话 (int)NaN 会静默变成 0，端口直接从 1024 起步，看着像配置被吃了。
    /// 统一按「非法/越界 → 收敛回合法区间，NaN → 回退默认值」处理。
    /// </summary>
    private static int ReadInt(FANumberBox box, int fallback, int min, int max)
    {
        return double.IsFinite(box.Value) ? (int)Math.Clamp(box.Value, min, max) : fallback;
    }

    private static double ReadDouble(FANumberBox box, double fallback, double min, double max)
    {
        return double.IsFinite(box.Value) ? Math.Clamp(box.Value, min, max) : fallback;
    }
}
