<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { app } from "../../shared/store";
import { broadcast, probeRtt } from "../../shared/status";
import { toast } from "../../shared/ui";
import { PREF } from "../../shared/prefs";

const emit = defineEmits<{ go: [page: "home" | "call" | "history" | "settings"] }>();

const name = PREF("rb_name", "");
const avatarChar = name.charAt(0) || "师";
const deviceUid = (localStorage.getItem("rb_device") || "").slice(0, 8).toUpperCase() || "--";

const historyCount = computed(() => app.history.length);

// ---- 播报状态卡：空闲一行，播报中自动展开阶段与进度 ----
const busy = computed(() => broadcast.state === "Broadcasting");
const errored = computed(() => broadcast.state === "Error");

const stateTitle = computed(() => {
  if (errored.value) return "异常";
  if (!busy.value) return "空闲";
  return broadcast.stage === "speechStarted" ? "语音" : "上屏";
});

const stateHint = computed(() => {
  if (errored.value) return broadcast.error || "播报出现问题";
  if (!busy.value) return "等待呼叫请求";
  const cur = broadcast.current;
  if (broadcast.stage === "speechStarted") return "正文播报中，请留意白板";
  return cur ? `${cur.caller} 正在呼叫${cur.destination ? ` · ${cur.destination}` : ""}` : "正在处理播报";
});

const progressPct = computed(() => {
  if (!busy.value) return 0;
  return broadcast.stage === "speechStarted" ? 70 : broadcast.stage === "displayed" ? 40 : 18;
});

const stageTags = computed(() => [
  { label: "上屏", on: busy.value && broadcast.stage !== null },
  { label: "语音", on: busy.value && broadcast.stage === "speechStarted" },
  { label: "完成", on: false },
]);

// ---- 网络诊断 ----
const rtt = ref<number | null>(null);
onMounted(probe);
async function probe(): Promise<void> {
  rtt.value = await probeRtt();
}
function netDiag(): void {
  probe();
  const ok = rtt.value != null && rtt.value < 100;
  toast(
    `到白板延迟：${rtt.value == null ? "测量失败" : rtt.value + " ms"}`,
    ok ? "连接良好（每 30 秒自动测量一次）" : "延迟偏高或离线，请检查网络",
    3600,
  );
}
</script>

<template>
  <section class="page active">
    <div class="home-hero">
      <div class="hero-bar">
        <div class="avatar">{{ avatarChar }}</div>
        <div class="hero-text">
          <b>你好，{{ name }}</b>
          <small>教师端 · 设备 <span class="badge">{{ deviceUid }}</span></small>
        </div>
        <button class="barbtn" style="margin-left:auto" title="设置" @click="emit('go', 'settings')">
          <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M9 18l6-6-6-6"/></svg>
        </button>
      </div>
    </div>

    <!-- 配置未到时先出骨架：功能行占位，加载失败也照常渲染 -->
    <template v-if="!app.loaded">
      <div class="group" style="padding:14px 12px;display:flex;flex-direction:column;gap:10px">
        <div class="skl" style="height:13px;width:42%"></div>
        <div class="skl" style="height:46px"></div>
        <div class="skl" style="height:46px;width:85%"></div>
        <div class="skl" style="height:46px;width:70%"></div>
      </div>
    </template>
    <template v-else>

    <!-- 播报状态：空闲一行；播报时自动展开 -->
    <div class="group" style="margin-bottom:10px">
      <div class="status-row">
        <span class="live-dot" :class="{ busy: busy, err: errored }"></span>
        <b>{{ stateTitle }}</b>
        <span class="status-hint">{{ stateHint }}</span>
      </div>
      <div class="status-detail" v-if="busy">
        <div class="live-prog"><i :style="{ width: progressPct + '%' }"></i></div>
        <div class="stage-line">
          <span v-for="t in stageTags" :key="t.label" class="stage-tag" :class="{ on: t.on }">{{ t.label }}</span>
        </div>
      </div>
    </div>

    <div class="group">
      <div class="ghead">
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M3 11l18-5v12L3 14v-3z"/></svg>
        远程呼叫功能
      </div>
      <button class="row" @click="emit('go', 'call')">
        <span class="ic"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m3 11 18-5v12L3 14v-3z"/><path d="M11.6 16.8a3 3 0 1 1-5.8-1.6"/></svg></span>
        <div class="tx">
          <b>Quick Call 快捷呼叫</b>
          <small>呼叫学生名单或投放自定义广播至班级大屏</small>
        </div>
        <svg class="chev" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="m9 6 6 6-6 6"/></svg>
      </button>
      <div class="row disabled">
        <span class="ic" style="background:transparent;border:1px solid var(--stroke-strong);color:var(--text-3)"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><path d="M12 19v3"/></svg></span>
        <div class="tx"><b>实时语音对讲</b><small>暂时不可用</small></div>
        <span class="tag">暂不可用</span>
      </div>
      <button class="row" @click="emit('go', 'history')">
        <span class="ic"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3.5 2"/></svg></span>
        <div class="tx"><b>呼叫历史</b><small>查看过往呼叫记录，一键按原内容重发</small></div>
        <span class="tag">{{ historyCount }}</span>
        <svg class="chev" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="m9 6 6 6-6 6"/></svg>
      </button>
    </div>

    <div class="group">
      <div class="ghead">
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-.33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>
        偏好与设置
      </div>
      <button class="row" @click="emit('go', 'settings')">
        <span class="ic"><svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="8" r="4"/><path d="M5 21c0-3.9 3.1-7 7-7s7 3.1 7 7"/></svg></span>
        <div class="tx"><b>设置</b><small>配置通用偏好、外观主题与查看关于</small></div>
        <svg class="chev" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="m9 6 6 6-6 6"/></svg>
      </button>
      <button class="row" @click="netDiag">
        <span class="ic"><svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M5 12.55a11 11 0 0 1 14.08 0"/><path d="M1.42 9a16 16 0 0 1 21.16 0"/><path d="M8.53 16.11a6 6 0 0 1 6.95 0"/><line x1="12" y1="20" x2="12.01" y2="20"/></svg></span>
        <div class="tx"><b>网络连接诊断</b><small>测试到白板端的延迟与连接健康度</small></div>
        <svg class="chev" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="m9 6 6 6-6 6"/></svg>
      </button>
    </div>
    </template>
  </section>
</template>

<style scoped>
/* 设计稿只给了「忙碌」的绿点；异常态补一个红点 */
.live-dot.err { background: var(--error); }
/* 骨架占位：呼吸式微光 */
.skl { border-radius: var(--r-ctrl); background: var(--item-hover);
  animation: sklPulse 1.6s ease-in-out infinite; }
@keyframes sklPulse { 0%, 100% { opacity: 1; } 50% { opacity: .45; } }
@media (prefers-reduced-motion: reduce) { .skl { animation: none; } }
</style>
