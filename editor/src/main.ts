import EditorWorker from "monaco-editor/esm/vs/editor/editor.worker?worker";
import JsonWorker from "monaco-editor/esm/vs/language/json/json.worker?worker";
import { Bridge } from "./bridge";
import { EditorAdapter } from "./editor";
import type { HostMessage } from "./protocol";

self.MonacoEnvironment = {
  getWorker(_, label) {
    return label === "json" ? new JsonWorker() : new EditorWorker();
  },
};

const container = document.querySelector<HTMLElement>("#app");
if (!container) throw new Error("Editor container is missing.");

document.documentElement.style.height = "100%";
document.body.style.cssText = "height:100%;margin:0;overflow:hidden";
container.style.height = "100%";

const bridge = new Bridge();
const editor = new EditorAdapter(container, bridge);

bridge.onMessage((message) => handleCommand(message));
bridge.send("ready", {});

async function handleCommand(message: HostMessage): Promise<void> {
  try {
    let result: unknown = {};
    switch (message.type) {
      case "initialize": editor.initialize(asPayload(message, "initialize")); break;
      case "openDocument": editor.openDocument(asPayload(message, "openDocument")); break;
      case "replaceContent": editor.replaceContent(asPayload(message, "replaceContent")); break;
      case "setTheme": editor.setTheme(asPayload(message, "setTheme")); break;
      case "format": await editor.format(); break;
      case "foldAll": await editor.foldAll(); break;
      case "unfoldAll": await editor.unfoldAll(); break;
      case "foldLevel": await editor.foldLevel(asPayload(message, "foldLevel")); break;
      case "enterDiff": editor.enterDiff(asPayload(message, "enterDiff")); break;
      case "exitDiff": editor.exitDiff(); break;
      case "focus": editor.focus(); break;
      case "getContent": result = { content: editor.getContent() }; break;
      default: throw new Error(`Unsupported command '${message.type}'.`);
    }
    bridge.send("commandResult", { ok: true, result }, message.requestId);
  } catch (error) {
    bridge.send("commandResult", {
      ok: false,
      error: error instanceof Error ? error.message : String(error),
    }, message.requestId);
  }
}

function asPayload<T>(message: HostMessage, _command: string): T {
  return message.payload as T;
}
