<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from "vue";
import { matchStudent, postQuickCall } from "../../shared/api";
import { app } from "../../shared/store";
import { broadcast, probeRtt } from "../../shared/status";
import { PREF, haptic } from "../../shared/prefs";
import { showDialog, toast } from "../../shared/ui";
import { resendRecord } from "../../shared/resend";
import { estimateBodySeconds } from "../../shared/estimate";
import FluentCombo from "../FluentCombo.vue";

const emit = defineEmits<{ go: [page: "history" | "settings"] }>();

const name = PREF("rb_name", "");
const avatarChar = name.charAt(0) || "师";
const deviceUid = (localStorage.getItem("rb_device") || "").slice(0, 8).toUpperCase() || "--";

// ---- 问候打字机（8 秒自动轮换，可手换） ----
const GREETINGS = [
  "欢迎使用远程广播服务。",
  "网络就绪后即可向大屏发起课堂呼叫。",
  "支持名单多选、批量勾选与自定义通知。",
  "请在课前或课间适时进行学生呼叫与通知。",
];
const greetText = ref("");
let greetIdx = 0;
let typeTimer: number | null = null;
let greetRotate: number | null = null;
function typeGreeting(text: string): void {
  if (typeTimer !== null) clearInterval(typeTimer);
  greetText.value = "";
  let i = 0;
  typeTimer = window.setInterval(() => {
    greetText.value = text.slice(0, ++i);
    if (i >= text.length && typeTimer !== null) {
      clearInterval(typeTimer);
      typeTimer = null;
    }
  }, 45);
}
function nextGreeting(): void {
  greetIdx = (greetIdx + 1) % GREETINGS.length;
  typeGreeting(GREETINGS[greetIdx]);
}

// ---- 名单与选择（按住左键滑过连续勾选；点击切换单个） ----
const keyword = ref("");
const selected = reactive(new Set<string>());
const matched = computed(() => (app.config?.roster ?? []).filter(s => matchStudent(s, keyword.value.trim())));
let batchActive = false;
let batchMode: "add" | "remove" = "add";

// 连选方向由按下的第一个学生决定：未选 → 连选加，已选 → 连选取消。
// 翻转动作只认 pointerdown：click 事件总在 pointerup（batchActive 已复位）之后到来，
// 若在 click 里再翻转一次，单击就变成「选中马上被取消」，这正是当年的选中失灵问题。
function onRowDown(id: string): void {
  batchActive = true;
  batchMode = selected.has(id) ? "remove" : "add";
  if (batchMode === "add") selected.add(id);
  else selected.delete(id);
  updateSummary();
}
function onRowEnter(id: string): void {
  if (!batchActive) return;
  if (batchMode === "add" && !selected.has(id)) {
    selected.add(id);
    updateSummary();
  } else if (batchMode === "remove" && selected.has(id)) {
    selected.delete(id);
    updateSummary();
  }
}
function selectAll(): void {
  matched.value.forEach(s => selected.add(s.id));
  updateSummary();
}
function clearSelected(): void {
  selected.clear();
  updateSummary();
}
function onDocUp(): void {
  batchActive = false;
}

// ---- 目的地（★ 置顶 + 呼叫记忆，本机保存） ----
const destChosen = ref("");
const destCustom = ref("");
const pinnedNames = ref<string[]>(JSON.parse(localStorage.getItem("rb_destpin") || "[]"));
const pinnedFirst = computed(() => {
  const list = app.config?.destinations ?? [];
  const pinned = pinnedNames.value
    .map(n => list.find(d => d.name === n))
    .filter(Boolean);
  const rest = list.filter(d => !pinnedNames.value.includes(d.name));
  return [...pinned, ...rest];
});
function togglePin(destName: string): void {
  const i = pinnedNames.value.indexOf(destName);
  if (i >= 0) pinnedNames.value.splice(i, 1);
  else pinnedNames.value.unshift(destName);
  localStorage.setItem("rb_destpin", JSON.stringify(pinnedNames.value.slice(0, 8)));
  toast(i >= 0 ? "已取消置顶" : "已置顶常用", destName, 1800);
}

