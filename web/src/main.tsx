import { createRoot } from "react-dom/client";
import "./index.css";
import "@/styles/combined/index.css";
import App from "./App";
import { ThemeProvider } from "@/prototype/theme.jsx";
import { I18nProvider } from "@/prototype/i18n.jsx";

createRoot(document.getElementById("root")!).render(
  <ThemeProvider>
    <I18nProvider>
      <App />
    </I18nProvider>
  </ThemeProvider>
);
