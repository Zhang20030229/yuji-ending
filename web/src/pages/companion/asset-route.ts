/**
 * 从 ECHORA 自己的受保护素材地址中读取 bigint 素材主键。
 * 外部地址、相似前缀和非十进制标识不会进入发送或删除请求。
 */
export function readAssetIdFromContentUrl(url: string) {
  return url.match(/^\/api\/assets\/(\d+)\/content$/)?.[1];
}
