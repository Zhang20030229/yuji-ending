const API_ROOT = "/api";
const ACCESS_TOKEN_KEY = "echora.access-token";
let accessToken = readSessionToken();

/** 后端 ProblemDetails 中前端会使用的安全字段。 */
interface ApiProblem {
  /** 可直接展示的错误标题。 */
  title?: string;
  /** 稳定的机器错误码。 */
  code?: string;
}

/** 一个保留后端 ProblemDetails 错误码的安全请求错误。 */
export class ApiError extends Error {
  /** HTTP 状态码。 */
  readonly status: number;

  /** 后端 ProblemDetails 提供的稳定错误码。 */
  readonly code?: string;

  constructor(message: string, status: number, code?: string) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

/** 保存 SaaS 登录态；退出、修改密码或删除账号时由认证 API 更新或清除。 */
export function setAccessToken(token: string) {
  accessToken = token;
  window.localStorage.setItem(ACCESS_TOKEN_KEY, token);
}

/** 清除浏览器持有的 JWT。 */
export function clearAccessToken() {
  accessToken = null;
  window.localStorage.removeItem(ACCESS_TOKEN_KEY);
}

/** 当前标签页是否持有登录凭据；路由可据此避免一次必然返回 401 的请求。 */
export function hasAccessToken() {
  return Boolean(accessToken);
}

/** SignalR 连接使用与普通 API 相同的当前 JWT。 */
export function getAccessToken() {
  return accessToken;
}

/** 使用可选 Bearer JWT 调用 ECHORA API。 */
export async function apiFetch(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers);
  if (typeof init.body === "string") headers.set("Content-Type", "application/json");
  if (accessToken) headers.set("Authorization", `Bearer ${accessToken}`);

  let response: Response;
  try {
    response = await fetch(`${API_ROOT}${path}`, { ...init, headers });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") throw error;
    throw new ApiError("无法连接服务器，请检查网络或服务器状态。", 0, "network_unavailable");
  }
  if (!response.ok) {
    if (response.status === 401) clearAccessToken();
    throw await toApiError(response);
  }
  return response;
}

/** 调用 API 并读取 JSON；204 响应由调用方使用 apiFetch。 */
export async function apiJson<T>(path: string, init: RequestInit = {}): Promise<T> {
  return (await apiFetch(path, init)).json() as Promise<T>;
}

function readSessionToken() {
  if (typeof window === "undefined") return null;
  return window.localStorage.getItem(ACCESS_TOKEN_KEY);
}

async function toApiError(response: Response) {
  const body = await response.json().catch(() => ({})) as ApiProblem;
  return new ApiError(body.title ?? `请求失败（${response.status}）`, response.status, body.code);
}