// ---- 接洽人三段选择 ----
const contactMode = ref<"self" | "teacher" | "none">("self");
const contactTeacher = ref("");
const teachers = computed(() => app.config?.teachers ?? []);
const teacherOptions = computed(() =>
  teachers.value.map(t => ({ value: t.name, label: t.name, sub: t.group || "" })),
);
watch(teachers, list => {
  if (!contactTeacher.value && list.length > 0) contactTeacher.value = list[0].name;
}, { immediate: true });
const contactTip = computed(() => {
  if (contactMode.value === "self") return `大屏播报将附加「找${name}」`;
  if (contactMode.value === "teacher") return contactTeacher.value ? `大屏播报将附加「找${contactTeacher.value}」` : "请选择一位老师";
  return "播报将不包含「找某人」（自动改用无接洽人句式）";
});

// ---- 循环次数 ----
const serverLoop = computed(() => app.config?.loopCount ?? 2);
const loopOverride = ref<number | null>(null);
function adjustLoop(delta: number): void {
  loopOverride.value = Math.max(1, Math.min(5, (loopOverride.value ?? serverLoop.value) + delta));
}

// ---- 自定义广播 ----
const customTitle = ref("");
const customBody = ref("");
const titleDuration = ref(3);
const bodyDuration = ref<number | null>(null);
function adjustBodyDuration(delta: number): void {
  if (bodyDuration.value == null) {
    bodyDuration.value = delta > 0 ? 3 : 60;
    return;
  }
  bodyDuration.value = Math.max(3, Math.min(60, bodyDuration.value + delta));
}
// 预览与标注按「实际生效时长」算：自动=按字数估语音时长（与服务端同口径），手动=固定值
const effectiveBodySeconds = computed(() => bodyDuration.value ?? estimateBodySeconds(customBody.value));
const TEMPLATES = [
  { icon: "edit", label: "收作业", title: "作业通知", body: "请全体同学保持安静，课代表收齐各组作业。" },
  { icon: "task", label: "值日提醒", title: "值日提醒", body: "请值日生放学后及时关好窗户并清理黑板讲台。" },
  { icon: "users", label: "清点人数", title: "人数清点", body: "各组组长请迅速清点本组人数，并在课间上报。" },
  { icon: "bell", label: "集合通知", title: "集合通知", body: "请班干部在午休后到讲台前集合并安排自习纪律。" },
] as const;
function adjustTitleDuration(delta: number): void {
  titleDuration.value = Math.max(1, Math.min(15, titleDuration.value + delta));
}
function applyTemplate(i: number): void {
  customTitle.value = TEMPLATES[i].title;
  customBody.value = TEMPLATES[i].body;
  toast("模板已填入", `${TEMPLATES[i].label}：可再微调或直接发起广播`);
}
const bodyRolling = computed(() => customBody.value.trim().length > 16);

// ---- 双 Tab 表单 ----
const activeTab = ref<"stu" | "custom" | "voice">("stu");
const sumStu = ref("已选 0 人 · 到…");
const sumCustom = ref("请输入通知标题与正文");
function updateSummary(): void {
  const dest = destCustom.value.trim() || destChosen.value;
  sumStu.value = `已选 ${selected.size} 人 · 到${dest || "…"}`;
  sumCustom.value = customBody.value.trim().length > 0
    ? `标题${titleDuration.value}s · 正文${bodyDuration.value == null ? `自动（约${Math.ceil(estimateBodySeconds(customBody.value))}s）` : bodyDuration.value + "s"}`
    : "请输入通知标题与正文";
}
watch([destCustom, destChosen, customTitle, customBody, titleDuration, bodyDuration], updateSummary);

