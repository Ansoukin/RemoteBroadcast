<script setup lang="ts">
import { ref } from "vue";
import { register } from "../../shared/api";
import { PREF, setRememberedName } from "../../shared/prefs";

const emit = defineEmits<{ go: [page: "home"] }>();

const step = ref(1);
const name = ref("");
const remember = ref(true);
const submitting = ref(false);
const errMsg = ref("");

// 曾登记过的设备允许直接跳过向导（改称谓入口在设置页）
const canSkip = !!PREF("rb_name", "");

function goStep(n: number): void {
  step.value = n;
}

async function submit(): Promise<void> {
  const trimmed = name.value.trim();
  if (trimmed.length < 2 || trimmed.length > 20) {
    errMsg.value = "姓名简称需为 2–20 个字符";
    return;
  }

  errMsg.value = "";
  submitting.value = true;
  try {
    await register(trimmed);
    if (remember.value) setRememberedName(trimmed);
    emit("go", "home");
  } catch (e) {
    errMsg.value = e instanceof Error ? e.message : "登记失败，请重试";
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <section class="page active">
    <div class="oo-topbar">
      <span class="st">第 {{ step }} 步，共 3 步</span>
      <button v-if="canSkip" @click="emit('go', 'home')">跳过向导</button>
    </div>
    <div class="oo-progress"><i :style="{ width: (step / 3 * 100) + '%' }"></i></div>

    <!-- 第一步：欢迎 -->
    <div v-if="step === 1">
      <div class="oo-center">
        <svg style="width:72px;height:72px" viewBox="0 0 192 192" xmlns="http://www.w3.org/2000/svg">
          <path fill="#e2e2e2" d="M177.94,160H56V100H177.94a8,8,0,0,1,8,8v44A8,8,0,0,1,177.94,160Z"/>
          <rect fill="#999" x="70.01" y="115" width="30" height="30"/><rect fill="#999" x="107.51" y="115" width="30" height="30"/><rect fill="#999" x="145" y="115" width="30" height="30"/>
          <polygon fill="#959595" points="124 32 56 100 56 160 124 92 124 32"/>
          <path fill="#f6f6f6" d="M124,92H16a8,8,0,0,1-8-8V40a8,8,0,0,1,8-8H124Z"/>
          <rect fill="#ccc" x="20" y="44.53" width="30" height="30"/><rect fill="#00bfff" x="56" y="44.53" width="58.02" height="30"/>
          <polygon fill="#00bfff" opacity=".15" points="116 100 56 160 185.94 160 185.94 153.94 132 100 116 100"/>
        </svg>
        <h1 style="font-size:21px;margin:12px 0 6px">欢迎使用远程广播</h1>
        <p style="color:var(--text-2);font-size:13px;margin:0 0 18px;line-height:1.6">ClassFabric Remote Broadcast<br/>手机与教室白板局域网互联，课堂一键呼叫</p>
      </div>
      <button class="btn accent" @click="goStep(2)">开始配置</button>
    </div>

    <!-- 第二步：功能简介 -->
    <div v-else-if="step === 2">
      <h1>功能简介</h1>
      <p class="sub">三步完成一次随堂呼叫。</p>
      <div class="group">
        <div class="row">
          <span class="ic"><svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg></span>
          <div class="tx"><b>呼叫学生或广播自定义内容</b><small>支持多选学生名单，或输入任意通知直达大屏</small></div>
        </div>
        <div class="row">
          <span class="ic"><svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M3 21h18"/><path d="M5 21V7l7-4 7 4v14"/><path d="M9 21v-6h6v6"/></svg></span>
          <div class="tx"><b>选择目的地与指定接洽人</b><small>常用地点预设、置顶与被接洽老师一键关联</small></div>
        </div>
        <div class="row">
          <span class="ic"><svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="m3 11 18-5v12L3 14v-3z"/><path d="M11.6 16.8a3 3 0 1 1-5.8-1.6"/></svg></span>
          <div class="tx"><b>白板弹条并伴随语音播报</b><small>语音高清合成，直观提醒不遗漏</small></div>
        </div>
      </div>
      <div class="dual-btns">
        <button class="btn std" @click="goStep(1)">上一步</button>
        <button class="btn accent" @click="goStep(3)">下一步</button>
      </div>
    </div>

    <!-- 第三步：登记称谓 -->
    <div v-else>
      <h1>登记您的称谓</h1>
      <p class="sub">用于在大屏幕呼叫悬浮条与语音中作为称呼展示。</p>
      <div class="group">
        <div style="padding:14px 12px">
          <div class="tbox big"><input v-model="name" type="text" maxlength="20" placeholder="姓名简称，如：刘老师" @keyup.enter="submit" /></div>
          <label class="switchrow"><input v-model="remember" type="checkbox" /><span class="sw"></span><span>记住我（本设备免登记）</span></label>
          <button class="btn accent" :disabled="submitting" style="margin-top:8px" @click="submit">{{ submitting ? "登记中…" : "完成并开启" }}</button>
          <div class="infobar" :class="{ show: errMsg }"><div><b>登记失败</b><small>{{ errMsg }}</small></div></div>
        </div>
      </div>
      <button class="btn std" style="margin-top:10px" @click="goStep(2)">上一步</button>
    </div>
  </section>
</template>
