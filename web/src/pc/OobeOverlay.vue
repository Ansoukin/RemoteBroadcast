<script setup lang="ts">
import { ref } from "vue";
import { register } from "../shared/api";
import { PREF, setRememberedName } from "../shared/prefs";

// initialStep：从设置页「修改称谓」进来时直接落到登记步，不必重看前两步
const props = defineProps<{ initialStep?: 1 | 2 | 3 }>();
const emit = defineEmits<{ finish: [] }>();

const step = ref(props.initialStep ?? 1);
const name = ref(PREF("rb_name", ""));
const remember = ref(true);
const submitting = ref(false);
const errMsg = ref("");

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
    emit("finish");
  } catch (e) {
    errMsg.value = e instanceof Error ? e.message : "登记失败，请重试";
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <div class="oobe-layer show">
    <div class="aurora"></div>
    <div class="oo-card">
      <div class="oo-top">
        <span>第 {{ step }} 步，共 3 步</span>
        <!-- 从设置页「修改称谓」进入时给个退出口，不登记也能走 -->
        <button v-if="props.initialStep" @click="emit('finish')">取消</button>
      </div>
      <div class="oo-bar"><i :style="{ width: (step / 3 * 100) + '%' }"></i></div>
      <div class="inner">
        <!-- 第一步：欢迎 -->
        <div v-if="step === 1" class="oo-step on">
          <div class="oo-hero">
            <svg style="width:84px;height:84px" viewBox="0 0 192 192" xmlns="http://www.w3.org/2000/svg">
              <path fill="#e2e2e2" d="M177.94,160H56V100H177.94a8,8,0,0,1,8,8v44A8,8,0,0,1,177.94,160Z"/>
              <rect fill="#999" x="70.01" y="115" width="30" height="30"/><rect fill="#999" x="107.51" y="115" width="30" height="30"/><rect fill="#999" x="145" y="115" width="30" height="30"/>
              <polygon fill="#959595" points="124 32 56 100 56 160 124 92 124 32"/>
              <path fill="#f6f6f6" d="M124,92H16a8,8,0,0,1-8-8V40a8,8,0,0,1,8-8H124Z"/>
              <rect fill="#ccc" x="20" y="44.53" width="30" height="30"/><rect fill="#00bfff" x="56" y="44.53" width="58.02" height="30"/>
              <polygon fill="#00bfff" opacity=".15" points="116 100 56 160 185.94 160 185.94 153.94 132 100 116 100"/>
            </svg>
            <h1>欢迎使用 远程广播</h1>
            <p>ClassFabric 远程广播<br/>手机与教室白板局域网互联，课堂一键呼叫</p>
          </div>
          <div class="oo-nav"><button class="btn accent" @click="step = 2">开始配置</button></div>
        </div>

        <!-- 第二步：功能简介 -->
        <div v-else-if="step === 2" class="oo-step on">
          <h1 style="font-size:20px;margin:0 0 2px">功能简介</h1>
          <p style="color:var(--text-2);font-size:13px;margin:0 0 12px">三步完成一次随堂呼叫。</p>
          <div class="feat-row">
            <span class="ic"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg></span>
            <div class="tx"><b>呼叫学生或广播自定义内容</b><small>支持多选学生名单，或输入任意通知直达大屏</small></div>
          </div>
          <div class="feat-row">
            <span class="ic"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M3 21h18"/><path d="M5 21V7l7-4 7 4v14"/><path d="M9 21v-6h6v6"/></svg></span>
            <div class="tx"><b>选择目的地与指定接洽人</b><small>常用地点预设、置顶与被接洽老师一键关联</small></div>
          </div>
          <div class="feat-row">
            <span class="ic"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="m3 11 18-5v12L3 14v-3z"/><path d="M11.6 16.8a3 3 0 1 1-5.8-1.6"/></svg></span>
            <div class="tx"><b>白板弹条并伴随语音播报</b><small>语音高清合成，直观提醒不遗漏</small></div>
          </div>
          <div class="oo-nav">
            <button class="btn" @click="step = 1">上一步</button>
            <button class="btn accent" @click="step = 3">下一步</button>
          </div>
        </div>

        <!-- 第三步：登记称谓 -->
        <div v-else class="oo-step on">
          <h1 style="font-size:20px;margin:0 0 2px">登记您的称谓</h1>
          <p style="color:var(--text-2);font-size:13px;margin:0 0 12px">用于在大屏幕呼叫悬浮条与语音中作为称呼展示。</p>
          <div class="card" style="box-shadow:none"><div class="cbody">
            <input v-model="name" class="input" maxlength="20" placeholder="姓名简称，如：刘老师" @keyup.enter="submit" />
            <label class="checkline"><span class="switch"><input v-model="remember" type="checkbox" /><i></i></span><span>记住我（本设备免登记）</span></label>
            <div class="infobar err" :class="{ show: errMsg }"><span>{{ errMsg }}</span></div>
            <div class="oo-actions" style="margin-top:12px">
              <button class="btn accent" :disabled="submitting" @click="submit">{{ submitting ? "登记中…" : "完成并开启" }}</button>
            </div>
          </div></div>
          <div class="oo-nav"><button class="btn" @click="step = 2">上一步</button></div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* 设计稿用 #oobe-layer 挂在 body 下；组件化后改 class 挂载，样式原样保留 */
.oobe-layer { position: fixed; inset: 0; z-index: 60; display: grid; place-items: center; background: var(--bg); overflow: auto; }
.oo-card { position: relative; z-index: 1; width: min(520px, 92vw); margin: 24px 0; }
</style>
