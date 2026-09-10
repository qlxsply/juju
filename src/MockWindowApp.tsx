const labels: Record<string, { eyebrow: string; title: string; copy: string }> = {
  settings: {
    eyebrow: "System window",
    title: "设置",
    copy: "Settings UI will be implemented in Phase 7.",
  },
  tool: {
    eyebrow: "Tool window",
    title: "JSON",
    copy: "JSON editor UI will be implemented in Phase 5.",
  },
};

export function MockWindowApp({ view }: { view: string }) {
  const content = labels[view] ?? labels.tool;

  return (
    <main className="bootstrap-shell">
      <section className="bootstrap-card">
        <span className="bootstrap-mark" aria-hidden="true">
          J
        </span>
        <div>
          <p className="bootstrap-eyebrow">{content.eyebrow}</p>
          <h1>{content.title}</h1>
          <p className="bootstrap-copy">{content.copy}</p>
        </div>
      </section>
    </main>
  );
}
