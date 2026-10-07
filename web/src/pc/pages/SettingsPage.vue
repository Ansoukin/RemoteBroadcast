<script setup lang="ts">
import { ref } from "vue";
import { app } from "../../shared/store";
import { auroraEnabled, applyAurora, currentThemeMode, setThemeMode } from "../../shared/theme";
import { PREF, savePref } from "../../shared/prefs";
import { showDialog, toast } from "../../shared/ui";

const emit = defineEmits<{ replayOobe: []; editName: [] }>();

const name = PREF("rb_name", "");
const avatarChar = name.charAt(0) || "师";
const deviceUid = (localStorage.getItem("rb_device") || "").slice(0, 8).toUpperCase() || "--";
const versionText = app.config ? `版本 ${app.config.version} · 宿主 ${app.config.hostVersion}` : "版本 --";

// ---- 分区独立内容区：点左侧分区切换显示，每区一页（Win11 设置的同款结构） ----
const sections = [
  { id: "profile", label: "个人资料" },
  { id: "general", label: "通用" },
  { id: "appearance", label: "外观" },
  { id: "sound", label: "声音与播报" },
  { id: "about", label: "关于" },
] as const;
const active = ref<string>("profile");

// ---- 偏好 ----
const themeMode = ref(currentThemeMode());
function pickTheme(mode: "auto" | "light" | "dark"): void {
  themeMode.value = mode;
  setThemeMode(mode);
  toast("外观已切换", mode === "auto" ? "已跟随系统明暗" : mode === "light" ? "已切换为浅色" : "已切换为深色");
}

const confirmOn = ref(PREF("rb_confirm", "0") === "1");
const autoclearOn = ref(PREF("rb_autoclear", "1") === "1");
const hapticsOn = ref(PREF("rb_haptics", "1") === "1");
const auroraOn = ref(auroraEnabled());

function switchAurora(v: boolean): void {
  auroraOn.value = v;
  savePref("rb_aurora", v);
  applyAurora(v);
}

const AUTHOR_AVATAR = "https://avatars.githubusercontent.com/u/101161423?v=4";

async function openAuthor(): Promise<void> {
  await showDialog({
    title: "关于作者与生态",
    avatarUrl: AUTHOR_AVATAR,
    body: "Ansoukin (Shuai Jiang) <caisenfull@outlook.com>\n基于 FluentAvalonia 与 ClassIsland 生态打造。\n感谢 ClassIsland 及其社区提供的优秀生态与设计参考。",
    okText: "关闭",
  });
}

function openRepo(url: string): void {
  window.open(url, "_blank", "noopener");
}
</script>

