/**
 * 实时状态流：WS 优先，断线回退 2s 轮询并自动重连。
 * 消息语义与 /api/status 长一个样；hello = 连上先对表现状，stage = 阶段事件。
 */
import { reactive } from "vue";
import { CODE, getStatus, type CurrentBroadcastDto } from "./api";

export interface BroadcastState {
  state: "Idle" | "Broadcasting" | "Error";
  /** 最近一次阶段（camelCase，与服务端一致）；hello 之后可能为空。 */
  stage: string | null;
  error: string | null;
  current: CurrentBroadcastDto | null;
  wsConnected: boolean;
}

export const broadcast = reactive<BroadcastState>({
  state: "Idle",
  stage: null,
  error: null,
  current: null,
  wsConnected: false,
});

type StageListener = (msg: { type: string; state?: string; stage?: string; error?: string | null; current?: CurrentBroadcastDto | null; message?: string | null }) => void;
const listeners = new Set<StageListener>();

export function onBroadcastMessage(fn: StageListener): () => void {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

function applyPayload(msg: { state?: string; stage?: string; error?: string | null; current?: CurrentBroadcastDto | null }): void {
  if (msg.state) broadcast.state = msg.state as BroadcastState["state"];
  if (msg.stage !== undefined) broadcast.stage = msg.stage;
  if (msg.error !== undefined) broadcast.error = msg.error || null;
  if (msg.current !== undefined) broadcast.current = msg.current || null;
}

let ws: WebSocket | null = null;
let wsRetryTimer: number | null = null;
let pollTimer: number | null = null;

/** 阶段终态 → 状态语义：completed/cancelled 都回到 Idle，failed 进 Error（与 /api/status 同义）。 */
const STAGE_STATE_MAP: Record<string, string> = { completed: "Idle", failed: "Error", cancelled: "Idle" };

function consumeStage(stage: string, message?: string | null): void {
  const state = STAGE_STATE_MAP[stage] ?? null;
  if (!state) {
    broadcast.stage = stage;
    return;
  }
  broadcast.state = state as BroadcastState["state"];
  broadcast.stage = stage;
  broadcast.error = state === "Error" ? message || null : null;
  if (state === "Idle") broadcast.current = null;
}

function connectStatusStream(): void {
  if (!("WebSocket" in window)) { startPolling(); return; }
  const proto = location.protocol === "https:" ? "wss" : "ws";
  try {
    ws = new WebSocket(`${proto}://${location.host}/${CODE}/ws`);
  } catch {
    startPolling();
    return;
  }

  ws.onopen = () => {
    broadcast.wsConnected = true;
    stopPolling();
  };
  ws.onmessage = (ev) => {
    try {
      const msg = JSON.parse(ev.data);
      if (msg.type === "hello") {
        applyPayload({ state: msg.state, stage: msg.stage ?? undefined, error: msg.error, current: msg.current });
      } else if (msg.type === "stage") {
        applyPayload({ stage: msg.stage, current: msg.current ?? undefined });
        consumeStage(msg.stage, msg.message);
      }
      for (const fn of listeners) {
        try { fn(msg); } catch { /* 单个订阅者坏了不影响别的 */ }
      }
    } catch { /* 坏消息静默忽略 */ }
  };
  ws.onclose = ws.onerror = () => {
    ws = null;
    broadcast.wsConnected = false;
    // 回退轮询兜底，再择机重连。
    if (pollTimer === null) startPolling();
    if (wsRetryTimer !== null) clearTimeout(wsRetryTimer);
    wsRetryTimer = window.setTimeout(connectStatusStream, 5000);
  };
}

function startPolling(): void {
  if (pollTimer !== null) return;
  pollTimer = window.setInterval(async () => {
    try {
      const s = await getStatus();
      applyPayload({ state: s.state, error: s.error, current: s.current });
    } catch { /* 轮询失败静默重试 */ }
  }, 2000);
}

function stopPolling(): void {
  if (pollTimer !== null) {
    clearInterval(pollTimer);
    pollTimer = null;
  }
}

/** 网络延迟探测：拿 /status 的往返耗时，顺带刷新播报状态（WS 在线时也不打断推送）。 */
export async function probeRtt(): Promise<number | null> {
  const t0 = performance.now();
  try {
    const res = await fetch(`/${CODE}/api/status`);
    await res.json();
    const rtt = Math.round(performance.now() - t0);
    if (broadcast.state === "Idle" && !ws) {
      // 轮询路径顺带对表；WS 路径的刷新交给推送。
    }
    return rtt;
  } catch {
    return null;
  }
}

connectStatusStream();
