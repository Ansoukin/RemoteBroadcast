/**
 * 历史记录重发：学生呼叫按姓名回当前名单换 id（名单改过就重发不了）；
 * 自定义广播没存正文，只复原学生呼叫。确认弹窗与结果提示在这层统一。
 */
import { postQuickCall, type HistoryRecordDto } from "./api";
import { app } from "./store";
import { PREF } from "./prefs";
import { showDialog, toast } from "./ui";

export async function resendRecord(record: HistoryRecordDto, caller: string): Promise<void> {
  const studentIds: string[] = [];
  for (const studentName of record.students) {
    const hit = app.config?.roster.find(s => s.name === studentName);
    if (!hit) {
      toast("无法重发", "名单已变更，找不到原学生，请手动重新选择", 3600);
      return;
    }
    studentIds.push(hit.id);
  }

  // 接洽人还原：原记录没带（旧记录）→ 跟发起人；「不指定」→ 空串；其余照传
  const contact = record.contact == null ? undefined : record.contact === "" ? "" : record.contact;
  const contactLabel = contact === undefined ? caller : contact === "" ? "不指定" : contact;
  const confirmed = PREF("rb_confirm", "0") === "1"
    ? await showDialog({
        title: "重新发起呼叫",
        body: `将按原内容重新呼叫：\n${record.students.join("、")} → ${record.destination}（接洽人：${contactLabel}）\n白板将立即弹条并语音播报。`,
        okText: "确认呼叫",
        cancelText: "取消",
      })
    : true;
  if (!confirmed) return;

  try {
    await postQuickCall({
      students: studentIds,
      destination: record.destination,
      caller,
      ...(contact !== undefined ? { contact } : {}),
    });
    toast("已按原内容重新发起呼叫", "请留意白板播报", 3000);
  } catch (e) {
    toast("重发失败", e instanceof Error ? e.message : "未知错误", 4000);
  }
}
