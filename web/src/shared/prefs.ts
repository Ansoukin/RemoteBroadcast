/**
 * 本地偏好（不落服务端，纯本机）与触觉反馈。
 * 键名与旧版网页一致：同一台设备换形态（PC/手机页）偏好互通。
 */

export const PREF = (k: string, d: string) => localStorage.getItem(k) ?? d;
export const savePref = (k: string, v: string) => localStorage.setItem(k, v);

export function haptic(ms = 15): void {
  if (PREF("rb_haptics", "1") === "1" && navigator.vibrate) {
    try { navigator.vibrate(ms); } catch { /* 不支持就算了 */ }
  }
}

/** 生成本机随机标识。crypto.randomUUID 只在 HTTPS/localhost 安全上下文里存在，
 * 局域网 http:// 访问时没有——自己拼一份， getRandomValues 再没有就退 Math.random。 */
function randomUuid(): string {
  if (typeof crypto.randomUUID === "function") return crypto.randomUUID();
  const bytes = new Uint8Array(16);
  if (typeof crypto.getRandomValues === "function") {
    crypto.getRandomValues(bytes);
  } else {
    for (let i = 0; i < 16; i++) bytes[i] = Math.floor(Math.random() * 256);
  }
  // 按 RFC 4122 v4 置位（版本号与变体位），格式对齐 8-4-4-4-12
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = [...bytes].map(b => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

/** 本机设备标识：只存本机，服务端不收——用途只是「这台设备记住了我」。 */
export function ensureDeviceToken(): string {
  if (!localStorage.getItem("rb_device")) {
    localStorage.setItem("rb_device", randomUuid());
  }
  return localStorage.getItem("rb_device") || "";
}

/** 登记恢复：rb_name + rb_device 都在才算「这台设备记住了我」。 */
export function tryResumeName(): string | null {
  const name = localStorage.getItem("rb_name");
  return name && localStorage.getItem("rb_device") ? name : null;
}

export function setRememberedName(name: string): void {
  ensureDeviceToken();
  localStorage.setItem("rb_name", name);
}
