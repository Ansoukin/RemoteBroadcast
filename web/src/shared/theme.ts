/**
 * 主题：明暗跟随系统，data-theme 手动覆盖优先（v11a 同源）。
 * rb_theme: auto | light | dark
 */

const systemDark = window.matchMedia("(prefers-color-scheme: dark)");

export function currentThemeMode(): "auto" | "light" | "dark" {
  return (localStorage.getItem("rb_theme") as "auto" | "light" | "dark") || "auto";
}

export function resolvedTheme(): "light" | "dark" {
  const mode = currentThemeMode();
  return mode === "auto" ? (systemDark.matches ? "dark" : "light") : mode;
}

export function applyTheme(): void {
  document.documentElement.dataset.theme = resolvedTheme();
}

export function setThemeMode(mode: "auto" | "light" | "dark"): void {
  localStorage.setItem("rb_theme", mode);
  applyTheme();
}

/** 流光动画开关（PC 端默认关，性能优先；移动端默认开）。静态渐变底不受影响，只控制四层渐变是否流动。 */
export function applyAurora(on: boolean): void {
  document.querySelectorAll(".aurora").forEach(el => el.classList.toggle("aurora-anim", on));
}

export function auroraEnabled(): boolean {
  return PREF("rb_aurora", "0") === "1";
}

function PREF(k: string, d: string): string {
  return localStorage.getItem(k) ?? d;
}

systemDark.addEventListener("change", () => {
  if (currentThemeMode() === "auto") applyTheme();
});
