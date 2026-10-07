/**
 * 播报时长估算：与服务端同口径（去空格字数 ÷ 4.2 字/秒，下限 4s、上限 90s），
 * 网页端「自动」模式和预览用它显示实际生效时长，别让人对着 10s 的假默认值调不出区别。
 */

export const OVERLAY_HARD_CAP_SECONDS = 90;

export function estimateBodySeconds(text: string): number {
  const effective = (text || "").replace(/\s/g, "").length;
  return Math.min(OVERLAY_HARD_CAP_SECONDS, Math.max(4, effective / 4.2));
}