<template>
  <section class="page active">
    <div class="setshell">
      <!-- 左列分区导航 -->
      <nav class="setnav">
        <div class="setnav-user">
          <span class="mini-avatar">{{ avatarChar }}</span>
          <span style="min-width:0">
            <b style="display:block;font-size:13px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">{{ name }}</b>
            <small style="color:var(--text-3);font-size:11px">已登记 · {{ deviceUid }}</small>
          </span>
        </div>
        <button
          v-for="s in sections"
          :key="s.id"
          class="setnav-item"
          :class="{ active: active === s.id }"
          @click="active = s.id"
        >
          <svg v-if="s.id === 'profile'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="8" r="4"/><path d="M5 21c0-3.9 3.1-7 7-7s7 3.1 7 7"/></svg>
          <svg v-else-if="s.id === 'general'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><rect x="3" y="5" width="18" height="6" rx="3"/><circle cx="7" cy="8" r="1.6"/><rect x="3" y="13" width="18" height="6" rx="3"/><circle cx="17" cy="16" r="1.6"/></svg>
          <svg v-else-if="s.id === 'appearance'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M12 2.7l5.7 5.7a8 8 0 1 1-11.4 0z"/></svg>
          <svg v-else-if="s.id === 'sound'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M3 10v4l9 4V6l-9 4z"/><path d="M14 7.5c1.6 1 2.5 2.6 2.5 4.5s-.9 3.5-2.5 4.5"/></svg>
          <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><path d="M12 16v-4M12 8h.01"/></svg>
          {{ s.label }}
        </button>
      </nav>

      <!-- 右区：当前分区的独立内容区 -->
      <div class="setcontent">
        <section v-show="active === 'profile'" class="sec">
          <h2 class="sec-title">个人资料</h2>
          <div class="wcard">
            <div class="wrow" style="min-height:76px">
              <span class="big-avatar">{{ avatarChar }}</span>
              <div style="min-width:0">
                <div style="display:flex;align-items:center;gap:8px">
                  <b style="font-size:15px">{{ name }}</b>
                  <span class="result-chip" style="background:var(--accent-light);color:var(--accent);font-weight:600">已登记</span>
                </div>
                <div style="font-size:12px;color:var(--text-2);margin-top:2px">设备标识：<code style="font-family:monospace;font-size:11px">{{ deviceUid }}</code></div>
              </div>
              <div class="ctrl"><button class="btn std" @click="emit('editName')">修改称谓</button></div>
            </div>
          </div>
        </section>

        <section v-show="active === 'general'" class="sec">
          <h2 class="sec-title">通用</h2>
          <div class="wcard">
            <div class="wrow">
              <div><div class="k">呼叫确认弹窗</div><div class="d">点击呼叫前进行二次确认，防止误触</div></div>
              <div class="ctrl"><label class="switch"><input v-model="confirmOn" type="checkbox" @change="savePref('rb_confirm', confirmOn)" /><i></i></label></div>
            </div>
            <div class="wrow">
              <div><div class="k">自动清空已选</div><div class="d">呼叫成功后自动重置学生已勾选项</div></div>
              <div class="ctrl"><label class="switch"><input v-model="autoclearOn" type="checkbox" @change="savePref('rb_autoclear', autoclearOn)" /><i></i></label></div>
            </div>
            <div class="wrow">
              <div><div class="k">操作触觉振动反馈</div><div class="d">在支持的触控设备上提供轻触振动；此偏好同时作用于手机页</div></div>
              <div class="ctrl"><label class="switch"><input v-model="hapticsOn" type="checkbox" @change="savePref('rb_haptics', hapticsOn)" /><i></i></label></div>
            </div>
          </div>
        </section>

        <section v-show="active === 'appearance'" class="sec">
          <h2 class="sec-title">外观</h2>
          <div class="wcard">
            <div class="wrow">
              <div><div class="k">主题模式</div><div class="d">手动选择明暗外观，或跟随系统自动切换</div></div>
              <div class="ctrl">
                <div class="seg mini">
                  <button :class="{ on: themeMode === 'auto' }" @click="pickTheme('auto')">跟随系统</button>
                  <button :class="{ on: themeMode === 'light' }" @click="pickTheme('light')">浅色</button>
                  <button :class="{ on: themeMode === 'dark' }" @click="pickTheme('dark')">深色</button>
                </div>
              </div>
            </div>
            <div class="wrow">
              <div><div class="k">流光背景动态效果</div><div class="d">让背景 Aurora 渐变流动。默认关闭以保证交互流畅，静态渐变底不受影响</div></div>
              <div class="ctrl"><label class="switch"><input :checked="auroraOn" type="checkbox" @change="switchAurora(($event.target as HTMLInputElement).checked)" /><i></i></label></div>
            </div>
          </div>
        </section>

        <section v-show="active === 'sound'" class="sec">
          <h2 class="sec-title">声音与播报</h2>
          <div class="wcard">
            <div class="wrow">
              <div style="min-width:0">
                <div class="k">播报音色 / 循环默认值 / 提醒音效</div>
                <div class="d">在白板端配置：ClassFabric 设置 → 远程广播（提醒音支持官方默认音与自定义音频）</div>
              </div>
              <span class="tag">白板端</span>
            </div>
          </div>
        </section>

        <section v-show="active === 'about'" class="sec">
          <h2 class="sec-title">关于</h2>
          <div class="wcard">
            <div class="wrow">
              <div><div class="k">ClassFabric Remote Broadcast</div><div class="d">{{ versionText }}</div></div>
              <span class="tag">插件</span>
            </div>
            <div class="wrow">
              <div><div class="k">重播入门向导</div><div class="d">重新查看首次打开的三步登记向导</div></div>
              <div class="ctrl"><button class="btn std" @click="emit('replayOobe')">重播向导</button></div>
            </div>
            <div class="wrow">
              <div><div class="k">作者与开源生态</div><div class="d">Ansoukin (Shuai Jiang) · GitHub 仓库</div></div>
              <div class="ctrl"><button class="btn std" @click="openAuthor">查看</button></div>
            </div>
            <div class="wrow">
              <div><div class="k">ClassFabric · 宿主项目</div><div class="d">远程广播依托的宿主框架 · github.com/ansoukin/ClassFabric</div></div>
              <div class="ctrl"><button class="btn std" @click="openRepo('https://github.com/ansoukin/ClassFabric')">前往</button></div>
            </div>
            <div class="wrow">
              <div><div class="k">ClassIsland · 生态致谢</div><div class="d">感谢 ClassIsland 生态与社区——本插件的架构与设计语言深受其启发</div></div>
              <div class="ctrl"><button class="btn std" @click="openRepo('https://github.com/ClassIsland/ClassIsland')">前往</button></div>
            </div>
            <div class="about-line" style="padding:12px 16px">远程广播 · 适用于班级多媒体大屏的课堂呼叫与通知投送工具</div>
          </div>
        </section>
      </div>
    </div>
  </section>
