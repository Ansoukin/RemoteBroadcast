<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { loadConfig, refreshHistory, app } from "../shared/store";
import { probeRtt, onBroadcastMessage } from "../shared/status";
import { toast, ui, dismissToast, resolveDialog } from "../shared/ui";
import { PREF, savePref, tryResumeName } from "../shared/prefs";
import { auroraEnabled, applyAurora, currentThemeMode, setThemeMode } from "../shared/theme";
import WorkbenchPage from "./pages/Workbench.vue";
import HistoryPage from "./pages/HistoryPage.vue";
import SettingsPage from "./pages/SettingsPage.vue";
import OobeOverlay from "./OobeOverlay.vue";

type PageName = "work" | "history" | "settings";
const pageTitles: Record<PageName, string> = { work: "工作台", history: "呼叫历史", settings: "设置" };
const page = ref<PageName>("work");
const showOobe = ref(!tryResumeName());
// 向导起始步：首次打开从第 1 步走；设置页「修改称谓」直达登记步，重播向导从头看
const oobeStep = ref<1 | 2 | 3>(1);

function go(p: PageName): void {
  page.value = p;
}

onMounted(async () => {
  await loadConfig().catch(() => undefined);
  await refreshHistory();
  // 流光动画默认关（性能优先）：静态渐变底常驻，偏好开了才让四层渐变流动
  applyAurora(auroraEnabled());
});

// ---- 网络延迟胶囊（30s 一测） ----
const rtt = ref<number | null>(null);
async function probe(): Promise<void> {
  rtt.value = await probeRtt();
}
probe();
setInterval(probe, 30000);

// ---- 阶段终态提示（实时流推送驱动；轮询路径静默） ----
onBroadcastMessage(msg => {
  if (msg.type !== "stage") return;
  if (msg.stage === "completed") {
    refreshHistory();
  } else if (msg.stage === "failed") {
    toast("呼叫失败：" + (msg.message || "未知原因"), "", 4000);
  } else if (msg.stage === "cancelled") {
    toast("播报已取消", "", 2600);
  }
});

// ---- 主题快捷轮换（跟随系统 → 浅色 → 深色） ----
const themeMode = ref(currentThemeMode());
function cycleTheme(): void {
  const order = ["auto", "light", "dark"] as const;
  const next = order[(order.indexOf(themeMode.value) + 1) % 3];
  themeMode.value = next;
  setThemeMode(next);
  toast("外观已切换", next === "auto" ? "已跟随系统明暗" : next === "light" ? "已切换为浅色" : "已切换为深色");
}

function openHelp(): void {
  toast("使用帮助", "呼叫学生：搜索选择名单与目的地后点「立即呼叫」；批量勾选：按住鼠标左键在名单上滑动。", 5200);
}

// ---- 跳移动端页面：只改查询参数，路径原样保留——
// 之前重拼过整个 URL，把接入码后的斜杠丢了，base 为相对引用的静态资源就会
// 解析到 /assets/...（没有接入码段）而 404，页面全白。 ----
function goMobile(): void {
  const q = new URLSearchParams(location.search);
  q.set("ui", "mobile");
  location.search = q.toString();
}

// ---- 侧栏收起/展开：点品牌图标切换；收窄成 56px 图标条后悬停图标显示名称提示 ----
const navCollapsed = ref(PREF("rb_navcollapse", "0") === "1");
function toggleNav(): void {
  navCollapsed.value = !navCollapsed.value;
  savePref("rb_navcollapse", navCollapsed.value ? "1" : "0");
}

// ---- 底部状态栏：版本号 ⇄ 产品口号 ----
const sloganMode = ref(false);
const versionText = computed(() => {
  const c = app.config;
  return c ? `ClassFabric ${c.hostVersion} · 远程广播 v${c.version}` : "ClassFabric · 远程广播";
});
const sloganText = computed(() => {
  const c = app.config;
  return `${c?.hostVersion || "ClassFabric"} —— 一款功能强、可定制，适用于班级多媒体屏幕的课表信息显示工具`;
});
</script>

