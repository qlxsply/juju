import EditorWorker from "../node_modules/monaco-editor/esm/vs/editor/editor.worker.js?worker";
import JsonWorker from "../node_modules/monaco-editor/esm/vs/language/json/json.worker.js?worker";

declare global {
    interface Window {
        MonacoEnvironment?: {
            getWorker: (workerId: string, label: string) => Worker;
        };
    }
}

self.MonacoEnvironment = {
    getWorker(_workerId: string, label: string) {
        if (label === "json") return new JsonWorker();
        return new EditorWorker();
    },
};
