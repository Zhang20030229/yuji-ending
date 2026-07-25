import { useState } from "react";
import CompanionSprite from "@/components/companion/CompanionSprite";
import AppLayout from "./AppLayout";

interface SpriteWrapperProps {
  children: React.ReactNode;
}

/** 
 * 陪伴小精灵包装器
 * 管理小精灵的展开/收起状态，并将状态传递给子组件和按钮
 */
export default function CompanionSpriteWrapper({ children }: SpriteWrapperProps) {
  const [isExpanded, setIsExpanded] = useState(false);

  const handleOpenSprite = () => {
    setIsExpanded(true);
  };

  const handleCloseSprite = () => {
    setIsExpanded(false);
  };

  const handleMessageSent = (message: string) => {
    console.log("对小精灵说:", message);
    // TODO: 这里可以集成到实际的聊天系统中
    // 例如调用 API 或者更新聊天历史
  };

  return (
    <>
      <AppLayout companionButtonPosition="after-nav" onOpenSprite={handleOpenSprite} />
      <CompanionSprite 
        expanded={isExpanded} 
        onClose={handleCloseSprite} 
        onSendMessage={handleMessageSent}
      />
      {children}
    </>
  );
}
