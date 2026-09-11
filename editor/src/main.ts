import EditorWorker from "monaco-editor/esm/vs/editor/editor.worker?worker";
import JsonWorker from "monaco-editor/esm/vs/language/json/json.worker?worker";
import { Bridge } from "./bridge";
import { EditorAdapter } from "./editor";
import type { EnterDiffPayload, FoldLevelPayload, HostMessage, InitializePayload, OpenDocumentPayload, ReplaceContentPayload, ThemeName } from "./protocol";

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
      case "initialize": editor.initialize(initializePayload(message)); break;
      case "openDocument": editor.openDocument(openDocumentPayload(message)); break;
      case "replaceContent": editor.replaceContent(replaceContentPayload(message)); break;
      case "setTheme": editor.setTheme(themePayload(message)); break;
      case "format": await editor.format(); break;
      case "foldAll": await editor.foldAll(); break;
      case "unfoldAll": await editor.unfoldAll(); break;
      case "unfoldLevel": await editor.unfoldLevel(); break;
      case "foldLevel": await editor.foldLevel(foldLevelPayload(message)); break;
      case "enterDiff": editor.enterDiff(diffPayload(message)); break;
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

function objectPayload(message: HostMessage): Record<string, unknown> {
  if (typeof message.payload !== "object" || message.payload === null || Array.isArray(message.payload)) {
    throw new Error(`Invalid payload for '${message.type}'.`);
  }
  return message.payload as Record<string, unknown>;
}

function initializePayload(message: HostMessage): InitializePayload {
  const payload = objectPayload(message);
  if (payload.theme !== undefined && payload.theme !== "vs" && payload.theme !== "vs-dark") throw new Error("Invalid theme.");
  if (payload.indentSize !== undefined && (typeof payload.indentSize !== "number" || !Number.isInteger(payload.indentSize) || payload.indentSize < 1 || payload.indentSize > 8)) throw new Error("Invalid indent size.");
  return payload as unknown as InitializePayload;
}

function openDocumentPayload(message: HostMessage): OpenDocumentPayload {
  const payload = objectPayload(message);
  if (typeof payload.documentId !== "string" || typeof payload.content !== "string" || (payload.language !== undefined && payload.language !== "json")) throw new Error("Invalid document payload.");
  return payload as unknown as OpenDocumentPayload;
}

function replaceContentPayload(message: HostMessage): ReplaceContentPayload {
  const payload = objectPayload(message);
  if (typeof payload.content !== "string") throw new Error("Invalid content payload.");
  return payload as unknown as ReplaceContentPayload;
}

function themePayload(message: HostMessage): ThemeName {
  const payload = objectPayload(message);
  if (payload.theme !== "vs" && payload.theme !== "vs-dark") throw new Error("Invalid theme.");
  return payload.theme;
}

function foldLevelPayload(message: HostMessage): FoldLevelPayload {
  const payload = objectPayload(message);
  if (!Number.isInteger(payload.level)) throw new Error("Invalid fold level.");
  return { level: payload.level as number };
}

function diffPayload(message: HostMessage): EnterDiffPayload {
  const payload = objectPayload(message);
  const isDocument = (value: unknown): value is { documentId: string; content: string } => typeof value === "object" && value !== null && typeof (value as Record<string, unknown>).documentId === "string" && typeof (value as Record<string, unknown>).content === "string";
  if (!isDocument(payload.original) || !isDocument(payload.modified)) throw new Error("Invalid diff payload.");
  return payload as unknown as EnterDiffPayload;
}
