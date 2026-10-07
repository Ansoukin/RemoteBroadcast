<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from "vue";
import { matchStudent, postQuickCall, type DestinationDto, type TeacherDto } from "../../shared/api";
import { app } from "../../shared/store";
import { broadcast } from "../../shared/status";
import { PREF, haptic } from "../../shared/prefs";
import { estimateBodySeconds } from "../../shared/estimate";
import { showDialog, toast } from "../../shared/ui";
import { callBar, hideCallBar, setCallBar } from "../callbar";

const emit = defineEmits<{ go: [page: "home"] }>();

// ---- 名单与选择 ----
const keyword = ref("");
const selected = reactive(new Set<string>());
const matched = computed(() => (app.config?.roster ?? []).filter(s => matchStudent(s, keyword.value.trim())));

// ---- 目的地（★ 置顶来自历史常用记忆，localStorage 本机保存） ----
const destChosen = ref("");
const destCustom = ref("");
const pinnedNames = ref<string[]>(JSON.parse(localStorage.getItem("rb_destpin") || "[]"));

const destList = computed<DestinationDto[]>(() => app.config?.destinations ?? []);
const pinnedFirst = computed(() => {
  const byName = new Map(destList.value.map(d => [d.name, d]));
  const pinned = pinnedNames.value.map(n => byName.get(n)).filter(Boolean) as DestinationDto[];
  const rest = destList.value.filter(d => !pinnedNames.value.includes(d.name));
  return [...pinned, ...rest];
});
function togglePin(name: string): void {
  const i = pinnedNames.value.indexOf(name);
  if (i >= 0) pinnedNames.value.splice(i, 1);
  else pinnedNames.value.unshift(name);
  localStorage.setItem("rb_destpin", JSON.stringify(pinnedNames.value.slice(0, 8)));
  haptic(15);
  toast(i >= 0 ? "已取消置顶" : "已置顶常用", name, 1800);
}

// ---- 接洽人（三段：自己 / 其它老师 / 不指定） ----
const contactMode = ref<"self" | "teacher" | "none">("self");
const contactTeacher = ref("");
const teachers = computed<TeacherDto[]>(() => app.config?.teachers ?? []);
const teachersSource = computed(() => app.config?.teachersSource ?? "");
watch(teachers, list => {
  if (!contactTeacher.value && list.length > 0) contactTeacher.value = list[0].name;
}, { immediate: true });

const contactTip = computed(() => {
  if (contactMode.value === "self") return `大屏播报将附加「找${name || "我"}」`;
  if (contactMode.value === "teacher") return contactTeacher.value ? `大屏播报将附加「找${contactTeacher.value}」` : "请选择一位老师";
  return "播报将不包含「找某人」（自动改用无接洽人句式）";
});

// ---- 循环次数（跟随后端配置，可临时覆盖） ----
const serverLoop = computed(() => app.config?.loopCount ?? 2);
const loopOverride = ref<number | null>(null);
const loopText = computed(() => (loopOverride.value ?? serverLoop.value) + " 轮");
function adjustLoop(delta: number): void {
  haptic(15);
  loopOverride.value = Math.max(1, Math.min(5, (loopOverride.value ?? serverLoop.value) + delta));
}

// ---- 自定义广播 ----
const customTitle = ref("");
const customBody = ref("");
const titleDuration = ref(3);
const bodyDuration = ref<number | null>(null); // null = 自动（按语音时长收尾）
function adjustTitleDuration(delta: number): void {
  haptic(15);
  titleDuration.value = Math.max(1, Math.min(15, titleDuration.value + delta));
}
function adjustBodyDuration(delta: number): void {
  haptic(15);
  // 手动区间 3–60s 独立可调，不把 3s 折叠成自动；从自动一步进两端（+到3s / −到60s）
  if (bodyDuration.value == null) {
    bodyDuration.value = delta > 0 ? 3 : 60;
    return;
  }
  bodyDuration.value = Math.max(3, Math.min(60, bodyDuration.value + delta));
}
const effectiveBodySeconds = computed(() => bodyDuration.value ?? estimateBodySeconds(customBody.value));
const TEMPLATES = [
  { icon: "edit", label: "收作业", title: "作业通知", body: "请全体同学保持安静，课代表收齐各组作业。" },
  { icon: "task", label: "值日提醒", title: "值日提醒", body: "请值日生放学后及时关好窗户并清理黑板讲台。" },
  { icon: "users", label: "清点人数", title: "人数清点", body: "各组组长请迅速清点本组人数，并在课间上报。" },
  { icon: "bell", label: "集合通知", title: "集合通知", body: "请班干部在午休后到讲台前集合并安排自习纪律。" },
] as const;
function applyTemplate(i: number): void {
  haptic(20);
  customTitle.value = TEMPLATES[i].title;
  customBody.value = TEMPLATES[i].body;
  toast("模板已填入", `${TEMPLATES[i].label}：可再微调或直接发起广播`);
}

