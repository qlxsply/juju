import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

import "./styles/global.css";

function getRootElement(): HTMLElement {
  const root = document.getElementById("root");
  if (!root) {
    throw new Error("Missing #root element");
  }
  return root;
}

const root = getRootElement();

async function renderCurrentView() {
  const view = new URLSearchParams(window.location.search).get("view");
  let App;
  if (view === "launcher") {
    App = (await import("./launcher/LauncherApp")).LauncherApp;
  } else if (view === "tool") {
    App = (await import("./tools/json/JsonToolApp")).JsonToolApp;
  } else if (view === "settings") {
    App = (await import("./settings/SettingsApp")).SettingsApp;
  } else {
    App = (await import("./BootstrapApp")).BootstrapApp;
  }

  createRoot(root).render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}

void renderCurrentView();
