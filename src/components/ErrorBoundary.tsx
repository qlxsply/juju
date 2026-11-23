import { Component, type ErrorInfo, type ReactNode } from "react";

interface ErrorBoundaryProps {
    children: ReactNode;
}

interface ErrorBoundaryState {
    hasError: boolean;
}

export class ErrorBoundary extends Component<
    ErrorBoundaryProps,
    ErrorBoundaryState
> {
    state: ErrorBoundaryState = { hasError: false };

    static getDerivedStateFromError(): ErrorBoundaryState {
        return { hasError: true };
    }

    componentDidCatch(error: Error, info: ErrorInfo) {
        console.error("Juju UI render error", error, info.componentStack);
    }

    render() {
        if (this.state.hasError) {
            return (
                <main className="app-shell">
                    <section
                        aria-labelledby="error-title"
                        className="welcome-panel"
                    >
                        <div
                            className="app-mark app-mark-error"
                            aria-hidden="true"
                        >
                            !
                        </div>
                        <p className="eyebrow">JUJU DESKTOP</p>
                        <h1 id="error-title">界面暂时无法显示</h1>
                        <p className="welcome-copy">
                            Juju
                            遇到了界面错误。请重新启动应用；如果问题持续，请查看应用日志。
                        </p>
                    </section>
                </main>
            );
        }

        return this.props.children;
    }
}
