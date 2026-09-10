import { useEffect, useRef, useState } from "react";
import type * as Monaco from "monaco-editor";

import type { JsonDocument } from "../../app/ipc";

function projection(content: string): { content: string; valid: boolean } {
  try {
    return { content: `${JSON.stringify(JSON.parse(content), null, 2)}\n`, valid: true };
  } catch {
    return { content, valid: false };
  }
}

export function JsonDiff({ original, modified }: { original: JsonDocument; modified: JsonDocument }) {
  const hostRef = useRef<HTMLDivElement>(null);
  const [rawFallback, setRawFallback] = useState(false);

  useEffect(() => {
    let disposed = false;
    let editor: Monaco.editor.IStandaloneDiffEditor | undefined;
    let originalModel: Monaco.editor.ITextModel | undefined;
    let modifiedModel: Monaco.editor.ITextModel | undefined;
    const left = projection(original.content);
    const right = projection(modified.content);
    setRawFallback(!left.valid || !right.valid);
    void import("monaco-editor").then((monaco) => {
      if (disposed || !hostRef.current) return;
      originalModel = monaco.editor.createModel(left.content, "json");
      modifiedModel = monaco.editor.createModel(right.content, "json");
      monaco.editor.setTheme("vs");
      editor = monaco.editor.createDiffEditor(hostRef.current, {
        automaticLayout: true,
        enableSplitViewResizing: true,
        renderSideBySide: true,
        readOnly: true,
        minimap: { enabled: false },
        scrollBeyondLastLine: false,
      });
      editor.setModel({ original: originalModel, modified: modifiedModel });
    });
    return () => {
      disposed = true;
      editor?.dispose();
      originalModel?.dispose();
      modifiedModel?.dispose();
    };
  }, [modified, original]);

  return (
    <div className="json-diff-shell">
      {rawFallback && <p className="json-diff-warning">存在非法 JSON，当前按原文比较</p>}
      <div className="json-diff" ref={hostRef} />
    </div>
  );
}