const bodyRolling = computed(() => customBody.value.trim().length > 16);

// ---- 名单虚拟滚动：只渲染可视窗口行，上下翻自动加载/卸载 ----
const ROW_H = 42;
const listEl = ref<HTMLElement | null>(null);
const win = reactive({ start: 0, end: 0 });
const topPad = ref(0);
const bottomPad = ref(0);
let rosterObserver: ResizeObserver | null = null;

function updateWindow(): void {
  const box = listEl.value;
  if (!box || matched.value.length === 0) {
    win.start = win.end = 0;
    topPad.value = bottomPad.value = 0;
    return;
  }
  // v-show 刚切回/首帧时 clientHeight 可能还是 0（布局未跑），按 6 行兜底估一版；
  // 等真正布局完成，scroll/observer 会带着真实高度再算一遍。
  const visible = Math.min(12, Math.max(1, Math.ceil((box.clientHeight || 6 * ROW_H) / ROW_H))) + 4;
  const start = Math.max(0, Math.floor(box.scrollTop / ROW_H) - 2);
  const end = Math.min(matched.value.length, start + visible + 2);
  win.start = start;
  win.end = end;
  topPad.value = start * ROW_H;
  bottomPad.value = (matched.value.length - end) * ROW_H;
}

// ---- 连选手势：长按 260ms 进入；方向由首个学生的状态决定（未选→连选加，已选→连选取消） ----
let holdTimer: number | null = null;
let batchActive = false;
let batchMode: "add" | "remove" = "add";
let batchPoint = { x: 0, y: 0 };
let batchRaf = 0;
const AUTO_EDGE = 48;

function onRowPointerDown(ev: PointerEvent, id: string): void {
  batchPoint = { x: ev.clientX, y: ev.clientY };
  holdTimer = window.setTimeout(() => {
    batchActive = true;
    batchMode = selected.has(id) ? "remove" : "add";
    if (batchMode === "add") selected.add(id);
    else selected.delete(id);
    haptic(20);
    updateSummary();
    startAutoScroll();
  }, 260);
}
function onRowPointerUp(): void {
  if (holdTimer !== null) {
    clearTimeout(holdTimer);
    holdTimer = null;
  }
}

// 命中检测挂在 document 层：滑到哪儿改到哪儿；连选期间拦截滚动，翻页交给自动滚动
function batchHitTest(): void {
  const el = document.elementFromPoint(batchPoint.x, batchPoint.y);
  const row = el && (el as HTMLElement).closest ? (el as HTMLElement).closest(".lvi") : null;
  const id = row && (row as HTMLElement).dataset.id;
  if (!id) return;
  const isSel = selected.has(id);
  if ((batchMode === "add" && !isSel) || (batchMode === "remove" && isSel)) {
    if (batchMode === "add") selected.add(id);
    else selected.delete(id);
    haptic(10);
    updateSummary();
  }
}
function onDocPointerMove(ev: PointerEvent): void {
  if (!batchActive) return;
  batchPoint = { x: ev.clientX, y: ev.clientY };
  batchHitTest();
}
function onDocPointerUp(): void {
  if (batchActive) {
    batchActive = false;
    updateSummary();
  }
}
function onDocTouchMove(ev: TouchEvent): void {
  if (batchActive) ev.preventDefault();
}

