import { createApp } from "vue";
import App from "./App.vue";
import { applyTheme } from "../shared/theme";
import "./mobile.css";

applyTheme();
createApp(App).mount("#app");
