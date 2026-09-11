import * as monaco from "monaco-editor";
import type { EnterDiffPayload } from "./protocol";

export class DiffView {
  private editor: monaco.editor.IStandaloneDiffEditor | null = null;
  private originalModel: monaco.editor.ITextModel | null = null;
  private modifiedModel: monaco.editor.ITextModel | null = null;

  show(container: HTMLElement, payload: EnterDiffPayload, theme: string): void {
    this.dispose();
    container.replaceChildren();
    this.editor = monaco.editor.createDiffEditor(container, {
      automaticLayout: true,
      readOnly: true,
      renderSideBySide: true,
      scrollBeyondLastLine: false,
      theme,
    });
    this.originalModel = monaco.editor.createModel(
      formatJsonProjection(payload.original.content),
      "json",
      monaco.Uri.parse(`inmemory://juju/diff/original/${encodeURIComponent(payload.original.documentId)}.json`),
    );
    this.modifiedModel = monaco.editor.createModel(
      formatJsonProjection(payload.modified.content),
      "json",
      monaco.Uri.parse(`inmemory://juju/diff/modified/${encodeURIComponent(payload.modified.documentId)}.json`),
    );
    this.editor.setModel({ original: this.originalModel, modified: this.modifiedModel });
  }

  dispose(): void {
    this.editor?.dispose();
    this.editor = null;
    this.originalModel?.dispose();
    this.originalModel = null;
    this.modifiedModel?.dispose();
    this.modifiedModel = null;
  }
}

function formatJsonProjection(content: string): string {
  try {
    return JSON.stringify(JSON.parse(content), null, 2);
  } catch {
    return content;
  }
}
