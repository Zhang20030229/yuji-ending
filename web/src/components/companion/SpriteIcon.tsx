import { cn } from "@/lib/utils";

interface SpriteIconProps extends React.SVGProps<SVGSVGElement> {
  /** 大小 */
  size?: number;
}

/** 
 * 陪伴小精灵图标组件
 * 使用 SVG 直接绘制，不依赖外部图片资源
 */
export default function SpriteIcon({ 
  className, 
  size = 21,
  ...props 
}: SpriteIconProps) {
  return (
    <svg
      viewBox="0 0 64 64"
      className={cn("companion-sprite-icon", className)}
      width={size}
      height={size}
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      {...props}
    >
      <defs>
        <linearGradient id="sprite-gradient" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="var(--color-primary)" />
          <stop offset="100%" stopColor="var(--color-primary-soft)" />
        </linearGradient>
      </defs>
      
      {/* 精灵主体 - 圆形背景 */}
      <circle cx="32" cy="32" r="28" fill="url(#sprite-gradient)" />
      
      {/* 精灵脸部轮廓 */}
      <path 
        d="M20 28c0-6 4-10 12-10s12 4 12 10c0 6-4 12-12 12S20 34 20 28z" 
        fill="white" 
        opacity="0.95"
      />
      
      {/* 眼睛 */}
      <circle cx="26" cy="28" r="2.5" fill="#374151" />
      <circle cx="38" cy="28" r="2.5" fill="#374151" />
      
      {/* 微笑嘴巴 */}
      <path 
        d="M28 35q4 3 8 0" 
        stroke="#374151" 
        strokeWidth="1.5" 
        strokeLinecap="round"
      />
      
      {/* 翅膀装饰 */}
      <path 
        d="M16 24l-4-6 4-6" 
        stroke="white" 
        strokeWidth="2" 
        strokeLinecap="round" 
        opacity="0.7"
      />
      <path 
        d="M48 24l4-6-4-6" 
        stroke="white" 
        strokeWidth="2" 
        strokeLinecap="round" 
        opacity="0.7"
      />
    </svg>
  );
}
