<script setup lang="ts">
import { onMounted, ref } from "vue";
import { app } from "../../shared/store";
import { currentThemeMode, setThemeMode } from "../../shared/theme";
import { PREF, haptic, savePref } from "../../shared/prefs";
import { showDialog, toast } from "../../shared/ui";

const emit = defineEmits<{ go: [page: "home" | "oobe"] }>();

const name = PREF("rb_name", "");
const avatarChar = name.charAt(0) || "师";
const deviceUid = (localStorage.getItem("rb_device") || "").slice(0, 8).toUpperCase() || "--";
const version = app.config?.version ? `版本 ${app.config.version} · 宿主 ${app.config.hostVersion}` : "版本 --";

const theme = ref(currentThemeMode());
function pickTheme(mode: "auto" | "light" | "dark"): void {
  haptic(15);
  theme.value = mode;
  setThemeMode(mode);
  toast("外观已切换", mode === "auto" ? "已跟随系统明暗" : mode === "light" ? "已切换为浅色" : "已切换为深色");
}

const confirmOn = ref(PREF("rb_confirm", "0") === "1");
const autoclearOn = ref(PREF("rb_autoclear", "1") === "1");
const hapticsOn = ref(PREF("rb_haptics", "1") === "1");
const auroraOn = ref(PREF("rb_aurora", "1") === "1");

function switchAurora(v: boolean): void {
  auroraOn.value = v;
  savePref("rb_aurora", v);
  const stage = document.querySelector(".stage");
  stage?.classList.toggle("aurora-on", v);
}

async function openAuthor(): Promise<void> {
  haptic(15);
  await showDialog({
    title: "关于作者与生态",
    body: "ClassFabric Remote Broadcast\n作者：Ansoukin (Shuai Jiang) <caisenfull@outlook.com>\n基于 FluentAvalonia 与 ClassIsland 生态打造。",
    okText: "关闭",
  });
}

// 回到电脑版：只改查询参数，路径原样保留——重拼整个 URL 会丢接入码后的斜杠，
// 相对引用的静态资源就会 404 导致白屏（PC 端切换按钮踩过同一个坑）。
function goPc(): void {
  haptic(15);
  const q = new URLSearchParams(location.search);
  q.set("ui", "pc");
  location.search = q.toString();
}

onMounted(() => {
  // Aurora 开关随实际状态走（其它页面改过外观的话，这里以全局类名为准）
  auroraOn.value = document.querySelector(".stage")?.classList.contains("aurora-on") ?? true;
});
</script>

<template>
  <section class="page active">
    <div style="display:flex;align-items:center;gap:6px;margin-bottom:10px">
      <button class="barbtn" title="返回主页" @click="emit('go', 'home')"><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="m15 18-6-6 6-6"/></svg></button>
      <h1 style="margin:0;font-size:18px">设置</h1>
    </div>

    <div class="profile-hero">
      <div class="big-avatar">{{ avatarChar }}</div>
      <div style="flex:1">
        <div style="display:flex;align-items:center;gap:6px">
          <b style="font-size:15px">{{ name }}</b>
          <span class="tag" style="background:var(--accent-light);color:var(--accent);border:0;font-weight:600">已登记</span>
        </div>
        <div style="font-size:11px;color:var(--text-2);margin-top:2px">设备标识：<code style="font-family:monospace;font-size:10.5px">{{ deviceUid }}</code></div>
      </div>
      <button class="btn std" style="width:auto;min-height:30px;padding:0 11px;font-size:11.5px" @click="emit('go', 'oobe')">修改称谓</button>
    </div>

    <div class="group">
      <div class="ghead">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-.33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>
        通用
      </div>
      <div class="row">
        <div class="tx"><b>呼叫确认弹窗</b><small>点击呼叫前进行二次确认防止误触</small></div>
        <label class="switchrow" style="margin:0"><input v-model="confirmOn" type="checkbox" @change="savePref('rb_confirm', confirmOn)" /><span class="sw"></span></label>
      </div>
      <div class="row">
        <div class="tx"><b>自动清空已选</b><small>呼叫成功后自动重置学生已勾选项</small></div>
        <label class="switchrow" style="margin:0"><input v-model="autoclearOn" type="checkbox" @change="savePref('rb_autoclear', autoclearOn)" /><span class="sw"></span></label>
      </div>
      <div class="row">
        <div class="tx"><b>操作触觉振动反馈</b><small>在支持的移动终端上提供轻触振动感应</small></div>
        <label class="switchrow" style="margin:0"><input v-model="hapticsOn" type="checkbox" @change="savePref('rb_haptics', hapticsOn)" /><span class="sw"></span></label>
      </div>
    </div>

    <div class="group">
      <div class="ghead">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 2.69l5.66 5.66a8 8 0 1 1-11.31 0z"/></svg>
        外观
      </div>
      <div class="row">
        <div class="tx"><b>主题模式</b><small>手动选择明暗外观，或跟随系统自动切换</small></div>
        <div class="segmented-mini">
          <button :class="{ on: theme === 'auto' }" @click="pickTheme('auto')">跟随系统</button>
          <button :class="{ on: theme === 'light' }" @click="pickTheme('light')">浅色</button>
          <button :class="{ on: theme === 'dark' }" @click="pickTheme('dark')">深色</button>
        </div>
      </div>
      <div class="row">
        <div class="tx"><b>流光背景动态效果</b><small>启用 Fluent Aurora 流光动画（移动端已按性能优化，可随时关闭）</small></div>
        <label class="switchrow" style="margin:0"><input :checked="auroraOn" type="checkbox" @change="switchAurora(($event.target as HTMLInputElement).checked)" /><span class="sw"></span></label>
      </div>
    </div>

    <div class="group">
      <div class="ghead">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>
        关于
      </div>
      <div class="row">
        <div class="tx"><b>ClassFabric Remote Broadcast</b><small>{{ version }}</small></div>
        <span class="tag">插件</span>
      </div>
      <div class="row">
        <div class="tx"><b>提醒音效</b><small>在白板端「设置 → 提醒 → 远程广播」配置（官方默认音 / 自定义音频）</small></div>
      </div>
      <button class="row" @click="openAuthor">
        <div class="tx"><b>作者与开源生态</b><small>Ansoukin (Shuai Jiang) · GitHub 仓库</small></div>
        <svg class="chev" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="m9 6 6 6-6 6"/></svg>
      </button>
      <button class="row" @click="emit('go', 'oobe')">
        <div class="tx"><b>重播入门向导</b><small>重新查看三步登记向导</small></div>
        <svg class="chev" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="m9 6 6 6-6 6"/></svg>
      </button>
      <button class="row" @click="goPc">
        <div class="tx"><b>切换到电脑版页面</b><small>工作台信息更全，适合平板或大屏操作</small></div>
        <svg class="chev" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="m9 6 6 6-6 6"/></svg>
      </button>
    </div>
  </section>
</template>
