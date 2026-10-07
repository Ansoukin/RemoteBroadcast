/**
 * 类型化 API 客户端：接入码取自 URL 第一段——它既是路由前缀也是身份凭据。
 * 404 = 接入码失效，429 = 限速；其余按 HTTP 语义上抛。
 */

export const CODE = location.pathname.split("/").filter(Boolean)[0] || "";
export const API = `/${CODE}/api`;

export interface StudentDto {
  id: string;
  name: string;
  group: string;
  no: string;
}

export interface DestinationDto {
  id: string;
  name: string;
}

export interface TeacherDto {
  name: string;
  group: string;
}

export interface ConfigDto {
  roster: StudentDto[];
  destinations: DestinationDto[];
  teachers: TeacherDto[];
  teachersSource: string;
  loopCount: number;
  version: string;
  hostVersion: string;
}

export interface CurrentBroadcastDto {
  caller: string;
  students: string[];
  destination: string;
  contact?: string | null;
  startedAt?: string;
}

export interface StatusDto {
  state: "Idle" | "Broadcasting" | "Error";
  current?: CurrentBroadcastDto | null;
  error?: string | null;
}

export interface HistoryRecordDto {
  /** 本运行期唯一的记录标识（删除接口按它定位）；历史只存白板内存，重启后整个换一批。 */
  id: string;
  at: string;
  students: string[];
  destination: string;
  caller: string;
  contact?: string | null;
  result: string;
}

export interface QuickCallPayload {
  students?: string[];
  destination?: string;
  caller: string;
  /** 接洽人三态：缺省（不带字段）= 跟随发起人；空串 = 不指定；非空 = 找该人。 */
  contact?: string;
  loopCount?: number;
  mode?: "custom";
  title?: string;
  body?: string;
  titleDuration?: number;
  bodyDuration?: number;
}

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

export async function api(path: string, opts?: RequestInit): Promise<Response> {
  let res: Response;
  try {
    res = await fetch(API + path, opts);
  } catch {
    throw new ApiError(0, "网络异常，请检查连接后重试");
  }
  if (res.status === 404) throw new ApiError(404, "接入码已失效，请重新扫码");
  if (res.status === 429) throw new ApiError(429, "请求太频繁，请稍候再试");
  return res;
}

export async function getConfig(): Promise<ConfigDto> {
  const res = await api("/config");
  if (!res.ok) throw new ApiError(res.status, `错误 ${res.status}`);
  return await res.json();
}

export async function getStatus(): Promise<StatusDto> {
  const res = await api("/status");
  if (!res.ok) throw new ApiError(res.status, `错误 ${res.status}`);
  return await res.json();
}

export async function getHistory(): Promise<{ records: HistoryRecordDto[] }> {
  const res = await api("/history");
  if (!res.ok) throw new ApiError(res.status, `错误 ${res.status}`);
  return await res.json();
}

/** 删除呼叫历史：ids 省略或空数组 = 清空全部；返回实际删除条数。 */
export async function deleteHistory(ids?: string[]): Promise<number> {
  const res = await api("/history", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(ids && ids.length > 0 ? { ids } : {}),
  });
  if (!res.ok) {
    const e = await res.json().catch(() => ({}));
    throw new ApiError(res.status, (e as { error?: string }).error || `错误 ${res.status}`);
  }
  const data = await res.json() as { removed?: number };
  return data.removed ?? 0;
}

export async function register(teacherName: string): Promise<void> {
  const res = await api("/register", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ teacherName }),
  });
  if (!res.ok) {
    const e = await res.json().catch(() => ({}));
    throw new ApiError(res.status, (e as { error?: string }).error || `错误 ${res.status}`);
  }
}

export async function postQuickCall(payload: QuickCallPayload): Promise<void> {
  const res = await api("/quickcall", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    const e = await res.json().catch(() => ({}));
    throw new ApiError(res.status, (e as { error?: string }).error || `错误 ${res.status}`);
  }
}

/** 名单搜索：姓名包含/首字/学号（服务端按位序推导下发）/id 兜底。 */
export function matchStudent(s: StudentDto, kw: string): boolean {
  if (!kw) return true;
  if ((s.name || "").includes(kw)) return true;
  if ((s.name || "").startsWith(kw)) return true;
  if ((s.no || "").includes(kw)) return true;
  if ((s.id || "").toLowerCase().includes(kw.toLowerCase())) return true;
  return false;
}

/** 时间显示：今天/昨天 + HH:mm，更早按 M月d日。 */
export function formatTime(iso: string): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "--";
  const now = new Date();
  const hm = `${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
  const sameDay = (a: Date, b: Date) => a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
  const yesterday = new Date(now);
  yesterday.setDate(now.getDate() - 1);
  if (sameDay(d, now)) return `今天 ${hm}`;
  if (sameDay(d, yesterday)) return `昨天 ${hm}`;
  return `${d.getMonth() + 1}月${d.getDate()}日 ${hm}`;
}
