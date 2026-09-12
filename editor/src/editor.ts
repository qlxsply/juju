import * as monaco from "monaco-editor";
import { Bridge } from "./bridge";
import { DiffView } from "./diff";
import type {
  EnterDiffPayload,
  FoldLevelPayload,
  InitializePayload,
  OpenDocumentPayload,
  ReplaceContentPayload,
  ThemeName,
  ValidationPayload,
} from "./protocol";

export class EditorAdapter {
  private readonly diffView = new DiffView();
  private editor: monaco.editor.IStandaloneCodeEditor | null = null;
  private model: monaco.editor.ITextModel | null = null;
  private theme: ThemeName = "vs";
  private indentSize = 2;
  private inDiffMode = false;
  private suppressContentChanged = false;

  constructor(private readonly container: HTMLElement, private readonly bridge: Bridge) {}

  initialize(payload: InitializePayload): void {
    this.theme = payload.theme ?? this.theme;
    this.indentSize = payload.indentSize ?? this.indentSize;
    monaco.editor.setTheme(this.theme);
    this.showEditor();
  }

  openDocument(payload: OpenDocumentPayload): void {
    this.exitDiff();
    this.model?.dispose();
    this.model = monaco.editor.createModel(
      payload.content,
      payload.language ?? "json",
      monaco.Uri.parse(`inmemory://juju/document/${encodeURIComponent(payload.documentId)}.json`),
    );
    this.getEditor().setModel(this.model);
    this.getEditor().updateOptions({ tabSize: this.indentSize, insertSpaces: true });
  }

  replaceContent(payload: ReplaceContentPayload): void {
    const model = this.requireModel();
    this.suppressContentChanged = true;
    try {
      model.setValue(payload.content);
    } finally {
      this.suppressContentChanged = false;
    }
  }

  setTheme(theme: ThemeName): void {
    this.theme = theme;
    monaco.editor.setTheme(theme);
  }

  async format(): Promise<void> {
    JSON.parse(this.requireModel().getValue());
    await this.getEditor().getAction("editor.action.formatDocument")?.run();
  }

  async foldAll(): Promise<void> {
    await this.runAction("editor.foldAll");
  }

  async unfoldAll(): Promise<void> {
    await this.runAction("editor.unfoldAll");
  }

  async unfoldLevel(payload: FoldLevelPayload): Promise<void> {
    const editor = this.getEditor();
    editor.setPosition({ lineNumber: 1, column: 1 });
    const action = editor.getAction("editor.unfold");
    if (!action) throw new Error("Monaco action 'editor.unfold' is unavailable.");
    await action.run({ levels: Math.max(1, Math.min(7, Math.trunc(payload.level))), direction: "down", selectionLines: [0] });
  }

  async foldLevel(payload: FoldLevelPayload): Promise<void> {
    if (!Number.isInteger(payload.level)) throw new Error("Fold level must be an integer.");
    const level = Math.max(1, Math.min(7, Math.trunc(payload.level)));
    await this.runAction(`editor.foldLevel${level}`);
  }

  enterDiff(payload: EnterDiffPayload): void {
    this.inDiffMode = true;
    this.editor?.dispose();
    this.editor = null;
    this.diffView.show(this.container, payload, this.theme);
  }

  exitDiff(): void {
    if (!this.inDiffMode) return;
    this.inDiffMode = false;
    this.diffView.dispose();
    this.showEditor();
  }

  focus(): void {
    if (!this.inDiffMode) this.getEditor().focus();
  }

  getContent(): string {
    return this.requireModel().getValue();
  }

  private showEditor(): void {
    if (this.editor) return;
    this.container.replaceChildren();
    this.editor = monaco.editor.create(this.container, {
      automaticLayout: true,
      language: "json",
      minimap: { enabled: false },
      scrollBeyondLastLine: false,
      tabSize: this.indentSize,
      insertSpaces: true,
      theme: this.theme,
    });
    if (this.model) this.editor.setModel(this.model);
    this.editor.onDidChangeModelContent(() => {
      if (!this.suppressContentChanged && this.model) {
        this.bridge.send("contentChanged", { content: this.model.getValue() });
      }
    });
    this.editor.onDidChangeCursorPosition((event) => {
      this.bridge.send("cursorChanged", {
        lineNumber: event.position.lineNumber,
        column: event.position.column,
      });
    });
    this.editor.onDidFocusEditorText(() => this.bridge.send("editorFocused", {}));
    this.editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
      this.bridge.send("saveRequested", { content: this.getContent() });
    });
    monaco.editor.onDidChangeMarkers((resources) => {
      if (this.model && resources.some((resource) => resource.toString() === this.model?.uri.toString())) {
        this.sendValidation();
      }
    });
  }

  private sendValidation(): void {
    const model = this.requireModel();
    const markers = monaco.editor.getModelMarkers({ resource: model.uri });
    const payload: ValidationPayload = {
      hasErrors: markers.some((marker) => marker.severity === monaco.MarkerSeverity.Error),
      markers: markers.map((marker) => ({
        severity: monaco.MarkerSeverity[marker.severity],
        message: marker.message,
        startLineNumber: marker.startLineNumber,
        startColumn: marker.startColumn,
        endLineNumber: marker.endLineNumber,
        endColumn: marker.endColumn,
      })),
    };
    this.bridge.send("validationChanged", payload);
  }

  private getEditor(): monaco.editor.IStandaloneCodeEditor {
    this.showEditor();
    return this.editor!;
  }

  private requireModel(): monaco.editor.ITextModel {
    if (!this.model) throw new Error("No document is open.");
    return this.model;
  }

  private async runAction(id: string): Promise<void> {
    const action = this.getEditor().getAction(id);
    if (!action) throw new Error(`Monaco action '${id}' is unavailable.`);
    await action.run();
  }
}
