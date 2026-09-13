import * as monaco from "monaco-editor";
import { Bridge } from "./bridge";
import { DiffView } from "./diff";
import type {
  EnterDiffPayload,
  FoldLevelPayload,
  InitializePayload,
  JujuBinding,
  JujuContext,
  MonacoBinding,
  MonacoCommand,
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
  private monacoBindings: MonacoBinding[] = [];
  private jujuBindings: JujuBinding[] = [];
  private pendingShortcut: string | null = null;
  private pendingShortcutAt = 0;

  constructor(private readonly container: HTMLElement, private readonly bridge: Bridge) {}

  initialize(payload: InitializePayload): void {
    this.theme = payload.theme ?? this.theme;
    this.indentSize = payload.indentSize ?? this.indentSize;
    this.monacoBindings = payload.monacoBindings ?? [];
    this.jujuBindings = payload.jujuBindings ?? [];
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

  handleShortcut(event: KeyboardEvent): boolean {
    if (event.repeat || event.getModifierState("AltGraph")) return false;
    const stroke = shortcutStroke(event);
    if (!stroke) return false;
    const context: JujuContext = this.inDiffMode ? "Diff" : "Editor";
    const bindings = this.jujuBindings.filter((binding) => binding.context === context);
    const now = Date.now();
    if (this.pendingShortcut !== null && now - this.pendingShortcutAt <= 1000) {
      const chord = `${this.pendingShortcut} ${stroke}`;
      this.pendingShortcut = null;
      if (this.handleShortcutCombination(chord, bindings, now)) return true;
    } else {
      this.pendingShortcut = null;
    }
    return this.handleShortcutCombination(stroke, bindings, now);
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
    this.configureMonacoBindings();
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

  private handleShortcutCombination(combination: string, bindings: JujuBinding[], now: number): boolean {
    if (bindings.some((binding) => binding.shortcut === combination)) {
      this.bridge.send("shortcut", { combination });
      return true;
    }
    if (!bindings.some((binding) => binding.shortcut.startsWith(`${combination} `))) return false;
    this.pendingShortcut = combination;
    this.pendingShortcutAt = now;
    return true;
  }

  private configureMonacoBindings(): void {
    const editor = this.getEditor();
    for (const binding of this.monacoBindings) {
      editor.addCommand(monacoKeybinding(binding.shortcut), () => this.executeMonacoCommand(binding.command));
    }
  }

  private executeMonacoCommand(command: MonacoCommand): void {
    switch (command) {
      case "Save":
        this.bridge.send("saveRequested", { content: this.getContent() });
        break;
      case "Format":
        void this.format().catch(() => undefined);
        break;
      case "FoldAll":
        void this.foldAll();
        break;
      case "UnfoldAll":
        void this.unfoldAll();
        break;
    }
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

function monacoKeybinding(shortcut: string): number {
  const strokes = shortcut.split(" ");
  const bindings = strokes.map(monacoKeybindingStroke);
  return bindings.length === 2 ? monaco.KeyMod.chord(bindings[0], bindings[1]) : bindings[0];
}

function monacoKeybindingStroke(stroke: string): number {
  const parts = stroke.split("+");
  const key = parts.at(-1);
  if (!key) throw new Error(`Invalid shortcut '${stroke}'.`);
  let binding = monacoKeyCode(key);
  if (parts.includes("Ctrl")) binding |= monaco.KeyMod.CtrlCmd;
  if (parts.includes("Alt")) binding |= monaco.KeyMod.Alt;
  if (parts.includes("Shift")) binding |= monaco.KeyMod.Shift;
  return binding;
}

function monacoKeyCode(key: string): monaco.KeyCode {
  if (/^[A-Z]$/.test(key)) return monaco.KeyCode.KeyA + key.charCodeAt(0) - "A".charCodeAt(0);
  if (/^\d$/.test(key)) return monaco.KeyCode.Digit0 + Number(key);
  if (/^F(?:[1-9]|1\d|2[0-4])$/.test(key)) return monaco.KeyCode.F1 + Number(key.slice(1)) - 1;
  const keys: Record<string, monaco.KeyCode> = {
    Escape: monaco.KeyCode.Escape, Delete: monaco.KeyCode.Delete, Backspace: monaco.KeyCode.Backspace,
    Enter: monaco.KeyCode.Enter, Space: monaco.KeyCode.Space, Tab: monaco.KeyCode.Tab,
    Up: monaco.KeyCode.UpArrow, Down: monaco.KeyCode.DownArrow, Left: monaco.KeyCode.LeftArrow, Right: monaco.KeyCode.RightArrow,
    Home: monaco.KeyCode.Home, End: monaco.KeyCode.End, PageUp: monaco.KeyCode.PageUp, PageDown: monaco.KeyCode.PageDown, Insert: monaco.KeyCode.Insert,
  };
  if (!(key in keys)) throw new Error(`Unsupported Monaco shortcut key '${key}'.`);
  return keys[key];
}

function shortcutStroke(event: KeyboardEvent): string | null {
  const keys: Record<string, string> = {
    Escape: "Escape", Delete: "Delete", Backspace: "Backspace", Enter: "Enter", " ": "Space", Tab: "Tab",
    ArrowUp: "Up", ArrowDown: "Down", ArrowLeft: "Left", ArrowRight: "Right",
    Home: "Home", End: "End", PageUp: "PageUp", PageDown: "PageDown", Insert: "Insert",
  };
  const key = /^[a-z]$/i.test(event.key) ? event.key.toUpperCase() : /^\d$/.test(event.key) || /^F(?:[1-9]|1\d|2[0-4])$/.test(event.key) ? event.key.toUpperCase() : keys[event.key];
  if (!key) return null;
  return [event.ctrlKey ? "Ctrl" : null, event.altKey ? "Alt" : null, event.shiftKey ? "Shift" : null, key].filter((part): part is string => part !== null).join("+");
}
