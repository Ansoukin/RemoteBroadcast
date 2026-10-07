<script setup lang="ts">
import { computed, ref } from "vue";
import { formatTime, type HistoryRecordDto } from "../../shared/api";
import { app, removeHistory, refreshHistory } from "../../shared/store";
import { resendRecord } from "../../shared/resend";
import { PREF } from "../../shared/prefs";
import { showDialog, toast } from "../../shared/ui";

const name = PREF("rb_name", "");
refreshHistory();

const keyword = ref("");
const filter = ref<"" | "ok" | "fail" | "cancel">("");

// 批量模式：进入后行首出复选框，点行即勾选（不用再摸小方块）
const picking = ref(false);
const picked = ref(new Set<string>());
const allPicked = computed(() => filtered.value.length > 0 && filtered.value.every(r => picked.value.has(r.id)));

const filtered = computed<HistoryRecordDto[]>(() =>
  app.history.filter(r => {
    const kw = keyword.value.trim();
    if (kw && !(r.students.join("、").includes(kw) || r.destination.includes(kw) || r.caller.includes(kw))) return false;
    if (filter.value === "ok") return r.result === "已完成";
    if (filter.value === "fail") return r.result !== "已完成" && r.result !== "已取消";
    if (filter.value === "cancel") return r.result === "已取消";
    return true;
  }),
);

function chipClass(r: HistoryRecordDto): string {
  if (r.result === "已完成") return "ok";
  if (r.result === "已取消") return "cancel";
  return "err";
}
function resend(r: HistoryRecordDto): void {
  resendRecord(r, name);
}

function togglePick(id: string): void {
  if (picked.value.has(id)) picked.value.delete(id);
  else picked.value.add(id);
}
function pickAll(): void {
  if (allPicked.value) picked.value.clear();
  else filtered.value.forEach(r => picked.value.add(r.id));
}
function exitPicking(): void {
  picking.value = false;
  picked.value.clear();
}

async function removeOne(r: HistoryRecordDto): Promise<void> {
  try {
    await removeHistory([r.id]);
    toast("已删除该条记录", formatTime(r.at), 2200);
  } catch (e) {
    toast("删除失败", e instanceof Error ? e.message : "未知错误", 4000);
  }
}

async function removePicked(): Promise<void> {
  const ids = [...picked.value];
  if (ids.length === 0) return;
  const ok = await showDialog({
    title: `删除 ${ids.length} 条呼叫记录`,
    body: "删除后不可恢复，确定要删除选中的记录吗？",
    okText: "删除",
    cancelText: "取消",
  });
  if (!ok) return;
  try {
    const n = await removeHistory(ids);
    toast(`已删除 ${n} 条记录`, "", 2200);
    exitPicking();
  } catch (e) {
    toast("删除失败", e instanceof Error ? e.message : "未知错误", 4000);
  }
}
</script>

<template>
  <section class="page active">
    <div class="toolbar">
      <input v-model="keyword" class="input" style="width:280px" placeholder="搜索学生 / 目的地…" />
      <select v-model="filter" class="input" style="width:130px">
        <option value="">全部结果</option>
        <option value="ok">已完成</option>
        <option value="fail">失败</option>
        <option value="cancel">已取消</option>
      </select>
      <button class="btn std" @click="picking ? exitPicking() : (picking = true)">
        {{ picking ? "退出批量" : "批量管理" }}
        <span v-if="picking && picked.size > 0" class="pick-count">{{ picked.size }}</span>
      </button>
      <button v-if="picking" class="btn std" :disabled="picked.size === 0" @click="removePicked">删除所选</button>
      <span style="margin-left:auto;font-size:12px;color:var(--text-3)">共 {{ filtered.length }} 条 · 仅保存在白板本机</span>
    </div>
    <div class="card">
      <table class="hist">
        <thead>
          <tr>
            <th v-if="picking" class="col-pick">
              <input type="checkbox" class="pick-box" :checked="allPicked" title="全选/全不选（按当前筛选结果）" @change="pickAll" />
            </th>
            <th>时间</th><th>发起人</th><th>接洽人</th><th>学生</th><th>目的地</th><th>结果</th><th></th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="r in filtered"
            :key="r.id"
            :class="{ picking, picked: picked.has(r.id) }"
            @click="picking && togglePick(r.id)"
          >
            <td v-if="picking" class="col-pick">
              <input type="checkbox" class="pick-box" :checked="picked.has(r.id)" @change="togglePick(r.id)" />
            </td>
            <td>{{ formatTime(r.at) }}</td>
            <td>{{ r.caller }}</td>
            <td>{{ r.contact == null || r.contact === "" ? "不指定" : r.contact }}</td>
            <td>{{ r.students.join("、") || "—" }}</td>
            <td>{{ r.destination }}</td>
            <td><span class="result-chip" :class="chipClass(r)" :title="r.result">{{ r.result === "已完成" ? "已完成" : r.result === "已取消" ? "已取消" : "失败" }}</span></td>
            <td style="text-align:right;white-space:nowrap">
              <button v-if="r.students.length > 0" class="resend" @click.stop="resend(r)">重发</button>
              <button class="row-del" title="删除该条记录" @click.stop="removeOne(r)">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M3 6h18"/><path d="M8 6V4h8v2"/><path d="M6 6l1 14h10l1-14"/><path d="M10 11v6M14 11v6"/></svg>
              </button>
            </td>
          </tr>
          <tr v-if="filtered.length === 0">
            <td :colspan="picking ? 8 : 7" style="text-align:center;padding:26px 0;color:var(--text-3)">暂无匹配的呼叫记录</td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>

<style scoped>
.pick-count { margin-left: 6px; background: var(--accent); color: #fff; border-radius: 999px;
  font-size: 11px; padding: 0 7px; font-weight: 600; }
html[data-theme="dark"] .pick-count { color: #000; }
tr.picked td { background: var(--accent-light); }
.col-pick { width: 36px; text-align: center; }
.pick-box { width: 15px; height: 15px; accent-color: var(--accent); cursor: pointer; }
tr.picking { cursor: pointer; }
.row-del { border: 1px solid var(--stroke-strong); background: transparent; color: var(--text-2);
  font: inherit; font-size: 12px; border-radius: var(--r-ctrl); padding: 4px 7px; cursor: pointer;
  vertical-align: middle; margin-left: 6px; display: inline-grid; place-items: center; }
.row-del svg { width: 13px; height: 13px; display: block; }
.row-del:hover { background: var(--error-bg); color: var(--error); border-color: transparent; }
</style>
