import { createContext, useContext } from "react";
import type { UserIdentity } from "@/api/auth";

/** 当前页面树的登录用户上下文。 */
export const UserContext = createContext<UserIdentity | null>(null);

/** 让一级页面读取当前用户与动态 AI 名称。 */
export function useUser() {
  const user = useContext(UserContext);
  if (!user) throw new Error("User identity is not ready.");
  return user;
}