// ---- 提交 ----
let submitting = false;
async function submit(): Promise<void> {
  if (submitting) return;
  const isStu = activeTab.value === "stu";
  const dest = destCustom.value.trim() || destChosen.value;

  if (!name) {
    showFormError("尚未登记称谓，请刷新页面完成入门向导后再呼叫");
    return;
  }
  if (broadcast.state === "Broadcasting") {
    showFormError("有播报正在进行，请稍候");
    return;
  }
  if (isStu) {
    if (selected.size === 0) { showFormError("请至少选择一名学生"); return; }
    if (!dest) { showFormError("请选择或输入目的地"); return; }
  } else if (customBody.value.trim().length < 2) {
    showFormError("请输入有效的通知正文（至少 2 字）");
    return;
  }

  const summaryText = isStu
    ? `已选 ${selected.size} 名学生，目的地「${dest}」`
    : `将向大屏投送自定义广播「${customTitle.value.trim() || "通知"}」`;
  const confirmed = PREF("rb_confirm", "0") === "1"
    ? await showDialog({
        title: "确认发起呼叫",
        body: `${summaryText}\n白板将立即弹条并语音播报。`,
        okText: isStu ? "确认呼叫" : "确认投送",
        cancelText: "取消",
      })
    : true;
  if (!confirmed) return;

  const payload = isStu
    ? {
        students: [...selected],
        destination: dest,
        caller: name,
        contact: contactMode.value === "none" ? "" : contactMode.value === "teacher" ? contactTeacher.value : undefined,
        ...(loopOverride.value != null ? { loopCount: loopOverride.value } : {}),
      }
    : {
        mode: "custom" as const,
        title: customTitle.value.trim() || "通知",
        body: customBody.value.trim(),
        caller: name,
        titleDuration: titleDuration.value,
        ...(bodyDuration.value != null ? { bodyDuration: bodyDuration.value } : {}),
        ...(loopOverride.value != null ? { loopCount: loopOverride.value } : {}),
      };

  submitting = true;
  try {
    await postQuickCall(payload);
    formError.value = "";
    if (isStu) {
      toast("已呼叫，请留意白板播报", "", 3000);
      if (PREF("rb_autoclear", "1") === "1") {
        selected.clear();
        if (!pinnedNames.value.includes(dest)) {
          pinnedNames.value.unshift(dest);
          pinnedNames.value = pinnedNames.value.slice(0, 8);
          localStorage.setItem("rb_destpin", JSON.stringify(pinnedNames.value));
        }
        updateSummary();
      }
    } else {
      toast("广播已投送", "白板将先展示标题，再展示正文", 3000);
    }
  } catch (e) {
    showFormError(e instanceof Error ? e.message : "未知错误");
  } finally {
    submitting = false;
  }
}

const formError = ref("");
function showFormError(message: string): void {
  formError.value = message;
  window.setTimeout(() => { formError.value = ""; }, 3200);
}

// ---- 右列：实时状态卡 ----
const busy = computed(() => broadcast.state === "Broadcasting");
const errored = computed(() => broadcast.state === "Error");
const liveTitle = computed(() => (errored.value ? "异常" : busy.value ? (broadcast.stage === "speechStarted" ? "语音播报中" : "正在呼叫") : "空闲"));
const liveSub = computed(() => {
  if (errored.value) return broadcast.error || "播报出现问题";
  if (!busy.value) return "等待呼叫请求。发起后此处实时显示播报阶段（上屏 → 语音 → 完成）。";
  const cur = broadcast.current;
  if (broadcast.stage === "speechStarted") return "正文播报中，请留意白板。";
  return cur ? `${cur.caller} 正在呼叫${cur.destination ? `，目的地 ${cur.destination}` : ""}。` : "正在处理播报。";
});
const progressPct = computed(() => (busy.value ? (broadcast.stage === "speechStarted" ? 70 : broadcast.stage === "displayed" ? 40 : 18) : 0));

// ---- 右列：大屏连接 ----
const rtt = ref<number | null>(null);
let probeTimer: number | null = null;
async function probe(): Promise<void> {
  rtt.value = await probeRtt();
}

// ---- 右列：最近呼叫 ----
const recent = computed(() => app.history.slice(0, 3));
function resend(r: (typeof app.history)[number]): void {
  resendRecord(r, name);
}

onMounted(() => {
  document.addEventListener("pointerup", onDocUp);
  typeGreeting(GREETINGS[0]);
  // 问候轮换与 RTT 探测的定时器记到底，卸载时统一清——挂在 beforeunload 上清不掉
  // KeepAlive 缓存实例的定时器（关标签页才触发），切页次数多了定时器会越积越多。
  greetRotate = window.setInterval(() => {
    if (typeTimer === null) nextGreeting();
  }, 8000);
  probe();
  probeTimer = window.setInterval(probe, 30000);
  updateSummary();
});
onBeforeUnmount(() => {
  document.removeEventListener("pointerup", onDocUp);
  if (typeTimer !== null) clearInterval(typeTimer);
  if (greetRotate !== null) clearInterval(greetRotate);
  if (probeTimer !== null) clearInterval(probeTimer);
});
</script>

