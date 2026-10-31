// Pure boundary mechanics: load the .NET WebAssembly runtime and move one
// serialized Limen message across it. What comes back is untrusted until
// Limen's generated contract decoder accepts it.
import { decodeEngineToBrowserMessage } from "./limen/generated/core.codec.js";

const isFunction = (value, name) =>
  typeof value === "object" && value !== null && typeof Reflect.get(value, name) === "function";

export class WasmEngineTransport {
  #dispatch = null;
  #runtimeUrl;

  constructor(runtimeUrl) {
    this.#runtimeUrl = runtimeUrl;
  }

  async start() {
    const module = await import(this.#runtimeUrl);
    const builder = module?.dotnet;
    if (!isFunction(builder, "withDiagnosticTracing") || !isFunction(builder, "create")) {
      throw new Error("dotnet.js did not export a .NET runtime builder.");
    }
    const runtime = await builder.withDiagnosticTracing(false).create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    const container = exports?.SpeculaWasm;
    if (!isFunction(container, "Dispatch")) {
      throw new Error("SpeculaWasm.Dispatch export not found.");
    }
    this.#dispatch = (json) => String(container.Dispatch(json));
  }

  async dispatch(message) {
    if (this.#dispatch === null) throw new Error("dispatch() called before start().");
    const decoded = decodeEngineToBrowserMessage(JSON.parse(this.#dispatch(JSON.stringify(message))));
    if (!decoded.ok) {
      throw new Error(`Engine message outside the Limen contract at ${decoded.error.path}: expected ${decoded.error.expected}, found ${decoded.error.found}.`);
    }
    return decoded.value;
  }
}