// 指针压住名单上/下边缘 48px 时持续翻动，越贴近边缘越快；滚动驱动虚拟窗口增删行
function autoScrollTick(): void {
  if (!batchActive) {
    batchRaf = 0;
    return;
  }
  const box = listEl.value;
  if (box) {
    const rect = box.getBoundingClientRect();
    const fromTop = batchPoint.y - rect.top;
    const fromBottom = rect.bottom - batchPoint.y;
    const speed = (dist: number) => 2 + Math.min(1, dist / AUTO_EDGE) * 12;
    let dy = 0;
    if (fromTop < AUTO_EDGE && fromTop >= -ROW_H) dy = -speed(AUTO_EDGE - fromTop);
    else if (fromBottom < AUTO_EDGE && fromBottom >= -ROW_H) dy = speed(AUTO_EDGE - fromBottom);
    if (dy) {
      box.scrollTop = Math.max(0, Math.min(box.scrollHeight - box.clientHeight, box.scrollTop + dy));
      batchHitTest();
    }
  }
  batchRaf = requestAnimationFrame(autoScrollTick);
}
function startAutoScroll(): void {
  if (!batchRaf) batchRaf = requestAnimationFrame(autoScrollTick);
}

function toggleRow(id: string): void {
  haptic(15);
  if (selected.has(id)) selected.delete(id);
  else selected.add(id);
  updateSummary();
}
function selectAll(): void {
  haptic(20);
  matched.value.forEach(s => selected.add(s.id));
  updateSummary();
}
function clearSelected(): void {
  haptic(20);
  selected.clear();
  updateSummary();
}

// ---- 呼叫条汇总 ----
const name = PREF("rb_name", "");
const activeTab = ref<"stu" | "custom">("stu");
const summary = computed(() => {
  if (activeTab.value === "stu") {
    const dest = destCustom.value.trim() || destChosen.value;
    return `已选 ${selected.size} 人 · 到 ${dest || "…"}`;
  }
  return customBody.value.trim().length > 0
    ? `标题${titleDuration.value}s · 正文${bodyDuration.value == null ? `自动（约${Math.ceil(estimateBodySeconds(customBody.value))}s）` : bodyDuration.value + "s"}`
    : "请输入通知标题与正文";
});
function updateSummary(): void {
  setCallBar(summary.value, submit);
}
watch(summary, updateSummary);
watch(activeTab, () => {
  haptic(18);
  updateSummary();
  if (activeTab.value === "stu") nextTick(updateWindow);
});
// 名单异步到达/搜索过滤后重算虚拟窗口——初始 win={0,0} 渲染空窗口，不重算就是空名单盒死锁
watch(matched, () => nextTick(updateWindow), { immediate: true });

// ---- 提交 ----
let submitting = false;
async function submit(): Promise<void> {
  if (submitting) return;
  haptic(30);

  if (broadcast.state === "Broadcasting") {
    toast("有播报正在进行", "请稍候再试");
    return;
  }

  const isStu = activeTab.value === "stu";
  const dest = destCustom.value.trim() || destChosen.value;
  if (isStu) {
    if (selected.size === 0) { toast("请至少选择一名学生", "", 2200); return; }
    if (!dest) { toast("请选择或输入目的地", "", 2200); return; }
  } else if (customBody.value.trim().length < 2) {
    toast("请输入有效的通知正文（至少 2 字）", "", 2200);
    return;
  }

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

  const summaryText = isStu
    ? `已选 ${selected.size} 名学生，目的地「${dest}」`
    : `将向大屏投送自定义广播「${customTitle.value.trim() || "通知"}」`;

  const confirmed = PREF("rb_confirm", "0") === "1"
    ? await showDialog({
        title: "确认发起呼叫",
        body: `${summaryText}\n白板将立即弹条并语音播报。`,
        okText: "确认呼叫",
        cancelText: "取消",
      })
    : true;
  if (!confirmed) return;

  submitting = true;
  callBar.busy = true;
  try {
    await postQuickCall(payload);
    haptic(60);
    if (isStu) {
      toast("已呼叫，请留意白板播报", "", 3000);
      if (PREF("rb_autoclear", "1") === "1") {
        selected.clear();
        // 呼叫过的目的地自动记为常用（本机记忆，上限 8 个）
        if (!pinnedNames.value.includes(dest)) {
          pinnedNames.value.unshift(dest);
          pinnedNames.value = pinnedNames.value.slice(0, 8);
          localStorage.setItem("rb_destpin", JSON.stringify(pinnedNames.value));
        }
      }
    } else {
      toast("广播已投送", "白板将先展示标题，再展示正文", 3000);
    }
  } catch (e) {
    haptic(40);
    toast("呼叫失败", e instanceof Error ? e.message : "未知错误", 4000);
  } finally {
    submitting = false;
    callBar.busy = false;
  }
}

