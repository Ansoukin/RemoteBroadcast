import { createApp } from "vue";
import App from "./App.vue";
import { applyTheme } from "../shared/theme";
import "./pc.css";

applyTheme();
createApp(App).mount("#app");
