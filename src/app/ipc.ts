import { invoke } from "@tauri-apps/api/core";

export type LauncherTarget = "json" | "settings";

interface LauncherReady {
  timeoutMs: number;
  armed: boolean;
}

export function notifyWindowReady() {
  return invoke<LauncherReady>("window_ready");
}

export function closeLauncher() {
  return invoke<void>("launcher_close");
}

export function openLauncherTarget(target: LauncherTarget) {
  return invoke<void>("launcher_open_target", { target });
}

export interface JsonDocument {
  documentId: string;
  revision: string;
  content: string;
}

export interface JsonDocumentSummary {
  documentId: string;
  revision: string;
}

export function listJsonDocuments() {
  return invoke<JsonDocumentSummary[]>("json_list_documents");
}

export function createJsonDocument() {
  return invoke<JsonDocument>("json_create_document");
}

export function readJsonDocument(documentId: string) {
  return invoke<JsonDocument>("json_read_document", { documentId });
}

export function writeJsonDocument(document: JsonDocument) {
  return invoke<JsonDocument>("json_write_document", {
    documentId: document.documentId,
    content: document.content,
    expectedRevision: document.revision,
  });
}

export function renameJsonDocument(documentId: string, newName: string) {
  return invoke<void>("json_rename_document", { documentId, newName });
}

export function deleteJsonDocument(documentId: string) {
  return invoke<void>("json_delete_document", { documentId });
}

export interface AppSettings { data_root: string; app: { autostart: boolean }; launcher: { shortcut: string; timeout_ms: number }; storage: { watch_external_changes: boolean; autosave_debounce_ms: number } }
export function getSettings() { return invoke<AppSettings>("app_get_settings"); }
export function updateLauncherSettings(shortcut: string, timeoutMs: number) { return invoke<void>("app_update_launcher_settings", { shortcut, timeoutMs }); }
export function setAutostart(enabled: boolean) { return invoke<void>("app_set_autostart", { enabled }); }
