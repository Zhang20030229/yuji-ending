import "@/styles/combined/index.css";
import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { getRecognitions } from "@/api/self";
import { toTreeRecognitions } from "@/api/map-data";
import SecondaryPageHeader, { useSecondaryHeaderScroll } from "@/components/layout/SecondaryPageHeader";
import { LifeTreeCanvas } from "./vendor/LifeTreeCanvas.jsx";
import { RecognitionList } from "./vendor/RecognitionList.jsx";
import { useI18n } from "@/prototype/i18n.jsx";
import { backwards } from "@/lib/navigation";
import { useDataRevision } from "@/components/realtime/DataUpdates";

/** 「生命之树」：真实认识数据渲染为可交互的叶子，列表与画布双向联动。 */
export default function TreePage() {
  const navigate = useNavigate();
  const { language, t } = useI18n();
  const copy = (zh, en) => (language === "zh" ? zh : en);
  const [items, setItems] = useState([]);
  const [activeCategory, setActiveCategory] = useState("all");
  const [selectedId, setSelectedId] = useState(null);
  const [focusKey, setFocusKey] = useState(0);
  const [error, setError] = useState("");
  const { scrolled, onScroll } = useSecondaryHeaderScroll();
  const revision = useDataRevision("self");

  useEffect(() => {
    const c = new AbortController();
    getRecognitions("", "", c.signal)
      .then((rows) => setItems(toTreeRecognitions(rows)))
      .catch((e) => {
        if (e.name !== "AbortError") setError(e.message ?? String(e));
      });
    return () => c.abort();
  }, [revision]);

  const filtered = useMemo(
    () => (activeCategory === "all" ? items : items.filter((i) => i.category === activeCategory)),
    [activeCategory, items],
  );

  useEffect(() => {
    if (!selectedId) return;
    document.getElementById(`recognition-${selectedId}`)?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }, [selectedId]);

  const handleCategoryChange = (category) => {
    setActiveCategory(category);
    setSelectedId(null);
  };

  const handleSelect = (id) => {
    const item = items.find((entry) => entry.id === id);
    if (item && activeCategory !== "all" && item.category !== activeCategory) {
      setActiveCategory("all");
    }
    setSelectedId(id);
    setFocusKey((key) => key + 1);
  };

  return (
    <section className="life-tree-page" aria-label={t("tree.title")}>
      <SecondaryPageHeader
        title={t("tree.title")}
        subtitle={copy(`${items.length} 条认识`, `${items.length} recognitions`)}
        onBack={() => backwards(navigate, "/app/home")}
        backLabel={copy("返回首页", "Back to home")}
        scrolled={scrolled}
      />
      <div className="life-tree-screen" onScrollCapture={onScroll}>
        <div className="life-tree-layout">
          <div className="life-tree-stage">
            <LifeTreeCanvas
              recognitions={items}
              activeCategory={activeCategory}
              selectedId={selectedId}
              focusKey={focusKey}
              onSelect={handleSelect}
            />
          </div>
          <RecognitionList
            language={language}
            recognitions={filtered}
            activeCategory={activeCategory}
            selectedId={selectedId}
            onCategoryChange={handleCategoryChange}
            onSelect={handleSelect}
            copy={copy}
          />
        </div>
        {error ? <p role="alert">{error}</p> : null}
      </div>
    </section>
  );
}
