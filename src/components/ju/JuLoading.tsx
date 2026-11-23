export function JuLoading({ label = "正在加载" }: { label?: string }) {
    return (
        <div className="ju-loading" role="status" aria-label={label}>
            <span className="ju-spinner" aria-hidden="true" />
            <span>{label}</span>
        </div>
    );
}