<template>
  <div class="app-root">
    <div class="aurora"></div>
    <div class="app">
      <aside class="nav" :class="{ collapsed: navCollapsed }">
        <div class="brand">
          <button class="brand-btn" :title="navCollapsed ? '展开侧栏' : '收起侧栏'" @click="toggleNav">
            <svg class="ic-logo" viewBox="0 0 192 192" xmlns="http://www.w3.org/2000/svg">
              <path fill="#e2e2e2" d="M177.94,160H56V100H177.94a8,8,0,0,1,8,8v44A8,8,0,0,1,177.94,160Z"/>
              <rect fill="#999" x="70.01" y="115" width="30" height="30"/><rect fill="#999" x="107.51" y="115" width="30" height="30"/><rect fill="#999" x="145" y="115" width="30" height="30"/>
              <polygon fill="#959595" points="124 32 56 100 56 160 124 92 124 32"/>
              <path fill="#f6f6f6" d="M124,92H16a8,8,0,0,1-8-8V40a8,8,0,0,1,8-8H124Z"/>
              <rect fill="#ccc" x="20" y="44.53" width="30" height="30"/><rect fill="#00bfff" x="56" y="44.53" width="58.02" height="30"/>
              <polygon fill="#00bfff" opacity=".15" points="116 100 56 160 185.94 160 185.94 153.94 132 100 116 100"/>
            </svg>
            <svg class="ic-menu" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M4 6h16"/><path d="M4 12h16"/><path d="M4 18h16"/></svg>
          </button>
          <div class="brand-text"><div class="name">Quick Call</div><div class="sub">ClassFabric 远程广播</div></div>
        </div>
        <button class="nav-item" :class="{ active: page === 'work' }" :title="navCollapsed ? '工作台' : undefined" @click="go('work')">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 10.5 12 3l9 7.5"/><path d="M5 9.5V21h14V9.5"/></svg>
          <span>工作台</span>
        </button>
        <button class="nav-item" :class="{ active: page === 'history' }" :title="navCollapsed ? '呼叫历史' : undefined" @click="go('history')">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3.5 2"/></svg>
          <span>呼叫历史</span><span class="badge">{{ app.history.length }}</span>
        </button>
        <button class="nav-item" :class="{ active: page === 'settings' }" :title="navCollapsed ? '设置' : undefined" @click="go('settings')">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="12" r="3.2"/><path d="M19 12c0-.6.5-1.2 1.1-1.6-.2-.9-.6-1.7-1.1-2.4-.7.2-1.5.1-2-.4s-.6-1.3-.4-2c-.7-.5-1.5-.9-2.4-1.1-.4.6-1 1.1-1.6 1.1s-1.2-.5-1.6-1.1c-.9.2-1.7.6-2.4 1.1.2.7.1 1.5-.4 2s-1.3.6-2 .4c-.5.7-.9 1.5-1.1 2.4.6.4 1.1 1 1.1 1.6s-.5 1.2-1.1 1.6c.2.9.6 1.7 1.1 2.4.7-.2 1.5-.1 2 .4s.6 1.3.4 2c.7.5 1.5.9 2.4 1.1.4-.6 1-1.1 1.6-1.1s1.2.5 1.6 1.1c.9-.2 1.7-.6 2.4-1.1-.2-.7-.1-1.5.4-2s1.3-.6 2-.4c.5-.7.9-1.5 1.1-2.4-.6-.4-1.1-1-1.1-1.6z"/></svg>
          <span>设置</span>
        </button>
        <div class="nav-foot">
          <div class="conn-pill">
            <span class="dot" :class="rtt != null ? (rtt < 100 ? 'ok pulse' : 'err') : 'off'"></span>
            <span class="txt">{{ rtt == null ? "白板离线" : "大屏在线 · " + rtt + "ms" }}</span>
          </div>
        </div>
      </aside>

      <main>
        <header class="topbar">
          <h1>{{ pageTitles[page] }}</h1>
          <div class="top-actions">
            <button class="icon-btn" title="切换到移动端页面" @click="goMobile">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><rect x="7" y="2.5" width="10" height="19" rx="2.2"/><path d="M11 18.5h2"/></svg>
            </button>
            <button class="net-pill" @click="probe()">
              <span class="dot" :class="rtt != null ? (rtt < 100 ? 'ok' : 'err') : 'off'"></span>
              <span>{{ rtt == null ? "--" : rtt + "ms" }}</span>
            </button>
            <button class="icon-btn" title="使用帮助" @click="openHelp">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M9.1 9a3 3 0 0 1 5.8 1c0 2-3 2.4-3 4"/><path d="M12 17.5h.01"/></svg>
            </button>
            <button class="icon-btn" title="切换主题" @click="cycleTheme">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M20 13.5A8 8 0 0 1 10.5 4 8 8 0 1 0 20 13.5z"/></svg>
            </button>
          </div>
        </header>

        <KeepAlive include="WorkbenchPage,HistoryPage,SettingsPage">
          <component :is="page === 'work' ? WorkbenchPage : page === 'history' ? HistoryPage : SettingsPage" :key="page" @go="go" @replay-oobe="oobeStep = 1; showOobe = true" @edit-name="oobeStep = 3; showOobe = true" />
        </KeepAlive>
      </main>
    </div>

    <footer class="statusbar">
      <span class="footer-chip" :title="'点击切换产品描述'" @click="sloganMode = !sloganMode">{{ sloganMode ? sloganText : versionText }}</span>
    </footer>

    <!-- OOBE 三步向导（首次打开；设置页可重播或直达登记步） -->
    <OobeOverlay v-if="showOobe" :initial-step="oobeStep" @finish="showOobe = false" />

    <!-- Toast -->
    <transition-group name="toast-fade" tag="div">
      <div v-for="t in ui.toasts" :key="t.id" class="toast show" @click="dismissToast(t.id)">
        <b>{{ t.title }}</b><small v-if="t.sub">{{ t.sub }}</small>
      </div>
    </transition-group>

    <!-- 通用弹窗 -->
    <div class="scrim" :class="{ show: ui.dialog }" @click="ui.dialog && resolveDialog(false)"></div>
    <div class="dlg" :class="{ show: ui.dialog }" v-if="ui.dialog">
      <h2>{{ ui.dialog.title }}</h2>
      <div class="body" :class="{ 'with-avatar': ui.dialog.avatarUrl }" style="white-space:pre-line">
        <img v-if="ui.dialog.avatarUrl" class="dlg-avatar" :src="ui.dialog.avatarUrl" alt="" @error="($event.target as HTMLImageElement).style.display = 'none'" />
        <span>{{ ui.dialog.body }}</span>
      </div>
      <div class="foot">
        <button class="btn" v-if="ui.dialog.cancelText" @click="resolveDialog(false)">{{ ui.dialog.cancelText }}</button>
        <button class="btn accent" @click="resolveDialog(true)">{{ ui.dialog.okText || "好的" }}</button>
      </div>
    </div>
  </div>
</template>

<style>
/* 版式修正：设计稿的 .app 按整屏 100dvh 画，真机还要给底部状态栏留一行 */
.app-root { display: flex; flex-direction: column; height: 100dvh; }
.app-root .app { flex: 1; min-height: 0; height: auto; }
.app-root .statusbar { position: relative; z-index: 1; flex: none; }
.toast-fade-enter-active, .toast-fade-leave-active { transition: opacity .25s, transform .25s; }
.toast-fade-enter-from, .toast-fade-leave-to { opacity: 0; transform: translateY(12px); }
</style>
