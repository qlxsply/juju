import { useEffect, useEffectEvent, useRef, useState } from "react";
import type * as Monaco from "monaco-editor";

import {
  createJsonDocument,
  deleteJsonDocument,
  listJsonDocuments,
  readJsonDocument,
  renameJsonDocument,
  type JsonDocument,
  type JsonDocumentSummary,
  writeJsonDocument,
} from "../../app/ipc";
import { JsonDiff } from "./JsonDiff";
import "./json.css";

const AUTOSAVE_DELAY_MS = 800;

export function JsonToolApp() {
  const hostRef = useRef<HTMLDivElement>(null);
  const editorRef = useRef<Monaco.editor.IStandaloneCodeEditor | null>(null);
  const modelRef = useRef<Monaco.editor.ITextModel | null>(null);
  const currentRef = useRef<JsonDocument | null>(null);
  const autosaveTimerRef = useRef<number | null>(null);
  const [documents, setDocuments] = useState<JsonDocumentSummary[]>([]);
  const [current, setCurrent] = useState<JsonDocument | null>(null);
  const [status, setStatus] = useState("正在加载文档...");
  const [compareOriginal, setCompareOriginal] = useState<JsonDocument | null>(null);
  const [compareModified, setCompareModified] = useState<JsonDocument | null>(null);

  useEffect(() => {
    currentRef.current = current;
  }, [current]);

  const selectDocument = useEffectEvent(async (documentId: string) => {
    const document = await readJsonDocument(documentId);
    currentRef.current = document;
    setCurrent(document);
    modelRef.current?.setValue(document.content);
    setStatus("已保存");
  });

  useEffect(() => {
    void (async () => {
      const listed = await listJsonDocuments();
      if (listed.length === 0) {
        const created = await createJsonDocument();
        setDocuments([{ documentId: created.documentId, revision: created.revision }]);
        setCurrent(created);
      } else {
        setDocuments(listed);
        await selectDocument(listed[0].documentId);
      }
    })().catch((error: unknown) => setStatus(String(error)));
  }, []);

  useEffect(() => {
    let disposed = false;
    void import("monaco-editor").then((monaco) => {
      if (disposed || !hostRef.current) return;
      const model = monaco.editor.createModel(current?.content ?? "", "json");
      monaco.editor.setTheme("vs");
      const editor = monaco.editor.create(hostRef.current, {
        model,
        automaticLayout: true,
        folding: true,
        minimap: { enabled: false },
        scrollBeyondLastLine: false,
        tabSize: 2,
      });
      modelRef.current = model;
      editorRef.current = editor;
      editor.onDidChangeModelContent(() => {
        const document = currentRef.current;
        if (!document) return;
        setStatus("未保存");
        if (autosaveTimerRef.current !== null) window.clearTimeout(autosaveTimerRef.current);
        autosaveTimerRef.current = window.setTimeout(() => {
          const content = editor.getValue();
          void writeJsonDocument({ ...document, content })
            .then((saved) => {
              currentRef.current = saved;
              setCurrent(saved);
              setDocuments((items) => items.map((item) => item.documentId === saved.documentId ? { documentId: saved.documentId, revision: saved.revision } : item));
              setStatus("已保存");
            })
            .catch((error: unknown) => setStatus(`保存失败: ${String(error)}`));
        }, AUTOSAVE_DELAY_MS);
      });
    });
    return () => {
      disposed = true;
      editorRef.current?.dispose();
      modelRef.current?.dispose();
      if (autosaveTimerRef.current !== null) window.clearTimeout(autosaveTimerRef.current);
      editorRef.current = null;
      modelRef.current = null;
    };
  }, []);

  async function createDocument() {
    const document = await createJsonDocument();
    setDocuments((items) => [...items, { documentId: document.documentId, revision: document.revision }]);
    await selectDocument(document.documentId);
    editorRef.current?.focus();
  }

  function formatDocument() {
    editorRef.current?.getAction("editor.action.formatDocument")?.run();
  }

  function copyText(minified: boolean) {
    const content = editorRef.current?.getValue() ?? "";
    try {
      const text = minified ? JSON.stringify(JSON.parse(content)) : content;
      void navigator.clipboard.writeText(text).then(() => setStatus("已复制"));
    } catch {
      setStatus("JSON 格式错误");
    }
  }

  async function renameDocument() {
    if (!current) return;
    const name = window.prompt("输入新文件名", current.documentId.replace(/\.json$/, ""));
    if (!name) return;
    const next = name.endsWith(".json") ? name : `${name}.json`;
    await renameJsonDocument(current.documentId, next);
    setDocuments(await listJsonDocuments());
    await selectDocument(next);
  }

  async function removeDocument() {
    if (!current || !window.confirm(`移动 ${current.documentId} 到回收站？`)) return;
    await deleteJsonDocument(current.documentId);
    const listed = await listJsonDocuments();
    setDocuments(listed);
    if (listed[0]) await selectDocument(listed[0].documentId);
    else await createDocument();
  }

  async function chooseComparison(documentId: string) {
    if (!compareOriginal || documentId === compareOriginal.documentId) return;
    setCompareModified(await readJsonDocument(documentId));
  }

  function exitComparison() {
    setCompareOriginal(null);
    setCompareModified(null);
  }

  function swapComparison() {
    setCompareOriginal(compareModified);
    setCompareModified(compareOriginal);
  }

  return (
    <main className="json-tool">
      <aside className="json-sidebar">
        <div className="json-sidebar-header"><span>DOCUMENTS</span><button onClick={() => void createDocument()}>新建</button></div>
        {documents.map((document) => <button className={document.documentId === (compareOriginal ? compareModified?.documentId : current?.documentId) ? "is-selected" : ""} disabled={document.documentId === compareOriginal?.documentId} key={document.documentId} onClick={() => void (compareOriginal ? chooseComparison(document.documentId) : selectDocument(document.documentId))}>{document.documentId.replace(/\.json$/, "")}</button>)}
      </aside>
      <section className="json-workspace">
        <header className="json-toolbar">
          <strong>{compareOriginal ? `${compareOriginal.documentId} / ${compareModified?.documentId ?? "选择对比文档"}` : current?.documentId ?? "JSON"}</strong>
          {compareOriginal ? <div><button disabled={!compareModified} onClick={swapComparison}>交换左右</button><button onClick={exitComparison}>退出对比</button></div> : <div><button onClick={formatDocument}>格式化</button><button onClick={() => copyText(false)}>复制</button><button onClick={() => copyText(true)}>压缩复制</button><button onClick={() => editorRef.current?.getAction("editor.foldAll")?.run()}>折叠</button><button onClick={() => editorRef.current?.getAction("editor.unfoldAll")?.run()}>展开</button><button disabled={!current} onClick={() => setCompareOriginal(current)}>对比</button><button onClick={() => void renameDocument()}>重命名</button><button onClick={() => void removeDocument()}>删除</button></div>}
        </header>
        <div className={compareOriginal ? "json-editor is-hidden" : "json-editor"} ref={hostRef} />
        {compareOriginal && !compareModified && <div className="json-compare-select">从左侧选择另一个文档开始对比</div>}
        {compareOriginal && compareModified && <JsonDiff modified={compareModified} original={compareOriginal} />}
        <footer>{compareOriginal && !compareModified ? "选择右侧对比文档" : status}</footer>
      </section>
    </main>
  );
}
