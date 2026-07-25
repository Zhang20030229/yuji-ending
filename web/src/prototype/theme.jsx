import { createContext, useContext } from "react";

export const theme = Object.freeze({
  colors: {
    canvas: "#eee9de",
    background: "#f8f4ea",
    surface: "#fffdf7",
    surfaceGlass: "rgba(255, 253, 247, 0.74)",
    glass: "rgba(255, 253, 247, 0.58)",
    glassBorder: "rgba(255, 255, 255, 0.78)",
    text: "#292b28",
    textMuted: "#75746d",
    primary: "#267b73",
    primaryStrong: "#1f6e68",
    primarySoft: "#e6f0eb",
    primaryFaint: "#edf6f2",
    accent: "#cd731c",
    accentSoft: "#faead5",
    border: "#e7dfd1",
    borderStrong: "#d8cfc0",
    chartGrid: "#ddd9cf",
    chartSecondary: "#8eb8ae",
    success: "#4c7d55",
    danger: "#a45545",
    white: "#ffffff",
  },
  shadows: {
    frame: "0 16px 48px rgba(76, 69, 47, 0.12)",
    card: "0 2px 10px rgba(88, 76, 47, 0.045)",
    floating: "0 6px 18px rgba(76, 69, 47, 0.075)",
    glass: "0 4px 16px rgba(76, 69, 47, 0.065), inset 0 1px 0 rgba(255, 255, 255, 0.82)",
  },
  effects: {
    glassBlur: "blur(22px) saturate(1.5)",
  },
});

const cssVariables = {
  "--color-canvas": theme.colors.canvas,
  "--color-background": theme.colors.background,
  "--color-surface": theme.colors.surface,
  "--color-surface-glass": theme.colors.surfaceGlass,
  "--color-glass": theme.colors.glass,
  "--color-glass-border": theme.colors.glassBorder,
  "--color-text": theme.colors.text,
  "--color-muted": theme.colors.textMuted,
  "--color-primary": theme.colors.primary,
  "--color-primary-strong": theme.colors.primaryStrong,
  "--color-primary-soft": theme.colors.primarySoft,
  "--color-primary-faint": theme.colors.primaryFaint,
  "--color-accent": theme.colors.accent,
  "--color-accent-soft": theme.colors.accentSoft,
  "--color-border": theme.colors.border,
  "--color-border-strong": theme.colors.borderStrong,
  "--color-chart-grid": theme.colors.chartGrid,
  "--color-chart-secondary": theme.colors.chartSecondary,
  "--color-success": theme.colors.success,
  "--color-danger": theme.colors.danger,
  "--color-white": theme.colors.white,
  "--shadow-frame": theme.shadows.frame,
  "--shadow-card": theme.shadows.card,
  "--shadow-floating": theme.shadows.floating,
  "--shadow-glass": theme.shadows.glass,
  "--glass-blur": theme.effects.glassBlur,
};

const ThemeContext = createContext(theme);

export function ThemeProvider({ children }) {
  return (
    <ThemeContext.Provider value={theme}>
      <div className="theme-root" style={cssVariables}>
        {children}
      </div>
    </ThemeContext.Provider>
  );
}

export function useTheme() {
  return useContext(ThemeContext);
}
