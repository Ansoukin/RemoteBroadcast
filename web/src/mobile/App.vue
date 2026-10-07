<script setup lang="ts">
import { onMounted, ref } from "vue";
import { loadConfig, refreshHistory } from "../shared/store";
import { probeRtt, onBroadcastMessage } from "../shared/status";
import { toast } from "../shared/ui";
import { tryResumeName } from "../shared/prefs";
import { ui, dismissToast, resolveDialog } from "../shared/ui";
import { callBar } from "./callbar";
import OobePage from "./pages/Oobe.vue";
import HomePage from "./pages/Home.vue";
import CallPage from "./pages/Call.vue";
import HistoryPage from "./pages/History.vue";
import SettingsPage from "./pages/Settings.vue";

type PageName = "oobe" | "home" | "call" | "history" | "settings";
const page = ref<PageName>(tryResumeName() ? "home" : "oobe");

function go(p: PageName): void {
  page.value = p;
}

onMounted(async () => {
  await loadConfig().catch(() => undefined);
  await refreshHistory();
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

function onCallBarSubmit(): void {
  callBar.submit?.();
}
</script>

<template>
  <div class="stage aurora-on">
    <header class="bar">
      <svg class="logo" viewBox="0 0 192 192" xmlns="http://www.w3.org/2000/svg">
        <path fill="#e2e2e2" d="M177.94,160H56V100H177.94a8,8,0,0,1,8,8v44A8,8,0,0,1,177.94,160Z"/>
        <rect fill="#999" x="70.01" y="115" width="30" height="30"/><rect fill="#999" x="107.51" y="115" width="30" height="30"/><rect fill="#999" x="145" y="115" width="30" height="30"/>
        <polygon fill="#959595" points="124 32 56 100 56 160 124 92 124 32"/>
        <path fill="#f6f6f6" d="M124,92H16a8,8,0,0,1-8-8V40a8,8,0,0,1,8-8H124Z"/>
        <rect fill="#ccc" x="20" y="44.53" width="30" height="30"/><rect fill="#00bfff" x="56" y="44.53" width="58.02" height="30"/>
        <polygon fill="#00bfff" opacity=".15" points="116 100 56 160 185.94 160 185.94 153.94 132 100 116 100"/>
      </svg>
      <span class="tt">远程广播</span>
      <div class="net-pill" title="点击查看网络诊断" @click="probe()">
        <span class="net-dot" :class="rtt == null ? '' : rtt < 100 ? 'ok' : 'bad'"></span>
        <span>{{ rtt == null ? "--" : rtt + "ms" }}</span>
      </div>
      <button class="barbtn" title="设置" @click="go('settings')">
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-.33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>
      </button>
    </header>

    <main>
      <component :is="page === 'oobe' ? OobePage : page === 'home' ? HomePage : page === 'call' ? CallPage : page === 'history' ? HistoryPage : SettingsPage" :key="page" @go="go" />
    </main>

    <!-- 常驻呼叫条：只有呼叫页显示 -->
    <div class="callbar" v-if="callBar.visible">
      <div class="sum">{{ callBar.summary }}</div>
      <button class="btn accent" :disabled="callBar.busy" style="margin-left:auto;width:auto;padding:0 22px;min-height:36px" @click="onCallBarSubmit">
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m3 11 18-5v12L3 14v-3z"/><path d="M11.6 16.8a3 3 0 1 1-5.8-1.6"/></svg>
        立即呼叫
      </button>
    </div>

    <footer class="foot">
      <span class="footer-chip" title="远程广播 · ClassFabric 插件">ClassFabric · 远程广播</span>
    </footer>

    <!-- Toast -->
    <transition-group name="toast-fade" tag="div">
      <div v-for="t in ui.toasts" :key="t.id" class="toast show" @click="dismissToast(t.id)">
        <svg class="ic" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="m8.5 12.5 2.5 2.5 4.5-5"/></svg>
        <div><b>{{ t.title }}</b><small v-if="t.sub">{{ t.sub }}</small></div>
      </div>
    </transition-group>

    <!-- ContentDialog：取消/遮罩只关不触发；确认回调只挂 OK 键 -->
    <div class="scrim" :class="{ show: ui.dialog }" @click="ui.dialog && resolveDialog(false)"></div>
    <div class="dlg" :class="{ show: ui.dialog }" v-if="ui.dialog">
      <h2>{{ ui.dialog.title }}</h2>
      <div class="body" :class="{ 'with-avatar': ui.dialog.avatarUrl }" style="white-space:pre-line">
        <img v-if="ui.dialog.avatarUrl" class="dlg-avatar" :src="ui.dialog.avatarUrl" alt="" @error="($event.target as HTMLImageElement).style.display = 'none'" />
        <span>{{ ui.dialog.body }}</span>
      </div>
      <div class="foot">
        <button class="btn std" v-if="ui.dialog.cancelText" @click="resolveDialog(false)">{{ ui.dialog.cancelText }}</button>
        <button class="btn accent" @click="resolveDialog(true)">{{ ui.dialog.okText || "好的" }}</button>
      </div>
    </div>
  </div>
</template>

<style>
/* Toast 进出场 */
.toast-fade-enter-active, .toast-fade-leave-active { transition: opacity .2s, transform .2s; }
.toast-fade-enter-from, .toast-fade-leave-to { opacity: 0; transform: translateY(8px); }
</style>
