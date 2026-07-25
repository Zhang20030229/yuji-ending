import { apiFetch, apiJson, clearAccessToken, setAccessToken } from "./client";

interface AuthTokenResponse {
  accessToken: string;
  expiresAt: string;
}

/** 当前登录用户及其最小个人资料。 */
export interface UserIdentity {
  username: string;
  displayName: string;
  gender: "Male" | "Female" | "";
  birthYear?: number;
  birthMonth?: number;
  age?: number;
  aiName: string;
  profileCompleted: boolean;
}

const ONBOARDING_PENDING_KEY = "yuji.onboarding.pending";

export async function register(username: string, password: string) {
  const token = await apiJson<AuthTokenResponse>("/auth/register", {
    method: "POST",
    body: JSON.stringify({ username, password }),
  });
  setAccessToken(token.accessToken);
  window.localStorage.setItem(ONBOARDING_PENDING_KEY, "true");
}

/** 新注册流程未完成引导时返回 true；普通登录和既有账号不受影响。 */
export function hasPendingOnboarding() {
  return window.localStorage.getItem(ONBOARDING_PENDING_KEY) === "true";
}

export async function login(username: string, password: string) {
  const token = await apiJson<AuthTokenResponse>("/auth/login", {
    method: "POST",
    body: JSON.stringify({ username, password }),
  });
  setAccessToken(token.accessToken);
}

export async function logout() {
  try {
    await apiFetch("/auth/logout", { method: "POST" });
  } finally {
    clearAccessToken();
    window.sessionStorage.removeItem("echora.launch-session-id");
  }
}

export function getMe() {
  return apiJson<UserIdentity>("/me");
}

export function saveProfile(request: {
  displayName: string;
  gender: "Male" | "Female";
  birthYear: number;
  birthMonth: number;
  aiName: string;
}) {
  return apiJson<UserIdentity>("/me/profile", {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

/** 把 API 中的出生年月转换为 month 输入框使用的值。 */
export function toBirthMonthValue(birthYear?: number, birthMonth?: number) {
  return birthYear && birthMonth ? `${birthYear}-${String(birthMonth).padStart(2, "0")}` : "";
}

/** 把 month 输入框值拆成后端明确字段。 */
export function parseBirthMonth(value: string) {
  const [birthYear, birthMonth] = value.split("-").map(Number);
  return { birthYear, birthMonth };
}

/** 在保存前即时显示与后端同口径、按年月计算的年龄。 */
export function calculateAgeFromBirthMonth(value: string) {
  if (!value) return undefined;
  const { birthYear, birthMonth } = parseBirthMonth(value);
  const now = new Date();
  return now.getFullYear() - birthYear - (now.getMonth() + 1 < birthMonth ? 1 : 0);
}

export async function changePassword(currentPassword: string, newPassword: string) {
  const token = await apiJson<AuthTokenResponse>("/me/password", {
    method: "PUT",
    body: JSON.stringify({ currentPassword, newPassword }),
  });
  setAccessToken(token.accessToken);
}

export async function deleteAccount() {
  await apiFetch("/me", { method: "DELETE" });
  clearAccessToken();
  window.sessionStorage.removeItem("echora.launch-session-id");
}