<template>
  <section class="page active">
    <!-- 配置未到时先出骨架：行卡脉冲占位，加载失败也照常渲染（右列离线状态如实显示） -->
    <template v-if="!app.loaded">
      <div class="skl" style="width:340px;height:15px;margin:2px 0 14px"></div>
      <div class="work-grid">
        <div class="card" style="padding:16px;display:flex;flex-direction:column;gap:12px">
          <div class="skl" style="height:13px;width:38%"></div>
          <div class="skl" style="height:42px"></div>
          <div class="skl" style="height:42px"></div>
          <div class="skl" style="height:42px;width:70%"></div>
          <div class="skl" style="height:13px;width:60%"></div>
        </div>
        <div class="side-col">
          <div class="card" style="padding:16px;display:flex;flex-direction:column;gap:10px">
            <div class="skl" style="height:13px;width:55%"></div>
            <div class="skl" style="height:13px;width:82%"></div>
            <div class="skl" style="height:13px;width:64%"></div>
          </div>
          <div class="card" style="padding:16px;display:flex;flex-direction:column;gap:10px">
            <div class="skl" style="height:13px;width:42%"></div>
            <div class="skl" style="height:44px"></div>
          </div>
        </div>
      </div>
    </template>
    <template v-else>
    <div class="greet-line">
      <span class="avatar">{{ avatarChar }}</span>
      <span>你好，<b>{{ name }}</b><span style="color:var(--text-3)"> · 设备 {{ deviceUid }}</span></span>
      <span style="flex:1"></span>
      <span><span>{{ greetText }}</span><span class="type-cursor"></span></span>
      <button class="btn std subtle" title="换一句" @click="nextGreeting">↻</button>
    </div>

    <div class="work-grid">
      <!-- 呼叫表单（双 Tab） -->
      <div class="card form-card">
        <div class="tabs">
          <button :class="{ on: activeTab === 'stu' }" @click="activeTab = 'stu'">呼叫学生</button>
          <button :class="{ on: activeTab === 'custom' }" @click="activeTab = 'custom'">自定义内容呼叫</button>
          <button :class="{ on: activeTab === 'voice' }" @click="activeTab = 'voice'">语音呼叫</button>
        </div>

        <!-- Tab A：呼叫学生 -->
        <div class="form-pane" :class="{ on: activeTab === 'stu' }">
          <div>
            <div class="tbox" style="position:relative">
              <svg style="position:absolute;left:11px;top:50%;transform:translateY(-50%);width:14px;height:14px;color:var(--text-3)" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>
              <input v-model="keyword" class="input" style="padding-left:33px" placeholder="搜索学生姓名、首字（如：陈）或学号（如：01）…" />
            </div>
            <div style="font-size:11px;color:var(--text-3);margin:6px 2px 8px">提示：按住鼠标左键滑过学生行可连续批量勾选；按住已选学生滑动则连续取消</div>
            <div class="roster-box">
              <div class="roster-head">
                <span>学生名单（{{ matched.length }} 人）</span>
                <div class="acts">
                  <button class="btn std" @click="selectAll">全选</button>
                  <button class="btn std" @click="clearSelected">清空</button>
                </div>
              </div>
              <div class="roster-list">
                <div
                  v-for="s in matched"
                  :key="s.id"
                  class="lvi"
                  :class="{ sel: selected.has(s.id) }"
                  @pointerdown="onRowDown(s.id)"
                  @mouseenter="onRowEnter(s.id)"
                >
                  <span class="nm">{{ s.name }}</span>
                  <span class="subinfo">学号 {{ s.no }}{{ s.group ? " · " + s.group : "" }}</span>
                  <span class="chk">{{ selected.has(s.id) ? "✓" : "" }}</span>
                </div>
                <div v-if="matched.length === 0" class="roster-empty">无匹配学生（可搜姓名、首字或学号）</div>
              </div>
            </div>
          </div>

          <div>
            <div class="step-h"><span class="no">1</span><span class="t">前往目的地</span><span class="hint">★ 常用置顶 · 呼叫过的目的地自动记忆（右键置顶/取消）</span></div>
            <div class="dest-grid" style="margin-top:8px">
              <button
                v-for="d in pinnedFirst"
                :key="d.id"
                class="dest"
                :class="{ on: destChosen === d.name }"
                @click="destChosen = d.name; destCustom = ''"
                @contextmenu.prevent="togglePin(d.name)"
              >
                <span class="pin" v-if="pinnedNames.includes(d.name)">★</span>
                <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 2a8 8 0 0 0-8 8c0 5.25 8 12 8 12s8-6.75 8-12a8 8 0 0 0-8-8z"/><circle cx="12" cy="10" r="3"/></svg>
                <span>{{ d.name }}</span>
              </button>
            </div>
            <input v-model="destCustom" class="input" style="margin-top:10px" maxlength="50" placeholder="输入临时自定义目的地，例如：图书馆二楼…" @input="destChosen = ''" />
          </div>

          <div>
            <div class="step-h"><span class="no">2</span><span class="t">指定接洽人</span><span class="hint">{{ contactTip }}</span></div>
            <div class="seg" style="margin-top:8px">
              <button :class="{ on: contactMode === 'self' }" @click="contactMode = 'self'">我自己</button>
              <button :class="{ on: contactMode === 'teacher' }" @click="contactMode = 'teacher'">指定其它老师</button>
              <button :class="{ on: contactMode === 'none' }" @click="contactMode = 'none'">不指定</button>
            </div>
            <div v-if="contactMode === 'teacher'" style="margin-top:10px">
              <div class="teacher-row" v-if="teachers.length > 0">
                <FluentCombo v-model="contactTeacher" :options="teacherOptions" placeholder="选择接洽老师" style="flex:1" />
              </div>
              <input v-else v-model="contactTeacher" class="input" style="margin-top:0" maxlength="20" placeholder="暂无名单，直接输入接洽人姓名（如：王老师）" />
              <div class="infobar info" style="margin-top:8px">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8v.01"/></svg>
                <span>名单来源：{{ app.config?.teachersSource || "未接入教师目录" }}；维护入口在白板端「远程广播 → 人员与目的地」。</span>
              </div>
            </div>
          </div>

          <div style="display:flex;align-items:center;justify-content:space-between;border-top:1px dashed var(--stroke);padding-top:12px">
            <div style="font-size:12.5px"><b>语音播报循环次数</b><div style="color:var(--text-2);font-size:11px;margin-top:1px">名单朗读的轮数（默认跟白板端配置）</div></div>
            <div class="stepper">
              <button @click="adjustLoop(-1)">−</button>
              <span class="val">{{ (loopOverride ?? serverLoop) + " 轮" }}</span>
              <button @click="adjustLoop(1)">+</button>
            </div>
          </div>

          <div class="callbar" style="position:static;margin-top:0">
            <div class="sum">{{ sumStu }}</div>
            <button class="btn accent" :disabled="submitting" @click="submit">立即呼叫</button>
          </div>
        </div>

        <!-- Tab B：自定义内容呼叫 -->
        <div class="form-pane" :class="{ on: activeTab === 'custom' }">
          <div>
            <div class="step-h"><span class="no">1</span><span class="t">通知内容</span><span class="hint">标题与正文分两阶段投送大屏</span></div>
            <input v-model="customTitle" class="input" maxlength="30" style="margin-top:8px" placeholder="通知标题（遮罩阶段展示，选填，默认“通知”）" />
            <textarea v-model="customBody" class="input" maxlength="120" style="margin-top:8px" placeholder="通知正文（悬浮条阶段展示，例如：请值日生放学后及时关窗并整理讲台…）"></textarea>
            <div style="display:flex;justify-content:space-between;font-size:11.5px;color:var(--text-3);margin-top:5px">
              <span>快捷模板一键填入：</span><span>{{ customBody.length }} / 120</span>
            </div>
            <div style="display:flex;gap:8px;flex-wrap:wrap;margin-top:8px">
              <button v-for="(t, i) in TEMPLATES" :key="i" class="btn std tpl-btn" @click="applyTemplate(i)">
                <svg v-if="t.icon === 'edit'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/></svg>
                <svg v-else-if="t.icon === 'task'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M9 11.5 12 14.5l8-8"/><path d="M20 12v6a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h9"/></svg>
                <svg v-else-if="t.icon === 'users'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
                <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.7 21a2 2 0 0 1-3.4 0"/></svg>
                {{ t.label }}
              </button>
            </div>
          </div>

          <div style="display:flex;flex-direction:column;gap:10px;border-top:1px dashed var(--stroke);padding-top:12px">
            <div style="display:flex;align-items:center;justify-content:space-between">
              <div style="font-size:12.5px"><b>标题（遮罩）持续</b><div style="color:var(--text-2);font-size:11px;margin-top:1px">大屏主题色标题条的展示时长</div></div>
              <div class="stepper"><button @click="adjustTitleDuration(-1)">−</button><span class="val">{{ titleDuration }}s</span><button @click="adjustTitleDuration(1)">+</button></div>
            </div>
            <div style="display:flex;align-items:center;justify-content:space-between">
              <div style="min-width:0"><b>正文（悬浮条）持续</b><div style="color:var(--text-2);font-size:11px;margin-top:1px">自动=按语音长度收尾（当前约 {{ Math.ceil(estimateBodySeconds(customBody)) }}s）；手动 3–60 秒精确控制</div></div>
              <div class="stepper"><button @click="adjustBodyDuration(-1)">−</button><span class="val">{{ bodyDuration == null ? "自动" : bodyDuration + "s" }}</span><button @click="adjustBodyDuration(1)">+</button></div>
            </div>
            <div style="display:flex;align-items:center;justify-content:space-between">
              <div style="font-size:12.5px"><b>语音播报循环次数</b><div style="color:var(--text-2);font-size:11px;margin-top:1px">正文朗读的轮数</div></div>
              <div class="stepper"><button @click="adjustLoop(-1)">−</button><span class="val">{{ (loopOverride ?? serverLoop) + " 轮" }}</span><button @click="adjustLoop(1)">+</button></div>
            </div>
          </div>

          <div>
            <div class="step-h"><span class="no">2</span><span class="t">大屏效果预览</span><span class="hint">1:1 还原两阶段形态</span></div>
            <div class="cf-pc-stage" style="margin-top:8px">
              <div class="cf-mask-bar">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>
                <span>{{ customTitle.trim() || "通知" }}</span>
              </div>
              <div class="cf-overlay-bar" :class="{ rolling: bodyRolling }" :style="bodyRolling ? { '--roll-dur': effectiveBodySeconds + 's' } : undefined">
                <span class="cf-overlay-text">{{ customBody.trim() || "请在上方输入正文…" }}</span>
                <i class="cf-overlay-progress" :style="{ animationDuration: effectiveBodySeconds + 's' }"></i>
              </div>
              <div class="stage-cap">▲ 模拟白板两阶段：本条播报预计展示约 {{ Math.ceil(effectiveBodySeconds) }} 秒（{{ bodyDuration == null ? "自动（跟随语音长度）" : "手动指定" }}）</div>
            </div>
          </div>

          <div class="callbar" style="position:static;margin-top:0">
            <div class="sum">{{ sumCustom }}</div>
            <button class="btn accent" :disabled="submitting" @click="submit">投送广播</button>
          </div>
        </div>

        <!-- Tab C：语音呼叫占位（服务端建设中） -->
        <div class="form-pane on" v-if="activeTab === 'voice'">
          <div class="voice-hold">
            <div class="vh-icon">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3z"/><path d="M19 10v1a7 7 0 0 1-14 0v-1"/><path d="M12 18v4"/></svg>
            </div>
            <div class="vh-title">语音呼叫 · 服务器基础建设中</div>
            <div class="vh-sub">该功能依赖语音对讲服务，暂不可用。详情请咨询管理员！</div>
            <div class="vh-tags">
              <span class="vh-tag">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><path d="M12 19v3"/></svg>
                实时对讲
              </span>
              <span class="vh-tag">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="m3 11 18-5v12L3 14v-3z"/><path d="M11.6 16.8a3 3 0 1 1-5.8-1.6"/></svg>
                语音投送
              </span>
              <span class="vh-tag">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>
                白板内网直达
              </span>
            </div>
          </div>
        </div>

        <div style="padding:0 16px 14px">
          <div class="infobar err" :class="{ show: formError }"><span>{{ formError }}</span></div>
        </div>
      </div>

      <!-- 右列 -->
      <div class="side-col">
        <div class="card live-state">
          <div class="ls-top"><span class="dot" :class="errored ? 'err' : busy ? 'ok pulse' : 'off'"></span><span>{{ liveTitle }}</span></div>
          <div class="ls-sub">{{ liveSub }}</div>
          <div class="progress" v-show="busy"><i :style="{ width: progressPct + '%' }"></i></div>
          <div class="stage-line">
            <span class="stage-tag" :class="{ on: busy && broadcast.stage !== null }">上屏</span>
            <span class="stage-tag" :class="{ on: busy && broadcast.stage === 'speechStarted' }">语音</span>
            <span class="stage-tag" :class="{ on: false }">完成</span>
          </div>
        </div>

        <div class="card conn-card">
          <div class="conn-infobar" v-if="rtt == null">白板连接异常，请检查宿主是否开启远程广播服务与网络</div>
          <div class="conn-head">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><rect x="3" y="4" width="18" height="12" rx="2"/><path d="M8 20h8M12 16v4"/></svg>
            <div class="conn-t"><b>大屏连接</b><small>ClassFabric 宿主 · 局域网</small></div>
            <span class="conn-badge" :class="rtt == null ? 'off' : 'ok'">{{ rtt == null ? "离线" : "在线" }}</span>
          </div>
          <div class="conn-row">
            <div class="conn-k"><b>连接延迟</b><small>每 30 秒自动测量一次</small></div>
            <div class="conn-v">
              <span class="num">{{ rtt == null ? "--" : rtt + " ms" }}</span>
              <button class="mini-ib" title="立即重测" @click="probe">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12a9 9 0 1 1-2.6-6.4"/><path d="M21 3v5h-5"/></svg>
              </button>
            </div>
          </div>
          <div class="conn-row">
            <div class="conn-k"><b>广播服务</b><small>呼叫受理与语音播报</small></div>
            <span class="pill-badge" :class="errored ? 'err' : 'ok'">{{ errored ? "异常" : "运行中" }}</span>
          </div>
          <div class="conn-row">
            <div class="conn-k"><b>实时通道</b><small>WebSocket 推送，断线自动转轮询</small></div>
            <span class="pill-badge" :class="broadcast.wsConnected ? 'ok' : 'warn'">{{ broadcast.wsConnected ? "已连接" : "轮询中" }}</span>
          </div>
        </div>

        <div class="card">
          <div class="chead">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3.5 2"/></svg>
            最近呼叫
            <span style="margin-left:auto;font-size:11px;color:var(--text-3);cursor:pointer" @click="emit('go', 'history')">查看全部 ›</span>
          </div>
          <div class="cbody" style="padding-top:6px">
            <div v-for="(r, i) in recent" :key="i" class="recent-item">
              <div>
                <div class="who">{{ r.students.length > 0 ? r.students.slice(0, 2).join("、") + (r.students.length > 2 ? " 等" : "") + " → " + r.destination : "「" + r.destination + "」" }}</div>
                <div class="meta">{{ r.caller }} · 接洽人：{{ r.contact == null || r.contact === "" ? "不指定" : r.contact }}</div>
              </div>
              <span class="result-chip" :class="r.result === '已完成' ? 'ok' : r.result === '已取消' ? 'cancel' : 'err'">{{ r.result === "已完成" ? "已完成" : r.result === "已取消" ? "已取消" : "失败" }}</span>
              <button v-if="r.students.length > 0" class="resend" @click="resend(r)">重发</button>
            </div>
            <div v-if="recent.length === 0" style="text-align:center;padding:14px 0;color:var(--text-3);font-size:12.5px">还没有呼叫记录</div>
          </div>
        </div>
      </div>
    </div>
    </template>
  </section>