</template>

<style scoped>
/* Win11 设置应用结构：左列分区导航，右区一次只显示一个分区的独立内容区 */
.setshell { display: flex; gap: 22px; height: 100%; min-height: 0; max-width: 1100px; }
.setnav { width: 216px; flex: none; display: flex; flex-direction: column; gap: 2px; overflow-y: auto; }
.setnav-user { display: flex; align-items: center; gap: 10px; padding: 10px 12px; margin-bottom: 10px;
  background: var(--card); border: 1px solid var(--stroke); border-radius: var(--r-card); }
.mini-avatar { width: 36px; height: 36px; border-radius: 50%; background: var(--accent); color: #fff;
  display: grid; place-items: center; font-weight: 600; font-size: 14px; flex: none; }
:global(html[data-theme="dark"]) .mini-avatar { color: #000; }
.setnav-item { position: relative; display: flex; align-items: center; gap: 11px; width: 100%;
  padding: 9px 12px; border: 0; border-radius: 6px; background: transparent; color: var(--text);
  font: inherit; font-size: 13px; text-align: left; cursor: pointer; transition: background .12s ease; }
.setnav-item:hover { background: var(--ctrl-hover); }
.setnav-item.active { background: var(--accent-light); font-weight: 600; }
.setnav-item.active::before { content: ""; position: absolute; left: 0; top: 50%; transform: translateY(-50%);
  width: 3px; height: 16px; border-radius: 2px; background: var(--accent); }
.setnav-item svg { width: 16px; height: 16px; flex: none; }

.setcontent { flex: 1; min-width: 0; overflow-y: auto; padding: 2px 6px 20px 0; }
.sec { animation: secEnter .18s cubic-bezier(.1, .9, .2, 1); }
@keyframes secEnter { from { opacity: 0; transform: translateY(6px); } to { opacity: 1; transform: none; } }
.sec-title { font-size: 14.5px; font-weight: 600; margin: 2px 0 10px; }
.wcard { background: var(--card); border: 1px solid var(--stroke); border-radius: var(--r-card);
  box-shadow: var(--shadow-card); overflow: hidden; }
.wrow { display: flex; align-items: center; gap: 14px; min-height: 52px; padding: 10px 16px;
  border-bottom: 1px solid var(--stroke); }
.wrow:last-child { border-bottom: 0; }
.wrow .k { font-size: 13px; font-weight: 600; }
.wrow .d { font-size: 11.5px; color: var(--text-3); margin-top: 2px; line-height: 1.55; }
.wrow .ctrl { margin-left: auto; flex: none; }

/* 窄窗口：设置页纵向收拢，分区导航改横向换行（scoped 优先级高于全局媒体查询，故在此覆盖） */
@media (max-width: 860px) {
  .setshell { flex-direction: column; gap: 12px; }
  .setnav { width: 100%; flex-direction: row; overflow-x: auto; flex-wrap: wrap; }
  .setnav-user { display: none; }
  .setnav-item { width: auto; flex: none; }
}
</style>
