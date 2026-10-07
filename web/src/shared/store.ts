/**
 * 应用配置（/api/config）与呼叫历史的共享存储：两个入口共用同一份事实。
 * 教师名单（接洽人三段选择用）也在这里——目录提供方优先，空名单时页面显示来源说明。
 */
import { reactive } from "vue";
import { deleteHistory, getConfig, getHistory, type ConfigDto, type HistoryRecordDto } from "./api";

interface AppState {
  config: ConfigDto | null;
  loaded: boolean;
  history: HistoryRecordDto[];
}

export const app = reactive<AppState>({
  config: null,
  loaded: false,
  history: [],
});

export async function loadConfig(): Promise<ConfigDto | undefined> {
  try {
    const c = await getConfig();
    app.config = c;
    return c;
  } finally {
    // 无论成败都撤骨架：失败时页面照常渲染，名单空态与右列离线状态如实显示
    app.loaded = true;
  }
}

export async function refreshHistory(): Promise<void> {
  try {
    const h = await getHistory();
    app.history = h.records || [];
  } catch {
    /* 历史拿不到就算了：主页计数与历史页都按 0 条显示 */
  }
}

/** 本地先摘除再调服务端删除；返回值给页面提示实际删除条数，失败时把本地状态滚回来。 */
export async function removeHistory(ids?: string[]): Promise<number> {
  const backup = app.history;
  const idSet = ids ? new Set(ids) : null;
  if (idSet) app.history = app.history.filter(r => !idSet.has(r.id));
  else app.history = [];
  try {
    return await deleteHistory(ids);
  } catch (e) {
    app.history = backup;
    throw e;
  }
}
