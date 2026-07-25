import "@assistant-ui/react-markdown/styles/dot.css";
import { MarkdownTextPrimitive } from "@assistant-ui/react-markdown";
import remarkGfm from "remark-gfm";
import { Link } from "react-router-dom";

/** 使用 assistant-ui 官方渲染器呈现 AI Markdown，并支持常用 GFM 表格。 */
export default function MarkdownText() {
  return (
    <MarkdownTextPrimitive
      remarkPlugins={[remarkGfm]}
      smooth
      components={{
        a: ({ href, children }) => {
          if (href?.startsWith("/app/archive/moments/")) {
            return (
              <Link
                to={href}
                className="rounded bg-evidence/10 px-1 font-medium text-evidence no-underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-evidence/35"
              >
                {children}
              </Link>
            );
          }
          return (
            <a
              href={href}
              className="text-primary underline underline-offset-4"
            >
              {children}
            </a>
          );
        },
      }}
      className="min-w-0 space-y-3 break-words overflow-x-auto [&_blockquote]:border-l-2 [&_blockquote]:border-primary/30 [&_blockquote]:pl-3 [&_code]:rounded-md [&_code]:bg-muted [&_code]:px-1.5 [&_code]:py-0.5 [&_li]:ml-5 [&_ol]:list-decimal [&_pre]:overflow-x-auto [&_pre]:rounded-[10px] [&_pre]:bg-foreground [&_pre]:p-4 [&_pre]:text-background [&_pre_code]:bg-transparent [&_pre_code]:p-0 [&_pre_code]:text-inherit [&_table]:w-full [&_table]:min-w-[560px] [&_table]:border-collapse [&_td]:border [&_td]:border-border [&_td]:px-3 [&_td]:py-2 [&_th]:border [&_th]:border-border [&_th]:bg-muted [&_th]:px-3 [&_th]:py-2 [&_th]:text-left [&_ul]:list-disc"
    />
  );
}
