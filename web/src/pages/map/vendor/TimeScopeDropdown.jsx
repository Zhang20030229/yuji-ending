import { useEffect, useRef, useState } from "react";
import { CaretDown, Check } from "@phosphor-icons/react";
import { useI18n } from "@/prototype/i18n.jsx";

export function TimeScopeDropdown({
  value,
  onChange,
  allowAll = true,
  disabled = false,
  years = [],
}) {
  const { t } = useI18n();
  const rootRef = useRef(null);
  const [isOpen, setIsOpen] = useState(false);
  const label = value === "all"
    ? t("map.allYears")
    : t("map.selectedYear", { year: value });

  useEffect(() => {
    if (!isOpen) return undefined;

    const closeOnOutsideClick = (event) => {
      if (!rootRef.current?.contains(event.target)) setIsOpen(false);
    };
    const closeOnEscape = (event) => {
      if (event.key === "Escape") setIsOpen(false);
    };

    document.addEventListener("pointerdown", closeOnOutsideClick);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOnOutsideClick);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [isOpen]);

  const chooseScope = (nextValue) => {
    if (disabled || (nextValue === "all" && !allowAll)) return;
    onChange(nextValue);
    setIsOpen(false);
  };

  return (
    <div className="time-scope-dropdown" ref={rootRef}>
      <button
        type="button"
        className="time-scope-dropdown__trigger"
        aria-label={t("map.scope")}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
        aria-disabled={disabled}
        disabled={disabled}
        onClick={() => setIsOpen((open) => !open)}
      >
        <span>{label}</span>
        <CaretDown size={16} weight="bold" />
      </button>

      {isOpen && (
        <div
          className="time-scope-dropdown__menu"
          role="listbox"
          aria-label={t("map.scope")}
        >
          {["all", ...years.map(String)].map((option) => {
            const optionLabel = option === "all"
              ? t("map.allYears")
              : t("map.selectedYear", { year: option });
            const isSelected = value === option;
            const isDisabled = option === "all" && !allowAll;

            return (
              <button
                type="button"
                role="option"
                aria-selected={isSelected}
                aria-disabled={isDisabled}
                disabled={isDisabled}
                key={option}
                className={`${isSelected ? "is-selected" : ""} ${isDisabled ? "is-disabled" : ""}`}
                onClick={() => chooseScope(option)}
              >
                <span>{optionLabel}</span>
                {isSelected && <Check size={15} weight="bold" />}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
