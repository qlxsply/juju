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
  | "foldLevel"
  | "enterDiff"
  | "exitDiff"
  | "focus"
  | "getContent";

export type HostMessage = EditorMessage<HostCommand, unknown>;

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
