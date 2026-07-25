/** Recognition categories + leaf/chip colors (aligned with product taxonomy). */

export const RECOGNITION_CATEGORIES = [
  { id: "Identity", zh: "身份", en: "Identity", color: "#5a9a8e", soft: "#e4f1ed", text: "#2f6b61" },
  { id: "Trait", zh: "特质", en: "Trait", color: "#c47a6a", soft: "#f7ebe7", text: "#8f4f42" },
  { id: "Value", zh: "价值", en: "Value", color: "#c98b3a", soft: "#f8ecd8", text: "#8f5f1f" },
  { id: "Preference", zh: "偏好", en: "Preference", color: "#7a8fc4", soft: "#e8ecf6", text: "#445889" },
  { id: "Habit", zh: "习惯", en: "Habit", color: "#6a9a5c", soft: "#e8f1e4", text: "#3f6b35" },
  { id: "Ability", zh: "能力", en: "Ability", color: "#5a8fb8", soft: "#e4eef5", text: "#355f82" },
  { id: "Need", zh: "需求", en: "Need", color: "#8b74d6", soft: "#eee8f8", text: "#5c4899" },
  { id: "Goal", zh: "目标", en: "Goal", color: "#d4a04a", soft: "#f7efd9", text: "#8a6520" },
  { id: "Relationship", zh: "关系", en: "Relationship", color: "#c46a8f", soft: "#f6e8ef", text: "#8a3f5f" },
];

export const categoryById = Object.fromEntries(
  RECOGNITION_CATEGORIES.map((category) => [category.id, category]),
);

export function categoryLabel(categoryId, language) {
  const category = categoryById[categoryId];
  if (!category) return categoryId;
  return language === "zh" ? category.zh : category.en;
}