onMounted(() => {
  document.addEventListener("pointermove", onDocPointerMove);
  document.addEventListener("pointerup", onDocPointerUp);
  document.addEventListener("pointercancel", onDocPointerUp);
  document.addEventListener("touchmove", onDocTouchMove, { passive: false });
  updateSummary();
  // 布局稳定后量一次名单盒高度；旋转/分屏等尺寸变化由 ResizeObserver 兜底重算
  nextTick(updateWindow);
  rosterObserver = new ResizeObserver(() => updateWindow());
  if (listEl.value) rosterObserver.observe(listEl.value);
});
onBeforeUnmount(() => {
  document.removeEventListener("pointermove", onDocPointerMove);
  document.removeEventListener("pointerup", onDocPointerUp);
  document.removeEventListener("pointercancel", onDocPointerUp);
  document.removeEventListener("touchmove", onDocTouchMove);
  if (batchRaf) cancelAnimationFrame(batchRaf);
  if (holdTimer !== null) clearTimeout(holdTimer);
  if (rosterObserver) {
    rosterObserver.disconnect();
    rosterObserver = null;
  }
  hideCallBar();
});
</script>

<template>
  <!-- id 对齐 mobile.css 的 #p-call 自适应规则：名单盒弹性吃满剩余高度，可见人数随屏幕动态决策 -->
  <section class="page active" id="p-call" style="overflow:hidden">
    <div style="display:flex;align-items:center;gap:6px;margin-bottom:8px">
      <button class="barbtn" title="返回主页" @click="emit('go', 'home')"><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="m15 18-6-6 6-6"/></svg></button>
      <h1 style="margin:0;font-size:18px">Quick Call</h1>
    </div>
    <div class="segmented-tab">
      <button class="tab-btn" :class="{ on: activeTab === 'stu' }" @click="activeTab = 'stu'">呼叫学生</button>
      <button class="tab-btn" :class="{ on: activeTab === 'custom' }" @click="activeTab = 'custom'">自定义内容呼叫</button>
    </div>

    <!-- 分区 A：呼叫学生。名单盒弹性吃满剩余高度，可见人数随屏幕动态决策 -->
    <div v-show="activeTab === 'stu'" id="section-stu" style="flex:1 1 0;min-height:0;display:flex;flex-direction:column;overflow-y:auto">
      <div class="tbox withicon" style="margin-bottom:4px">
        <svg class="lic" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>
        <input v-model="keyword" type="text" placeholder="搜索学生姓名、首字（如：陈）或学号（如：01）…" @input="updateWindow" />
      </div>
      <div style="font-size:10.5px;color:var(--text-3);margin:0 2px 7px">提示：按住学生滑动连续勾选（按住已选学生则连续取消），滑到列表上/下边缘自动翻页</div>

      <div class="group roster-group" style="margin-bottom:10px">
        <div class="roster-header">
          <b>学生名单（{{ matched.length }} 人）</b>
          <div style="display:flex;gap:6px">
            <button class="btn std" style="width:auto;min-height:27px;padding:0 8px;font-size:11px" @click="selectAll">全选</button>
            <button class="btn std" style="width:auto;min-height:27px;padding:0 8px;font-size:11px" @click="clearSelected">清空</button>
          </div>
        </div>
        <div ref="listEl" class="roster-scroll-box" @scroll="updateWindow">
          <div class="vl-spacer" :style="{ height: topPad + 'px' }"></div>
          <div
            v-for="s in matched.slice(win.start, win.end)"
            :key="s.id"
            class="lvi"
            :class="{ sel: selected.has(s.id) }"
            :data-id="s.id"
            @pointerdown="onRowPointerDown($event, s.id)"
            @pointerup="onRowPointerUp"
            @pointercancel="onRowPointerUp"
            @click="toggleRow(s.id)"
            @contextmenu.prevent
          >
            <span class="nm">{{ s.name }}</span>
            <span class="subinfo">学号 {{ s.no }}{{ s.group ? " · " + s.group : "" }}</span>
            <span class="chk">{{ selected.has(s.id) ? "✓" : "" }}</span>
          </div>
          <div v-if="matched.length === 0" style="text-align:center;padding:22px 0;color:var(--text-3);font-size:12.5px">无匹配学生（可搜姓名、首字或学号）</div>
          <div class="vl-spacer" :style="{ height: bottomPad + 'px' }"></div>
        </div>
      </div>

      <div class="group" style="padding:9px 11px;margin-bottom:10px">
        <div style="display:flex;align-items:center;gap:6px;font-size:12.5px;font-weight:600;margin-bottom:7px">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M12 2a8 8 0 0 0-8 8c0 5.25 8 12 8 12s8-6.75 8-12a8 8 0 0 0-8-8z"/><circle cx="12" cy="10" r="3"/></svg>
          前往目的地
        </div>
        <div class="dgrid">
          <button
            v-for="d in pinnedFirst"
            :key="d.id"
            class="dtile"
            :class="{ on: destChosen === d.name }"
            @click="destChosen = d.name; destCustom = ''"
            @contextmenu.prevent="togglePin(d.name)"
          >
            <span class="ic" :class="{ star: pinnedNames.includes(d.name) }">
              <svg v-if="pinnedNames.includes(d.name)" width="13" height="13" viewBox="0 0 24 24" fill="#E9A700" stroke="#E9A700" stroke-width="1" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z"/></svg>
              <svg v-else width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2a8 8 0 0 0-8 8c0 5.25 8 12 8 12s8-6.75 8-12a8 8 0 0 0-8-8z"/><circle cx="12" cy="10" r="3"/></svg>
            </span>
            <b>{{ d.name }}</b>
          </button>
        </div>
        <div class="tbox" style="margin-top:7px">
          <input v-model="destCustom" type="text" maxlength="50" placeholder="输入临时自定义目的地，例如：图书馆二楼…" @input="destChosen = ''" />
        </div>
        <div style="font-size:10.5px;color:var(--text-3);margin:5px 2px 0">★ 为常用置顶；呼叫过的目的地会自动记忆（长按可手动置顶/取消）</div>

        <!-- 指定接洽人：三段选择 -->
        <div style="display:flex;align-items:center;justify-content:space-between;padding-top:9px;margin-top:9px;border-top:1px dashed var(--stroke)">
          <div style="font-size:12.5px">
            <b>指定接洽人</b>
            <small style="display:block;color:var(--text-2);font-size:10.5px">{{ contactTip }}</small>
          </div>
          <div class="segmented-mini">
            <button :class="{ on: contactMode === 'self' }" @click="contactMode = 'self'; haptic(15)">我自己</button>
            <button :class="{ on: contactMode === 'teacher' }" @click="contactMode = 'teacher'; haptic(15)">其它老师</button>
            <button :class="{ on: contactMode === 'none' }" @click="contactMode = 'none'; haptic(15)">不指定</button>
          </div>
        </div>
        <div v-if="contactMode === 'teacher'" style="margin-top:8px">
          <div class="hscroll" v-if="teachers.length > 0">
            <button v-for="t in teachers" :key="t.name" class="tchip" :class="{ on: contactTeacher === t.name }" @click="contactTeacher = t.name; haptic(12)">
              <b>{{ t.name }}</b>
              <small v-if="t.group">{{ t.group }}</small>
            </button>
          </div>
          <div v-else style="font-size:10.5px;color:var(--text-3);margin:2px 2px 0">
            暂无接洽人名单（{{ teachersSource }}）。可在白板端「远程广播 → 人员与目的地」添加，或在下方输入姓名。
          </div>
          <div class="tbox" style="margin-top:6px">
            <input v-model="contactTeacher" type="text" maxlength="20" placeholder="直接输入接洽人姓名（如：王老师）" />
          </div>
        </div>

        <div style="display:flex;align-items:center;justify-content:space-between;padding-top:9px;margin-top:9px;border-top:1px dashed var(--stroke)">
          <div style="font-size:12.5px">
            <b>语音播报循环次数</b>
            <small style="display:block;color:var(--text-2);font-size:10.5px">名单朗读的轮数（默认跟白板端配置）</small>
          </div>
          <div class="stepper">
            <button class="stepper-btn" @click="adjustLoop(-1)">−</button>
            <span class="stepper-val">{{ loopText }}</span>
            <button class="stepper-btn" @click="adjustLoop(1)">+</button>
          </div>
        </div>
      </div>
    </div>

    <!-- 分区 B：自定义内容呼叫 -->
    <div v-show="activeTab === 'custom'" id="section-custom" style="flex:1 1 0;min-height:0;overflow-y:auto">
      <div class="group">
        <div class="ghead">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>
          自定义通知内容
          <small>标题与正文将分两阶段投送大屏</small>
        </div>
        <div style="padding:11px 12px">
          <div class="tbox" style="margin-bottom:7px">
            <input v-model="customTitle" type="text" maxlength="30" placeholder="通知标题（遮罩阶段展示，选填，默认“通知”）" />
          </div>
          <div class="tbox">
            <textarea v-model="customBody" maxlength="120" placeholder="通知正文（悬浮条阶段展示，例如：请值日生放学后及时关窗并整理讲台…）"></textarea>
          </div>
          <div style="display:flex;align-items:center;justify-content:space-between;font-size:11px;color:var(--text-3);margin-top:5px">
            <span>快捷模板一键填入：</span>
            <span>{{ customBody.length }} / 120</span>
          </div>
          <div class="template-pills">
            <span v-for="(t, i) in TEMPLATES" :key="i" class="tpl-pill" @click="applyTemplate(i)">{{ t.label }}</span>
          </div>

          <div style="border-top:1px solid var(--stroke);margin-top:9px;padding-top:3px">
            <div style="display:flex;align-items:center;justify-content:space-between;padding:5px 0">
              <div style="font-size:12.5px">
                <b>标题（遮罩）持续</b>
                <small style="display:block;color:var(--text-2);font-size:10.5px">大屏主题色标题条的展示时长</small>
              </div>
              <div class="stepper">
                <button class="stepper-btn" @click="adjustTitleDuration(-1)">−</button>
                <span class="stepper-val">{{ titleDuration }}s</span>
                <button class="stepper-btn" @click="adjustTitleDuration(1)">+</button>
              </div>
            </div>
            <div style="display:flex;align-items:center;justify-content:space-between;padding:5px 0;border-top:1px dashed var(--stroke)">
              <div style="font-size:12.5px">
                <b>正文（悬浮条）持续</b>
                <small style="display:block;color:var(--text-2);font-size:10.5px">自动=按语音长度收尾（当前约 {{ Math.ceil(estimateBodySeconds(customBody)) }}s）；手动 3–60 秒精确控制</small>
              </div>
              <div class="stepper">
                <button class="stepper-btn" @click="adjustBodyDuration(-1)">−</button>
                <span class="stepper-val">{{ bodyDuration == null ? "自动" : bodyDuration + "s" }}</span>
                <button class="stepper-btn" @click="adjustBodyDuration(1)">+</button>
              </div>
            </div>
            <div style="display:flex;align-items:center;justify-content:space-between;padding:5px 0;border-top:1px dashed var(--stroke)">
              <div style="font-size:12.5px">
                <b>语音播报循环次数</b>
                <small style="display:block;color:var(--text-2);font-size:10.5px">正文朗读的轮数</small>
              </div>
              <div class="stepper">
                <button class="stepper-btn" @click="adjustLoop(-1)">−</button>
                <span class="stepper-val">{{ loopText }}</span>
                <button class="stepper-btn" @click="adjustLoop(1)">+</button>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- 1:1 双阶段预览：标题条 → 悬浮条（超长自动走马灯 + 时长倒计时） -->
      <div class="cf-pc-stage">
        <div class="cf-mask-bar">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>
          <span>{{ customTitle.trim() || "通知" }}</span>
        </div>
        <div class="cf-overlay-bar" :class="{ rolling: bodyRolling }">
          <span class="cf-overlay-text">{{ customBody.trim() || "请在上方输入正文…" }}</span>
          <i class="cf-overlay-progress" :style="{ animationDuration: effectiveBodySeconds + 's' }"></i>
        </div>
      </div>
    </div>

    <div style="height:20px"></div>
  </section>
</template>
