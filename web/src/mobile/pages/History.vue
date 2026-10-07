<script setup lang="ts">
import { computed, ref } from "vue";
import { postQuickCall, formatTime, type HistoryRecordDto } from "../../shared/api";
import { app, refreshHistory } from "../../shared/store";
import { PREF, haptic } from "../../shared/prefs";
import { showDialog, toast } from "../../shared/ui";

const emit = defineEmits<{ go: [page: "home"] }>();

const keyword = ref("");
const filter = ref<"" | "ok" | "fail">("");

const name = PREF("rb_name", "");

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

function chipOf(r: HistoryRecordDto): { cls: string; text: string } {
  if (r.result === "已完成") return { cls: "ok", text: "已完成" };
  if (r.result === "已取消") return { cls: "cancel", text: "已取消" };
  return { cls: "err", text: "失败" };
}

// 自定义广播没存正文，重发只能复原学生呼叫；学生按姓名回当前名单换 id，名单改过就重发不了
const studentIdsOf = (r: HistoryRecordDto): string[] | null => {
  const roster = app.config?.roster ?? [];
  const ids: string[] = [];
  for (const n of r.students) {
    const hit = roster.find(s => s.name === n);
    if (!hit) return null;
    ids.push(hit.id);
  }
  return ids;
};

async function resend(r: HistoryRecordDto): Promise<void> {
  haptic(15);
  const ids = studentIdsOf(r);
  if (ids == null) {
    toast("无法重发", "名单已变更，找不到原学生，请手动重新选择", 3600);
    return;
  }

  const contact = r.contact == null ? undefined : r.contact === "" ? "" : r.contact;
  const contactLabel = contact === undefined ? name : contact === "" ? "不指定" : contact;
  const ok = await showDialog({
    title: "重新发起呼叫",
    body: `将按原内容重新呼叫：\n${r.students.join("、")} → ${r.destination}（接洽人：${contactLabel}）\n白板将立即弹条并语音播报。`,
    okText: "确认呼叫",
    cancelText: "取消",
  });
  if (!ok) return;

  try {
    await postQuickCall({
      students: ids,
      destination: r.destination,
      caller: name,
      ...(contact !== undefined ? { contact } : {}),
    });
    toast("已按原内容重新发起呼叫", "请留意白板播报", 3000);
  } catch (e) {
    toast("重发失败", e instanceof Error ? e.message : "未知错误", 4000);
  }
}

refreshHistory();
</script>

<template>
  <section class="page active">
    <div style="display:flex;align-items:center;gap:6px;margin-bottom:9px">
      <button class="barbtn" title="返回主页" @click="emit('go', 'home')"><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"><path d="m15 18-6-6 6-6"/></svg></button>
      <h1 style="margin:0;font-size:18px">呼叫历史</h1>
    </div>
    <div class="hist-toolbar">
      <div class="tbox withicon">
        <svg class="lic" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>
        <input v-model="keyword" type="text" placeholder="搜索学生 / 目的地…" />
      </div>
      <div class="segmented-mini">
        <button :class="{ on: filter === '' }" @click="filter = ''; haptic(12)">全部</button>
        <button :class="{ on: filter === 'ok' }" @click="filter = 'ok'; haptic(12)">完成</button>
        <button :class="{ on: filter === 'fail' }" @click="filter = 'fail'; haptic(12)">失败</button>
      </div>
    </div>
    <div class="group">
      <div v-for="(r, i) in filtered" :key="i" class="hist-item row" style="min-height:0">
        <div style="flex:1;min-width:0">
          <div class="l1">
            <b>{{ r.students.length > 0 ? r.students.join("、") + " → " + r.destination : "「" + r.destination + "」" }}</b>
            <span class="hist-chip" :class="chipOf(r).cls">{{ chipOf(r).text }}</span>
          </div>
          <div class="l2">
            {{ formatTime(r.at) }} · 发起人 {{ r.caller }} · 接洽人 {{ r.contact == null || r.contact === "" ? "不指定" : r.contact }}{{ r.result === "已完成" ? "" : "（" + r.result + "）" }}
          </div>
        </div>
        <button v-if="r.students.length > 0" class="resend" @click.stop="resend(r)">重发</button>
      </div>
      <div v-if="filtered.length === 0" class="hist-empty">暂无匹配的呼叫记录</div>
    </div>
    <div style="font-size:10.5px;color:var(--text-3);margin:2px 2px 8px">共 {{ filtered.length }} 条 · 仅保存在白板本机</div>
  </section>
</template>
