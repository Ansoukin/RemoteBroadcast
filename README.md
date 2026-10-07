# ClassFabric.RemoteBroadcast (远程广播插件)

[![Release](https://img.shields.io/github/v/release/Ansoukin/RemoteBroadcast?color=005FB8&label=Release)](https://github.com/Ansoukin/RemoteBroadcast/releases)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.txt)
[![Target](https://img.shields.io/badge/Host-ClassFabric%202.2-00bfff.svg)](https://github.com/Ansoukin/ClassFabric)

ClassFabric 现代教学白板生态下的远程广播插件（Remote Broadcast）。通过将教室大屏多媒体白板化身为局域网广播服务端，教师可在移动端（手机/平板浏览器）扫码即用，实现随堂学生呼叫、通知通告推送与大屏声画同步播报。

---

## 🌟 核心特性

- **Quick Call 快捷呼叫**：
  - **呼叫学生**：支持多选学生名单、全选/清空、三维模糊搜索（姓名全称、拼音首字、学号位序）以及**长按滑动批量连选**。
  - **自定义内容广播**：支持标题（遮罩阶段）与正文（悬浮条阶段）分段投放，内置高频快捷通知模板（收作业、值日提醒、清点人数、集合通知）。
  - **双阶段声画同步播报**：严格对齐 ClassFabric 宿主规范——阶段① 主题色 Mask 遮罩（带图标与加粗字标）→ 阶段② 40px Acrylic 毛玻璃悬浮条（带倒计时衰减条与长文本走马灯 RollingText）。
- **纯净安全与局域网直连**：
  - **零外部依赖**：手机端 Web 资源完全由白板本地 HTTP 服务内嵌托管，无公网 CDN 依赖，断开外网时局域网仍可完整运行。
  - **32位安全接入码机制**：一人一码，URL 路径隔离与独立撤销，无凭证请求一律 404，不暴露白板端口指纹。
  - **隐私保护底线**：呼叫记录仅在白板内存保留最近 20 条，重启即清，绝不私自写入硬盘。
- **现代 Fluent 2 / WinUI 3 设计语言**：
  - 接入 Fluent Aurora 四层复合动态流光背景、Acrylic 亚克力模糊材质。
  - 完整支持**手动明暗切换与跟随系统深浅色**。
  - 手机端轻触感振动反馈（Web Vibration API）。
  - 实时 RTT 网络延迟测量与一键通信通道诊断。

---

## 🏗️ 架构拓扑

```text
[ 教师移动端 (Phone / Tablet) ]
       │
       │ HTTP / RESTful (局域网直连，接入码前缀隔离)
       ▼
[ ClassFabric 宿主白板 : BroadcastHttpServer ]
       │
       ├──> ConfigStore / AccessCodeManager (接入鉴权与名单管理)
       │
       └──> QuickCallService (呼叫状态机与生命周期控制)
               │
               ├──> EdgeTTS 客户端 (内存语音流合成，双段补读)
               │
               └──> 宿主提醒通道 (NotificationProviderBase)
                       │
                       ├──> 阶段① QuickCallMaskContent (3s 主题色遮罩)
                       │
                       └──> 阶段② QuickCallOverlayContent (40px 悬浮条 + 音频播放)
```

---

## 📦 安装与部署

### 方式一：下载发布包安装（推荐）

1. 从 [Releases 页面](https://github.com/Ansoukin/RemoteBroadcast/releases) 下载最新版本的 `.cipx` 插件安装包。
2. 打开 ClassFabric 白板端，进入 **应用设置 → 插件**。
3. 点击 **从文件安装插件**，选择下载的 `.cipx` 文件，确认重启 ClassFabric 即可。

### 方式二：开发环境手动调试部署

1. 确认已成功编译 ClassFabric 2.2 宿主（Debug 配置）。
2. 在本项目根目录下执行编译：
   ```bash
   dotnet build src/ClassFabric.RemoteBroadcast/ClassFabric.RemoteBroadcast.csproj -c Debug
   ```
3. 将编译产物目录（包含 DLL、PDB、`manifest.yml`、`icon.png` 及 `Assets/wwwroot/`）复制至宿主输出目录的 `Plugins/ClassFabric.RemoteBroadcast/` 文件夹下。
4. 启动宿主即可加载。

---

## 🚀 教师使用指南

1. **白板端准备**：
   - 在 ClassFabric 设置中心进入 **远程广播** 分组。
   - 在「接入码管理」中点击生成新接入码（可备注姓名如「刘老师」）。
   - 教师使用手机相机或扫码工具扫描屏幕上的二维码。
2. **移动端登记**：
   - 首次进入跟随 OOBE 三步向导登记教师称谓，勾选「记住我」可免重复输入。
3. **发起呼叫**：
   - 点选目标学生并选择前往地点（如「办公室」），点击「立即呼叫」。
   - 白板将立即弹出提示并自动播放语音提醒。

---

## 🛠️ 从源码构建与 CI 流水线

### 本地编译依赖

- .NET 10.0 SDK
- ClassFabric 2.2 本地输出或配置 `ClassFabricDevOutput` 环境变量指向包含 `ClassIsland.Core.dll`、`ClassFabric.Core.dll` 的目录。

```bash
# 编译插件后端与静态网页资产
dotnet build -c Debug
```

### GitHub Actions 自动化 CI

本项目在 `.github/workflows/build_release.yml` 建立了完整的自动化构建流：
- **Push / PR**：全量执行代码合规与编译校验。
- **Tag 推送 (`v*`)**：自动提取版本号并打包生成标准 `.cipx` 插件分发包，发布至 GitHub Releases。

---

## 📄 开源许可与依赖致谢

- **ClassFabric / ClassIsland 生态**：遵循主仓开源许可协议。
- **QRCoder** ([MIT License](https://github.com/codebude/QRCoder))：用于白板端本地生成高清接入二维码。
- **EdgeTTS 协议参考**：参考 [rany2/edge-tts](https://github.com/rany2/edge-tts) 公开文档与算法协议，采用独立安全封装实现。
