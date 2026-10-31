// Browser mechanism only: start the Limen kernel with the F# WebAssembly
// engine behind it. No application vocabulary or state lives here.
import { BrowserKernel } from "./limen/index.js";
import { WasmEngineTransport } from "./wasm-transport.js";

const bootFailure = (detail) => {
  console.error("[specula] boot failure", detail);
  const element = document.getElementById("boot-failure");
  if (element) element.hidden = false;
};

const diagnostics = {
  report(event) {
    switch (event.kind) {
      case "BridgeError":
        console.error(`[limen:${event.phase}]`, event.detail);
        if (event.phase === "start" || event.phase === "binding" || event.phase === "handshake") bootFailure(event.detail);
        return;
      case "Handshake":
        if (event.verdict?.kind === "Rejected") bootFailure(event.verdict);
        return;
      default:
        return;
    }
  },
};

const transport = new WasmEngineTransport(new URL("./_framework/dotnet.js", import.meta.url).href);
try {
  await new BrowserKernel(transport, document, diagnostics, { requireHandshake: true }).start();
  document.documentElement.dataset.speculaReady = "true";
} catch (error) {
  bootFailure(error);
}
