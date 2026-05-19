import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App.tsx";
import "@/styles/globals.css";
import { FontApplier } from "@/components/FontApplier";
import { TooltipProvider } from "@/components/ui/tooltip";

if (import.meta.env.DEV && !window.javaBridge) {
  void import("./utils/mockDpsData").then(({ injectMockDpsData }) => injectMockDpsData());
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <TooltipProvider delayDuration={150}>
      <FontApplier />
      <App />
    </TooltipProvider>
  </StrictMode>,
);
