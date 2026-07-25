import { categoryById, categoryLabel, RECOGNITION_CATEGORIES } from "./categories.js";

export function RecognitionList({
  language,
  recognitions,
  activeCategory,
  selectedId,
  onCategoryChange,
  onSelect,
  copy,
}) {
  return (
    <div className="life-tree-list">
      <div className="life-tree-filters" role="tablist" aria-label={copy("认识分类", "Recognition categories")}>
        <button
          type="button"
          role="tab"
          aria-selected={activeCategory === "all"}
          className={activeCategory === "all" ? "is-active" : undefined}
          onClick={() => onCategoryChange("all")}
        >
          {copy("全部", "All")}
        </button>
        {RECOGNITION_CATEGORIES.map((category) => (
          <button
            key={category.id}
            type="button"
            role="tab"
            aria-selected={activeCategory === category.id}
            className={activeCategory === category.id ? "is-active" : undefined}
            style={
              activeCategory === category.id
                ? { background: category.color, borderColor: category.color }
                : undefined
            }
            onClick={() => onCategoryChange(category.id)}
          >
            {language === "zh" ? category.zh : category.en}
          </button>
        ))}
      </div>

      <header className="life-tree-list__intro">
        <h2>{copy("从谈话里慢慢认识自己", "Getting to know yourself from conversations")}</h2>
        <p>{copy("按固定分类查找，每条都保留你的原话。", "Browse fixed categories — each entry keeps your own words.")}</p>
      </header>

      <div className="life-tree-cards">
        {recognitions.length === 0 ? (
          <p className="life-tree-empty">{copy("这个分类还没有认识。", "No recognitions in this category yet.")}</p>
        ) : (
          recognitions.map((item) => {
            const meta = categoryById[item.category];
            const selected = item.id === selectedId;
            return (
              <article
                key={item.id}
                id={`recognition-${item.id}`}
                className={`life-tree-card${selected ? " is-selected" : ""}`}
                style={{
                  background: meta?.soft ?? "var(--color-surface)",
                  borderColor: selected ? meta?.color : "transparent",
                }}
                onClick={() => onSelect(item.id)}
                onKeyDown={(event) => {
                  if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    onSelect(item.id);
                  }
                }}
                role="button"
                tabIndex={0}
              >
                <div className="life-tree-card__meta">
                  <span style={{ color: meta?.text ?? "var(--color-muted)" }}>
                    {categoryLabel(item.category, language)}
                  </span>
                  <small>{item.meta}</small>
                </div>
                <h3>{item.title}</h3>
                <p>{item.content}</p>
                {item.quote ? <blockquote>“{item.quote}”</blockquote> : null}
              </article>
            );
          })
        )}
      </div>
    </div>
  );
}
