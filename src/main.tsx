import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

import { BootstrapApp } from "./BootstrapApp";
import "./styles/global.css";

const root = document.getElementById("root");

if (!root) {
  throw new Error("Missing #root element");
}

createRoot(root).render(
  <StrictMode>
    <BootstrapApp />
  </StrictMode>,
);
