import type { ReactNode, UIEventHandler } from "react";
import { useCallback, useState } from "react";
import { IconChevronLeft } from "@tabler/icons-react";

type SecondaryPageHeaderProps = {
  title: string;
  subtitle?: string;
  actions?: ReactNode;
  onBack: () => void;
  backLabel?: string;
  scrolled?: boolean;
  titleId?: string;
};

/** 二级页面统一导航：返回路径保持空间一致，滚动后提升为更清晰的玻璃材质。 */
export default function SecondaryPageHeader({
  title,
  subtitle,
  actions,
  onBack,
  backLabel = "返回",
  scrolled = false,
  titleId,
}: SecondaryPageHeaderProps) {
  return (
    <header className={`secondary-page-header${scrolled ? " is-scrolled" : ""}`}>
      <div className="secondary-page-header__inner">
        <button type="button" className="secondary-page-header__back" onClick={onBack} aria-label={backLabel}>
          <IconChevronLeft aria-hidden />
        </button>
        <div className="secondary-page-header__copy">
          <h1 id={titleId}>{title}</h1>
          {subtitle ? <p>{subtitle}</p> : null}
        </div>
        {actions ? <div className="secondary-page-header__actions">{actions}</div> : null}
      </div>
    </header>
  );
}

/** 绑定到页面真正的滚动容器，避免只根据窗口滚动做出错误的材质反馈。 */
export function useSecondaryHeaderScroll() {
  const [scrolled, setScrolled] = useState(false);
  const onScroll = useCallback<UIEventHandler<HTMLElement>>((event) => {
    setScrolled((event.target as HTMLElement).scrollTop > 4);
  }, []);
  return { scrolled, onScroll };
}
