import { isHostMessage, PROTOCOL_VERSION, type EditorEvent, type EditorEventMessage, type HostMessage } from "./protocol";

type MessageHandler = (message: HostMessage) => void | Promise<void>;

interface WebView {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: MessageEvent<unknown>) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebView };
  }
}

export class Bridge {
  private readonly handlers = new Set<MessageHandler>();

  constructor() {
    window.chrome?.webview?.addEventListener("message", (event) => {
      const message = event.data;
      if (!isHostMessage(message)) return;

      for (const handler of this.handlers) {
        void handler(message);
      }
    });
  }

  onMessage(handler: MessageHandler): () => void {
    this.handlers.add(handler);
    return () => this.handlers.delete(handler);
  }

  send<TPayload>(type: EditorEvent, payload: TPayload, requestId: string | null = null): void {
    const message: EditorEventMessage = {
      version: PROTOCOL_VERSION,
      type,
      requestId,
      payload,
    };
    window.chrome?.webview?.postMessage(message);
  }
}
