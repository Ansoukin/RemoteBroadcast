<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from "vue";

export interface ComboOption {
  value: string;
  label: string;
  sub?: string;
}

const props = defineProps<{ options: ComboOption[]; modelValue: string; placeholder?: string }>();
const emit = defineEmits<{ "update:modelValue": [value: string] }>();

const open = ref(false);
const query = ref("");
const activeIdx = ref(0);
const root = ref<HTMLElement | null>(null);
const filterEl = ref<HTMLInputElement | null>(null);
const listEl = ref<HTMLElement | null>(null);

const list = computed(() => {
  const kw = query.value.trim();
  if (!kw) return props.options;
  return props.options.filter(o => o.label.includes(kw) || (o.sub ?? "").includes(kw));
});
const current = computed(() => props.options.find(o => o.value === props.modelValue));

function toggle(): void {
  if (open.value) {
    open.value = false;
    return;
  }
  if (props.options.length === 0) return;
  open.value = true;
  query.value = "";
  activeIdx.value = Math.max(0, props.options.findIndex(o => o.value === props.modelValue));
  requestAnimationFrame(() => filterEl.value?.focus());
}

function choose(o: ComboOption): void {
  emit("update:modelValue", o.value);
  open.value = false;
}

function scrollActive(): void {
  requestAnimationFrame(() => {
    listEl.value?.children[activeIdx.value]?.scrollIntoView({ block: "nearest" });
  });
}

// 键盘导航在过滤框上接管：上下移动高亮、回车选中、Esc 收起
function onListKey(e: KeyboardEvent): void {
  if (e.key === "Escape") {
    open.value = false;
    return;
  }
  if (e.key === "ArrowDown") {
    e.preventDefault();
    activeIdx.value = Math.min(list.value.length - 1, activeIdx.value + 1);
    scrollActive();
  } else if (e.key === "ArrowUp") {
    e.preventDefault();
    activeIdx.value = Math.max(0, activeIdx.value - 1);
    scrollActive();
  } else if (e.key === "Enter") {
    e.preventDefault();
    const o = list.value[activeIdx.value];
    if (o) choose(o);
  }
}

function onBtnKey(e: KeyboardEvent): void {
  if (!open.value && (e.key === "ArrowDown" || e.key === "Enter" || e.key === " ")) {
    e.preventDefault();
    toggle();
  }
}

function onDocDown(e: PointerEvent): void {
  if (open.value && root.value && !root.value.contains(e.target as Node)) open.value = false;
}
document.addEventListener("pointerdown", onDocDown);
onBeforeUnmount(() => document.removeEventListener("pointerdown", onDocDown));
</script>

<template>
  <div ref="root" class="combo">
    <button type="button" class="combo-btn" :class="{ open }" @click="toggle" @keydown="onBtnKey">
      <span :class="{ ph: !current }">{{ current ? current.label : (placeholder || "请选择") }}</span>
      <svg class="chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6"/></svg>
    </button>
    <div v-if="open" class="combo-pop">
      <input ref="filterEl" v-model="query" class="combo-filter" placeholder="筛选老师…" @keydown="onListKey" />
      <div ref="listEl" class="combo-list" role="listbox">
        <div
          v-for="(o, i) in list"
          :key="o.value"
          class="combo-item"
          :class="{ act: i === activeIdx, on: o.value === modelValue }"
          role="option"
          :aria-selected="o.value === modelValue"
          @pointerenter="activeIdx = i"
          @click="choose(o)"
        >
          <svg v-if="o.value === modelValue" class="chk" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>
          <span class="lbl">{{ o.label }}</span>
          <span v-if="o.sub" class="sub">{{ o.sub }}</span>
        </div>
        <div v-if="list.length === 0" class="combo-empty">无匹配项</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* Fluent ComboBox 形态：按钮态与输入框同语言，浮层为发丝线圆角列表 */
.combo { position: relative; }
.combo-btn {
  display: flex; align-items: center; gap: 8px; width: 100%; min-height: 34px; padding: 6px 10px;
  border: 1px solid var(--stroke-strong); border-bottom-width: 2px; border-radius: var(--r-ctrl);
  background: var(--card); color: var(--text); font: inherit; font-size: 13px; text-align: left; cursor: pointer;
  transition: border-color .12s ease;
}
.combo-btn:hover, .combo-btn.open { border-color: var(--stroke-strong); border-bottom-color: var(--accent); }
.combo-btn .ph { color: var(--text-3); }
.combo-btn .chev { width: 13px; height: 13px; margin-left: auto; flex: none; color: var(--text-2); transition: transform .15s ease; }
.combo-btn.open .chev { transform: rotate(180deg); }
.combo-pop {
  position: absolute; left: 0; right: 0; top: calc(100% + 6px); z-index: 30;
  background: var(--card); border: 1px solid var(--stroke); border-radius: var(--r-ctrl);
  box-shadow: var(--shadow-flyout); overflow: hidden;
}
.combo-filter {
  width: 100%; border: 0; border-bottom: 1px solid var(--stroke); background: transparent;
  padding: 8px 12px; font: inherit; font-size: 12.5px; color: var(--text); outline: none;
}
.combo-list { max-height: 264px; overflow-y: auto; }
.combo-item {
  display: flex; align-items: center; gap: 8px; padding: 8px 12px; font-size: 12.5px;
  cursor: pointer; border-bottom: 1px solid var(--stroke); transition: background .1s ease;
}
.combo-item:last-child { border-bottom: 0; }
.combo-item.act { background: var(--ctrl-hover); }
.combo-item.on .lbl { color: var(--accent); font-weight: 600; }
.combo-item .chk { width: 14px; height: 14px; color: var(--accent); flex: none; }
.combo-item .sub { margin-left: auto; font-size: 11px; color: var(--text-3); }
.combo-empty { text-align: center; padding: 16px 0; color: var(--text-3); font-size: 12px; }
</style>
