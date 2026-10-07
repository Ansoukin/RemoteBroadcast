/** 移动端呼叫条与呼叫页之间的桥：呼叫页登记汇总文案与提交动作，壳层渲染常驻呼叫条。 */
import { reactive } from "vue";

export const callBar = reactive({
  visible: false,
  summary: "",
  busy: false,
  submit: null as null | (() => void),
});

export function setCallBar(summary: string, submit: () => void): void {
  callBar.summary = summary;
  callBar.submit = submit;
  callBar.visible = true;
}

export function hideCallBar(): void {
  callBar.visible = false;
  callBar.submit = null;
}