</template>

<style scoped>
/* 骨架占位：呼吸式微光，动画跟随系统减少动效设置 */
.skl { border-radius: var(--r-ctrl); background: var(--item-hover);
  animation: sklPulse 1.6s ease-in-out infinite; }
@keyframes sklPulse { 0%, 100% { opacity: 1; } 50% { opacity: .45; } }
@media (prefers-reduced-motion: reduce) { .skl { animation: none; } }

/* 语音呼叫占位页：居中徽标 + 一句话说明，不做任何会动的装饰 */
.voice-hold { display: flex; flex-direction: column; align-items: center; text-align: center;
  padding: 44px 20px 52px; }
.vh-icon { width: 62px; height: 62px; border-radius: 16px; background: var(--accent-light);
  color: var(--accent); display: grid; place-items: center; }
.vh-icon svg { width: 30px; height: 30px; }
.vh-title { font-size: 15.5px; font-weight: 600; margin-top: 14px; }
.vh-sub { font-size: 12.5px; color: var(--text-2); margin-top: 6px; line-height: 1.6; max-width: 380px; }
.vh-tags { display: flex; gap: 8px; margin-top: 16px; flex-wrap: wrap; justify-content: center; }
.vh-tag { font-size: 11.5px; color: var(--text-3); border: 1px solid var(--stroke);
  border-radius: 999px; padding: 3px 12px; background: var(--layer);
  display: inline-flex; align-items: center; gap: 6px; }
