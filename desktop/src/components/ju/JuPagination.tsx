import { JuButton } from "./JuButton";

export function JuPagination({
    page,
    pageCount,
    onPageChange,
}: {
    page: number;
    pageCount: number;
    onPageChange: (page: number) => void;
}) {
    const normalizedPageCount = Math.max(1, pageCount);
    const normalizedPage = Math.min(Math.max(1, page), normalizedPageCount);
    return (
        <nav className="ju-pagination" aria-label="分页">
            <JuButton
                aria-label="上一页"
                disabled={normalizedPage <= 1}
                onClick={() => onPageChange(normalizedPage - 1)}
                size="compact"
                variant="outline"
            >
                上一页
            </JuButton>
            <span aria-live="polite">
                第 {normalizedPage} / {normalizedPageCount} 页
            </span>
            <JuButton
                aria-label="下一页"
                disabled={normalizedPage >= normalizedPageCount}
                onClick={() => onPageChange(normalizedPage + 1)}
                size="compact"
                variant="outline"
            >
                下一页
            </JuButton>
        </nav>
    );
}
