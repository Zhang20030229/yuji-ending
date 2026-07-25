import * as React from "react"
import {
  IconCalendar,
  IconChevronDown,
  IconChevronLeft,
  IconChevronRight,
  IconChevronsLeft,
  IconChevronsRight,
} from "@tabler/icons-react"
import { Popover } from "radix-ui"

import { cn } from "@/lib/utils"

type PickerProps = {
  value: string
  onChange: (value: string) => void
  min?: string
  max?: string
  id?: string
  className?: string
  placeholder?: string
  "aria-label"?: string
}

type YearPickerProps = {
  value: number
  onChange: (value: number) => void
  min?: number
  max?: number
  className?: string
  "aria-label"?: string
}

const weekdays = ["一", "二", "三", "四", "五", "六", "日"]
const dateFormatter = new Intl.DateTimeFormat("zh-CN", {
  year: "numeric",
  month: "long",
  day: "numeric",
  weekday: "short",
})
const monthFormatter = new Intl.DateTimeFormat("zh-CN", {
  year: "numeric",
  month: "long",
})

function DatePicker({
  value,
  onChange,
  min,
  max,
  id,
  className,
  placeholder = "选择日期",
  "aria-label": ariaLabel = "选择日期",
}: PickerProps) {
  const initial = parseDate(value || max) ?? new Date()
  const [open, setOpen] = React.useState(false)
  const [visibleMonth, setVisibleMonth] = React.useState(
    new Date(initial.getFullYear(), initial.getMonth(), 1),
  )
  const [selectingYear, setSelectingYear] = React.useState(false)
  const days = calendarDays(visibleMonth)
  const today = toDateValue(new Date())
  const canUseToday = isWithin(today, min, max)
  const previousMonth = addMonths(visibleMonth, -1)
  const nextMonth = addMonths(visibleMonth, 1)
  const yearPageStart = Math.floor(visibleMonth.getFullYear() / 12) * 12
  const yearPageEnd = yearPageStart + 11

  function handleOpen(nextOpen: boolean) {
    if (nextOpen) {
      const selected = parseDate(value || max)
      if (selected) setVisibleMonth(new Date(selected.getFullYear(), selected.getMonth(), 1))
      setSelectingYear(false)
    }
    setOpen(nextOpen)
  }

  function choose(nextValue: string) {
    if (!isWithin(nextValue, min, max)) return
    onChange(nextValue)
    setOpen(false)
  }

  return (
    <Popover.Root open={open} onOpenChange={handleOpen}>
      <Popover.Trigger asChild>
        <button
          id={id}
          type="button"
          aria-label={ariaLabel}
          aria-haspopup="dialog"
          aria-expanded={open}
          className={cn(
            "flex h-11 w-full items-center gap-2.5 rounded-xl border border-input bg-background px-3.5 text-left text-sm text-foreground outline-none transition-[border-color,box-shadow,background-color] active:scale-[.99] focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/25",
            !value && "text-muted-foreground",
            className,
          )}
        >
          <IconCalendar className="size-[18px] shrink-0 text-primary" aria-hidden />
          <span className="min-w-0 flex-1 truncate">
            {value ? dateFormatter.format(parseDate(value) ?? new Date()) : placeholder}
          </span>
          <IconChevronRight className={cn("size-4 shrink-0 text-muted-foreground transition-transform", open && "rotate-90")} aria-hidden />
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          sideOffset={8}
          collisionPadding={12}
          align="end"
          className="z-[120] w-[min(320px,calc(100vw-24px))] rounded-2xl border border-border bg-popover p-3 text-popover-foreground shadow-[0_16px_44px_rgba(67,58,43,.16)] outline-none data-[state=closed]:animate-out data-[state=open]:animate-in data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95"
        >
          {selectingYear ? (
            <>
              <div className="mb-3 flex h-10 items-center justify-between">
                <button
                  type="button"
                  aria-label="向前十二年"
                  disabled={!yearRangeOverlaps(yearPageStart - 12, yearPageStart - 1, min, max)}
                  onClick={() => setVisibleMonth(new Date(yearPageStart - 12, visibleMonth.getMonth(), 1))}
                  className="grid size-10 place-items-center rounded-xl text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground active:scale-95 disabled:opacity-30"
                >
                  <IconChevronsLeft className="size-[18px]" />
                </button>
                <strong className="text-sm font-semibold tracking-[-0.02em]">
                  {yearPageStart}–{yearPageEnd} 年
                </strong>
                <button
                  type="button"
                  aria-label="向后十二年"
                  disabled={!yearRangeOverlaps(yearPageStart + 12, yearPageEnd + 12, min, max)}
                  onClick={() => setVisibleMonth(new Date(yearPageStart + 12, visibleMonth.getMonth(), 1))}
                  className="grid size-10 place-items-center rounded-xl text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground active:scale-95 disabled:opacity-30"
                >
                  <IconChevronsRight className="size-[18px]" />
                </button>
              </div>
              <div className="grid grid-cols-3 gap-2" role="grid" aria-label="选择年份">
                {Array.from({ length: 12 }, (_, index) => yearPageStart + index).map((year) => {
                  const selected = year === visibleMonth.getFullYear()
                  return (
                    <button
                      key={year}
                      type="button"
                      role="gridcell"
                      aria-selected={selected}
                      disabled={!yearOverlaps(year, min, max)}
                      style={selected ? { color: "var(--primary-foreground)" } : undefined}
                      onClick={() => {
                        setVisibleMonth(new Date(year, visibleMonth.getMonth(), 1))
                        setSelectingYear(false)
                      }}
                      className={cn(
                        "h-11 rounded-xl text-sm transition-[transform,background-color,color] hover:bg-primary-soft hover:text-primary active:scale-95 disabled:pointer-events-none disabled:opacity-25",
                        selected && "bg-primary font-semibold text-primary-foreground hover:bg-primary hover:text-primary-foreground",
                      )}
                    >
                      {year} 年
                    </button>
                  )
                })}
              </div>
            </>
          ) : (
            <>
              <div className="mb-2 flex h-10 items-center justify-between">
                <button
                  type="button"
                  aria-label="上个月"
                  disabled={!monthOverlaps(previousMonth, min, max)}
                  onClick={() => setVisibleMonth(previousMonth)}
                  className="grid size-10 place-items-center rounded-xl text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground active:scale-95 disabled:opacity-30"
                >
                  <IconChevronLeft className="size-[18px]" />
                </button>
                <button
                  type="button"
                  aria-label={`选择年份，当前 ${visibleMonth.getFullYear()} 年`}
                  aria-expanded={selectingYear}
                  onClick={() => setSelectingYear(true)}
                  className="flex h-10 items-center gap-1 rounded-xl px-3 text-sm font-semibold tracking-[-0.02em] transition-colors hover:bg-secondary active:scale-95"
                >
                  {monthFormatter.format(visibleMonth)}
                  <IconChevronDown className="size-4 text-muted-foreground" aria-hidden />
                </button>
                <button
                  type="button"
                  aria-label="下个月"
                  disabled={!monthOverlaps(nextMonth, min, max)}
                  onClick={() => setVisibleMonth(nextMonth)}
                  className="grid size-10 place-items-center rounded-xl text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground active:scale-95 disabled:opacity-30"
                >
                  <IconChevronRight className="size-[18px]" />
                </button>
              </div>
              <div className="grid grid-cols-7" aria-hidden>
                {weekdays.map((day) => (
                  <span key={day} className="grid h-8 place-items-center text-[11px] font-medium text-muted-foreground">
                    {day}
                  </span>
                ))}
              </div>
              <div className="grid grid-cols-7 gap-y-0.5" role="grid" aria-label={monthFormatter.format(visibleMonth)}>
                {days.map((day) => {
                  const dayValue = toDateValue(day)
                  const selected = dayValue === value
                  const currentMonth = day.getMonth() === visibleMonth.getMonth()
                  const disabled = !isWithin(dayValue, min, max)
                  return (
                    <button
                      key={dayValue}
                      type="button"
                      role="gridcell"
                      aria-selected={selected}
                      aria-current={dayValue === today ? "date" : undefined}
                      disabled={disabled}
                      style={selected ? { color: "var(--primary-foreground)" } : undefined}
                      onClick={() => choose(dayValue)}
                      className={cn(
                        "mx-auto grid size-10 place-items-center rounded-xl text-sm transition-[transform,background-color,color] hover:bg-primary-soft hover:text-primary active:scale-90 disabled:pointer-events-none disabled:opacity-25",
                        !currentMonth && "text-muted-foreground/55",
                        dayValue === today && !selected && "font-semibold text-primary",
                        selected && "bg-primary font-semibold text-primary-foreground hover:bg-primary hover:text-primary-foreground",
                      )}
                    >
                      {day.getDate()}
                    </button>
                  )
                })}
              </div>
            </>
          )}
          {!selectingYear && canUseToday && (
            <div className="mt-2 border-t border-border pt-2">
              <button
                type="button"
                onClick={() => choose(today)}
                className="h-9 w-full rounded-xl text-xs font-medium text-primary transition-colors hover:bg-primary-soft active:scale-[.98]"
              >
                回到今天
              </button>
            </div>
          )}
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}

function MonthPicker({
  value,
  onChange,
  min,
  max,
  id,
  className,
  placeholder = "选择年月",
  "aria-label": ariaLabel = "选择月份",
}: PickerProps) {
  const fallbackYear = Number((value || max)?.slice(0, 4)) || new Date().getFullYear()
  const minYear = Number(min?.slice(0, 4)) || fallbackYear - 120
  const maxYear = Number(max?.slice(0, 4)) || fallbackYear + 120
  const [open, setOpen] = React.useState(false)
  const [year, setYear] = React.useState(fallbackYear)
  const [selectingYear, setSelectingYear] = React.useState(false)

  function handleOpen(nextOpen: boolean) {
    if (nextOpen) {
      setYear(Number((value || max)?.slice(0, 4)) || fallbackYear)
      setSelectingYear(false)
    }
    setOpen(nextOpen)
  }

  function moveYear(amount: number) {
    setYear((current) => Math.min(maxYear, Math.max(minYear, current + amount)))
  }

  return (
    <Popover.Root open={open} onOpenChange={handleOpen}>
      <Popover.Trigger asChild>
        <button
          id={id}
          type="button"
          aria-label={ariaLabel}
          aria-haspopup="dialog"
          aria-expanded={open}
          className={cn(
            "flex h-11 w-full items-center gap-2.5 rounded-xl border border-input bg-background px-3.5 text-left text-sm text-foreground outline-none transition-[border-color,box-shadow,background-color] active:scale-[.99] focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/25",
            !value && "text-muted-foreground",
            className,
          )}
        >
          <IconCalendar className="size-[18px] shrink-0 text-primary" aria-hidden />
          <span className="min-w-0 flex-1 truncate">
            {value ? monthFormatter.format(parseMonth(value) ?? new Date()) : placeholder}
          </span>
          <IconChevronRight className={cn("size-4 shrink-0 text-muted-foreground transition-transform", open && "rotate-90")} aria-hidden />
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          sideOffset={8}
          collisionPadding={12}
          align="end"
          className="z-[120] w-[min(320px,calc(100vw-24px))] rounded-2xl border border-border bg-popover p-3 text-popover-foreground shadow-[0_16px_44px_rgba(67,58,43,.16)] outline-none data-[state=closed]:animate-out data-[state=open]:animate-in data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95"
        >
          {selectingYear ? (
            <YearGrid
              value={year}
              min={minYear}
              max={maxYear}
              onChange={(nextYear) => {
                setYear(nextYear)
                setSelectingYear(false)
              }}
              onPageChange={setYear}
            />
          ) : (
            <>
              <div className="mb-3 grid grid-cols-[40px_40px_1fr_40px_40px] items-center">
                <button type="button" aria-label="向前十年" disabled={year <= minYear} onClick={() => moveYear(-10)} className="grid size-10 place-items-center rounded-xl text-muted-foreground hover:bg-secondary active:scale-95 disabled:opacity-30"><IconChevronsLeft className="size-[18px]" /></button>
                <button type="button" aria-label="上一年" disabled={year <= minYear} onClick={() => moveYear(-1)} className="grid size-10 place-items-center rounded-xl text-muted-foreground hover:bg-secondary active:scale-95 disabled:opacity-30"><IconChevronLeft className="size-[18px]" /></button>
                <button type="button" aria-label={`选择年份，当前 ${year} 年`} onClick={() => setSelectingYear(true)} className="flex h-10 items-center justify-center gap-1 rounded-xl text-sm font-semibold hover:bg-secondary active:scale-95">
                  {year} 年
                  <IconChevronDown className="size-4 text-muted-foreground" aria-hidden />
                </button>
                <button type="button" aria-label="下一年" disabled={year >= maxYear} onClick={() => moveYear(1)} className="grid size-10 place-items-center rounded-xl text-muted-foreground hover:bg-secondary active:scale-95 disabled:opacity-30"><IconChevronRight className="size-[18px]" /></button>
                <button type="button" aria-label="向后十年" disabled={year >= maxYear} onClick={() => moveYear(10)} className="grid size-10 place-items-center rounded-xl text-muted-foreground hover:bg-secondary active:scale-95 disabled:opacity-30"><IconChevronsRight className="size-[18px]" /></button>
              </div>
              <div className="grid grid-cols-3 gap-2" role="grid" aria-label={`${year} 年`}>
                {Array.from({ length: 12 }, (_, index) => {
                  const monthValue = `${year}-${String(index + 1).padStart(2, "0")}`
                  const selected = monthValue === value
                  const disabled = !isWithin(monthValue, min, max)
                  return (
                    <button
                      key={monthValue}
                      type="button"
                      role="gridcell"
                      aria-selected={selected}
                      disabled={disabled}
                      style={selected ? { color: "var(--primary-foreground)" } : undefined}
                      onClick={() => {
                        onChange(monthValue)
                        setOpen(false)
                      }}
                      className={cn(
                        "h-11 rounded-xl text-sm transition-[transform,background-color,color] hover:bg-primary-soft hover:text-primary active:scale-95 disabled:pointer-events-none disabled:opacity-25",
                        selected && "bg-primary font-semibold text-primary-foreground hover:bg-primary hover:text-primary-foreground",
                      )}
                    >
                      {index + 1} 月
                    </button>
                  )
                })}
              </div>
            </>
          )}
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}

function YearPicker({
  value,
  onChange,
  min = value - 120,
  max = new Date().getFullYear(),
  className,
  "aria-label": ariaLabel = "选择年份",
}: YearPickerProps) {
  const [open, setOpen] = React.useState(false)
  const [visibleYear, setVisibleYear] = React.useState(value)

  return (
    <Popover.Root
      open={open}
      onOpenChange={(nextOpen) => {
        if (nextOpen) setVisibleYear(value)
        setOpen(nextOpen)
      }}
    >
      <Popover.Trigger asChild>
        <button
          type="button"
          aria-label={ariaLabel}
          aria-haspopup="dialog"
          aria-expanded={open}
          className={cn(
            "flex h-11 w-full items-center gap-2.5 rounded-xl border border-input bg-background px-3.5 text-left text-sm text-foreground outline-none transition-[border-color,box-shadow,background-color] active:scale-[.99] focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/25",
            className,
          )}
        >
          <IconCalendar className="size-[18px] shrink-0 text-primary" aria-hidden />
          <span className="min-w-0 flex-1 truncate">{value} 年</span>
          <IconChevronRight className={cn("size-4 shrink-0 text-muted-foreground transition-transform", open && "rotate-90")} aria-hidden />
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          sideOffset={8}
          collisionPadding={12}
          align="end"
          className="z-[120] w-[min(320px,calc(100vw-24px))] rounded-2xl border border-border bg-popover p-3 text-popover-foreground shadow-[0_16px_44px_rgba(67,58,43,.16)] outline-none"
        >
          <YearGrid
            value={visibleYear}
            selected={value}
            min={min}
            max={max}
            onChange={(nextYear) => {
              onChange(nextYear)
              setOpen(false)
            }}
            onPageChange={setVisibleYear}
          />
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}

function YearGrid({
  value,
  selected = value,
  min,
  max,
  onChange,
  onPageChange,
}: {
  value: number
  selected?: number
  min: number
  max: number
  onChange: (value: number) => void
  onPageChange?: (value: number) => void
}) {
  const pageStart = Math.floor(value / 12) * 12
  const movePage = (amount: number) => onPageChange?.(Math.min(max, Math.max(min, value + amount)))

  return (
    <>
      <div className="mb-3 flex h-10 items-center justify-between">
        <button type="button" aria-label="向前十二年" disabled={pageStart <= min} onClick={() => movePage(-12)} className="grid size-10 place-items-center rounded-xl text-muted-foreground hover:bg-secondary active:scale-95 disabled:opacity-30"><IconChevronsLeft className="size-[18px]" /></button>
        <strong className="text-sm font-semibold">{pageStart}–{pageStart + 11} 年</strong>
        <button type="button" aria-label="向后十二年" disabled={pageStart + 11 >= max} onClick={() => movePage(12)} className="grid size-10 place-items-center rounded-xl text-muted-foreground hover:bg-secondary active:scale-95 disabled:opacity-30"><IconChevronsRight className="size-[18px]" /></button>
      </div>
      <div className="grid grid-cols-3 gap-2" role="grid" aria-label="选择年份">
        {Array.from({ length: 12 }, (_, index) => pageStart + index).map((year) => (
          <button
            key={year}
            type="button"
            role="gridcell"
            aria-selected={year === selected}
            disabled={year < min || year > max}
            style={year === selected ? { color: "var(--primary-foreground)" } : undefined}
            onClick={() => onChange(year)}
            className={cn(
              "h-11 rounded-xl text-sm transition-[transform,background-color,color] hover:bg-primary-soft hover:text-primary active:scale-95 disabled:pointer-events-none disabled:opacity-25",
              year === selected && "bg-primary font-semibold text-primary-foreground hover:bg-primary hover:text-primary-foreground",
            )}
          >
            {year} 年
          </button>
        ))}
      </div>
    </>
  )
}

function parseDate(value?: string) {
  if (!value) return undefined
  const [year, month, day] = value.split("-").map(Number)
  if (!year || !month || !day) return undefined
  return new Date(year, month - 1, day)
}

function parseMonth(value?: string) {
  if (!value) return undefined
  const [year, month] = value.split("-").map(Number)
  if (!year || !month) return undefined
  return new Date(year, month - 1, 1)
}

function toDateValue(date: Date) {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`
}

function addMonths(date: Date, amount: number) {
  return new Date(date.getFullYear(), date.getMonth() + amount, 1)
}

function calendarDays(month: Date) {
  const first = new Date(month.getFullYear(), month.getMonth(), 1)
  const leading = (first.getDay() + 6) % 7
  return Array.from({ length: 42 }, (_, index) =>
    new Date(month.getFullYear(), month.getMonth(), index - leading + 1),
  )
}

function isWithin(value: string, min?: string, max?: string) {
  return (!min || value >= min) && (!max || value <= max)
}

function monthOverlaps(month: Date, min?: string, max?: string) {
  const first = `${month.getFullYear()}-${String(month.getMonth() + 1).padStart(2, "0")}-01`
  const last = toDateValue(new Date(month.getFullYear(), month.getMonth() + 1, 0))
  return (!min || last >= min) && (!max || first <= max)
}

function yearOverlaps(year: number, min?: string, max?: string) {
  return (!min || `${year}-12-31` >= min) && (!max || `${year}-01-01` <= max)
}

function yearRangeOverlaps(firstYear: number, lastYear: number, min?: string, max?: string) {
  return (!min || `${lastYear}-12-31` >= min) && (!max || `${firstYear}-01-01` <= max)
}

export { DatePicker, MonthPicker, YearPicker }