.vh-tag svg { width: 12px; height: 12px; flex: none; }
.tpl-btn svg { width: 14px; height: 14px; }

/* 大屏连接卡：与设置页 wcard/wrow 同规格的行卡组；异常时顶部一条 InfoBar */
.conn-card { position: relative; overflow: hidden; }
.conn-infobar {
  display: flex; align-items: center; gap: 8px; padding: 8px 14px; font-size: 12px;
  background: var(--warn-bg); color: var(--warn); border-bottom: 1px solid var(--stroke);
}
.conn-head { display: flex; align-items: center; gap: 10px; padding: 12px 16px; border-bottom: 1px solid var(--stroke); }
.conn-head > svg { width: 16px; height: 16px; color: var(--accent); flex: none; }
.conn-t { min-width: 0; }
.conn-t b { display: block; font-size: 13.5px; font-weight: 600; }
.conn-t small { display: block; font-size: 11px; color: var(--text-3); margin-top: 1px; }
.conn-badge { margin-left: auto; flex: none; font-size: 11px; font-weight: 600; border-radius: 4px; padding: 2px 9px; }
.conn-badge.ok { background: var(--ok-bg); color: var(--ok); }
.conn-badge.off { background: var(--item-hover); color: var(--text-3); }
.conn-row { display: flex; align-items: center; gap: 12px; padding: 10px 16px; min-height: 46px; border-bottom: 1px solid var(--stroke); }
.conn-row:last-child { border-bottom: 0; }
.conn-k { min-width: 0; }
.conn-k b { display: block; font-size: 12.5px; font-weight: 600; }
.conn-k small { display: block; font-size: 11px; color: var(--text-3); margin-top: 1px; }
.conn-v { margin-left: auto; display: flex; align-items: center; gap: 8px; flex: none; }
.conn-v .num { font-size: 12.5px; font-weight: 600; font-variant-numeric: tabular-nums; }
.mini-ib { width: 26px; height: 26px; border: 0; border-radius: var(--r-ctrl); background: transparent;
  color: var(--text-2); display: grid; place-items: center; cursor: pointer; }
.mini-ib:hover { background: var(--ctrl-hover); color: var(--text); }
.mini-ib svg { width: 13px; height: 13px; }
.pill-badge { margin-left: auto; flex: none; font-size: 11.5px; font-weight: 600; border-radius: 4px; padding: 2px 9px; }
.pill-badge.ok { background: var(--ok-bg); color: var(--ok); }
.pill-badge.err { background: var(--error-bg); color: var(--error); }
.pill-badge.warn { background: var(--warn-bg); color: var(--warn); }
</style>
