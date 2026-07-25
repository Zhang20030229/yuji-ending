import { useState, useEffect, useRef } from "react";
import { IconX, IconArrowRight } from "@tabler/icons-react";
import { cn } from "@/lib/utils";
import SpriteIcon from "./SpriteIcon";

interface CompanionSpriteProps {
  /** 是否激活/展开状态 */
  expanded: boolean;
  /** 关闭时的回调 */
  onClose: () => void;
  /** 发送消息的回调 */
  onSendMessage: (message: string) => void;
}

export default function CompanionSprite({ expanded, onClose, onSendMessage }: CompanionSpriteProps) {
  const [inputValue, setInputValue] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  // TODO: 集成实际的聊天功能
  // - 调用后端 API 发送消息
  // - 显示 AI 的回复
  // - 管理对话历史

  useEffect(() => {
    if (expanded && inputRef.current) {
      // 延迟聚焦确保动画完成
      setTimeout(() => {
        inputRef.current?.focus();
      }, 300);
    }
  }, [expanded]);

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "Enter" && inputValue.trim()) {
      onSendMessage(inputValue.trim());
      setInputValue("");
    }
  };

  if (!expanded) {
    return (
      <button
        type="button"
        className={cn(
          "companion-sprite-button",
          "fixed z-50 lg:z-20",
          "w-14 h-14 rounded-full",
          "border-2 border-white shadow-lg",
          "bg-primary text-primary-foreground",
          "flex items-center justify-center",
          "cursor-pointer",
          "transition-all duration-300 hover:scale-105 hover:shadow-xl",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40",
          // 桌面端：侧边栏右侧、底部上方
          "lg:right-[calc((100vw-1024px)/2+232px-28px)] lg:bottom-32",
          // 移动端：屏幕右下角
          "right-4 bottom-24",
        )}
        aria-label="打开陪伴小精灵"
      >
        <SpriteIcon className="w-8 h-8 p-1.5" size={32} />
      </button>
    );
  }

  return (
    <div className={cn(
      "companion-sprite-expanded",
      "fixed inset-0 z-50 flex flex-col",
      "bg-background/95 backdrop-blur-sm",
    )}>
      {/* 头部区域 */}
      <div className="companion-sprite-header px-4 py-6 border-b border-background">
        <div className="max-w-3xl mx-auto flex items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="companion-avatar w-10 h-10 rounded-full bg-primary flex items-center justify-center animate-pulse">
              <SpriteIcon className="w-6 h-6 p-1" size={24} />
            </div>
            <div>
              <h2 className="text-lg font-semibold">小精灵</h2>
              <p className="text-xs text-muted-foreground">我在这里陪着你</p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="companion-close-btn w-10 h-10 rounded-full flex items-center justify-center hover:bg-secondary transition-colors"
            aria-label="关闭"
          >
            <IconX className="w-5 h-5" />
          </button>
        </div>
      </div>

          {/* 聊天区域 */}
          <div className="companion-chat-area flex-1 overflow-y-auto px-4 py-6">
            <div className="max-w-3xl mx-auto space-y-4">
              <div className="companion-welcome flex flex-col items-center justify-center py-8 text-center">
                <div className="companion-avatar-large w-20 h-20 rounded-full bg-primary mb-4 flex items-center justify-center animate-pulse">
                  <SpriteIcon className="w-12 h-12 p-2" size={48} />
                </div>
                <h3 className="text-xl font-semibold mb-2">你好呀！</h3>
                <p className="text-sm text-muted-foreground max-w-sm">
                  我是你的陪伴小精灵，随时都在这里听你说说话。可以告诉我你现在的心情，或者我们随便聊聊~
                </p>
              </div>
            </div>
          </div>

          {/* 输入区域 */}
          <div className="companion-sprite-input-container px-4 pb-6 pt-4 border-t border-background">
            <div className="max-w-3xl mx-auto">
              <div className="companion-input-wrapper relative flex items-center gap-2">
                <input
                  ref={inputRef}
                  type="text"
                  value={inputValue}
                  onChange={(e) => setInputValue(e.target.value)}
                  onKeyDown={handleKeyDown}
                  placeholder="想说什么呢..."
                  className="companion-input flex-1 min-h-[48px] px-4 pr-12 rounded-full border border-border focus:border-primary focus:ring-2 focus:ring-primary/20 outline-none transition-all bg-background"
                />
                <button
                  type="button"
                  onClick={() => {
                    if (inputValue.trim()) {
                      onSendMessage(inputValue.trim());
                      setInputValue("");
                    }
                  }}
                  disabled={!inputValue.trim()}
                  className="companion-send-btn w-12 h-12 rounded-full bg-primary text-primary-foreground flex items-center justify-center hover:bg-primary/90 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                  aria-label="发送"
                >
                  <IconArrowRight className="w-5 h-5" />
                </button>
              </div>
              <p className="mt-2 text-xs text-center text-muted-foreground">
                按下 Enter 键即可发送消息
              </p>
            </div>
          </div>
    </div>
  );
}
