import type { ReactNode } from "react"
import { IconAlertTriangle, IconLoader2 } from "@tabler/icons-react"
import { AlertDialog } from "radix-ui"

import { Button } from "@/components/ui/button"

interface ConfirmDialogProps {
  open: boolean
  title: string
  description: ReactNode
  detail?: ReactNode
  confirmLabel?: string
  busyLabel?: string
  busy?: boolean
  confirmDisabled?: boolean
  onOpenChange: (open: boolean) => void
  onConfirm: () => void
}

/** 产品内统一的危险操作确认框；Radix 负责焦点圈定、Esc 与焦点归还。 */
export function ConfirmDialog({
  open,
  title,
  description,
  detail,
  confirmLabel = "确认删除",
  busyLabel = "正在删除…",
  busy = false,
  confirmDisabled = false,
  onOpenChange,
  onConfirm,
}: ConfirmDialogProps) {
  return (
    <AlertDialog.Root open={open} onOpenChange={(nextOpen) => {
      if (!busy) onOpenChange(nextOpen)
    }}>
      <AlertDialog.Portal>
        <AlertDialog.Overlay className="fixed inset-0 z-[90] bg-black/45 data-[state=closed]:animate-out data-[state=open]:animate-in data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0" />
        <AlertDialog.Content className="fixed left-1/2 top-1/2 z-[91] w-[min(420px,calc(100vw-28px))] -translate-x-1/2 -translate-y-1/2 rounded-[22px] border border-border bg-background p-5 shadow-[0_24px_70px_rgba(34,30,24,.24)] outline-none data-[state=closed]:animate-out data-[state=open]:animate-in data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 sm:p-6">
          <div className="grid size-11 place-items-center rounded-[14px] bg-destructive/10 text-destructive">
            <IconAlertTriangle className="size-5" aria-hidden />
          </div>
          <AlertDialog.Title className="mt-4 text-lg font-semibold tracking-[-0.025em] text-foreground">
            {title}
          </AlertDialog.Title>
          <AlertDialog.Description asChild>
            <div className="mt-2 text-sm leading-6 text-muted-foreground">
              {description}
            </div>
          </AlertDialog.Description>
          {detail}
          <div className="mt-6 grid grid-cols-2 gap-3">
            <AlertDialog.Cancel asChild>
              <Button type="button" variant="outline" className="h-11 rounded-xl" disabled={busy}>
                取消
              </Button>
            </AlertDialog.Cancel>
            <AlertDialog.Action asChild>
              <Button
                type="button"
                variant="destructive"
                className="h-11 rounded-xl"
                disabled={busy || confirmDisabled}
                onClick={(event) => {
                  event.preventDefault()
                  onConfirm()
                }}
              >
                {busy ? <><IconLoader2 className="animate-spin" aria-hidden />{busyLabel}</> : confirmLabel}
              </Button>
            </AlertDialog.Action>
          </div>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  )
}
