export const PROTOCOL_VERSION = 1 as const;

export type ThemeName = "vs" | "vs-dark";

export interface EditorMessage<TType extends string = string, TPayload = unknown> {
  version: typeof PROTOCOL_VERSION;
  type: TType;
  requestId: string | null;
  payload: TPayload;
}

export interface DocumentPayload {
  documentId: string;
  content: string;
}

export interface InitializePayload {
  theme?: ThemeName;
  indentSize?: number;
}

export interface OpenDocumentPayload extends DocumentPayload {
  language?: "json";
}

export interface ReplaceContentPayload {
  content: string;
}

export interface FoldLevelPayload {
  level: number;
}

export interface EnterDiffPayload {
  original: DocumentPayload;
  modified: DocumentPayload;
}

export type HostCommand =
  | "initialize"
  | "openDocument"
  | "replaceContent"
  | "setTheme"
  | "format"
  | "foldAll"
  | "unfoldAll"
  | "unfoldLevel"
  | "foldLevel"
  | "enterDiff"
  | "exitDiff"
  | "focus"
  | "getContent";

export type HostMessage = EditorMessage<HostCommand, unknown>;

export type CommandResultPayload =
  | { ok: true; result: unknown }
  | { ok: false; error: string };

export interface ValidationPayload {
  hasErrors: boolean;
  markers: Array<{
    severity: string;
    message: string;
    startLineNumber: number;
    startColumn: number;
    endLineNumber: number;
    endColumn: number;
  }>;
}

export type EditorEvent =
  | "ready"
  | "contentChanged"
  | "cursorChanged"
  | "validationChanged"
  | "saveRequested"
  | "editorFocused"
  | "commandResult";

export type EditorEventMessage = EditorMessage<EditorEvent, unknown>;

const hostCommands: ReadonlySet<string> = new Set([
  "initialize", "openDocument", "replaceContent", "setTheme", "format", "foldAll",
  "unfoldAll", "unfoldLevel", "foldLevel", "enterDiff", "exitDiff", "focus", "getContent",
]);

export function isHostMessage(value: unknown): value is HostMessage {
  if (!isEnvelope(value)) return false;
  return hostCommands.has(value.type);
}

export function isEnvelope(value: unknown): value is EditorMessage {
  if (typeof value !== "object" || value === null) return false;
  const message = value as Partial<EditorMessage>;
  return message.version === PROTOCOL_VERSION
    && typeof message.type === "string"
    && (typeof message.requestId === "string" || message.requestId === null)
    && Object.prototype.hasOwnProperty.call(message, "payload");
}
