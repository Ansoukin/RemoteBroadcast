/**
 * 轻量 UI 状态：Toast 队列 + 确认弹窗（promise 化）。两端形态的渲染壳各自实现，
 * 状态与语义（确认回调只挂 OK 键，取消/遮罩不触发）在这里统一。
 */
import { reactive } from "vue";

export interface ToastItem {
  id: number;
  title: string;
  sub: string;
  ms: number;
}

export interface DialogOptions {
  title: string;
  /** 正文，纯文本（按 \n 换行渲染）。 */
  body: string;
  /** 头像图（作者弹窗等场景）；加载失败时页面回退首字占位。 */
  avatarUrl?: string;
  okText?: string;
  cancelText?: string | null;
}

interface UiState {
  toasts: ToastItem[];
  dialog: (DialogOptions & { id: number }) | null;
}

export const ui = reactive<UiState>({ toasts: [], dialog: null });

let nextId = 1;
const timers = new Map<number, number>();

export function toast(title: string, sub = "", ms = 2600): void {
  const id = nextId++;
  ui.toasts.push({ id, title, sub, ms });
  timers.set(id, window.setTimeout(() => dismissToast(id), ms));
}

export function dismissToast(id: number): void {
  const t = timers.get(id);
  if (t !== undefined) {
    clearTimeout(t);
    timers.delete(id);
  }
  const i = ui.toasts.findIndex(x => x.id === id);
  if (i >= 0) ui.toasts.splice(i, 1);
}

let dialogResolve: ((ok: boolean) => void) | null = null;

/** 确认弹窗：resolve(true) 仅由 OK 键触发；取消/点遮罩 resolve(false)。 */
export function showDialog(options: DialogOptions): Promise<boolean> {
  return new Promise(resolve => {
    dialogResolve?.(false); // 旧弹窗没答完就来了新的：旧的按取消收场
    dialogResolve = resolve;
    ui.dialog = { ...options, id: nextId++ };
  });
}

export function resolveDialog(ok: boolean): void {
  const r = dialogResolve;
  dialogResolve = null;
  ui.dialog = null;
  if (ok && r) r(true);
  else if (!ok && r) r(false);
}
