"use client"

import * as React from "react"

import { cn } from "@/lib/utils"

/**
 * ScrollArea - a lightweight scroll container that renders a viewport with
 * native scrolling. It mirrors the shadcn ScrollArea API (ScrollArea,
 * ScrollBar) while staying dependency-free so it works with base-ui projects.
 */
function ScrollArea({
  className,
  viewportClassName,
  children,
  ...props
}: React.ComponentProps<"div"> & {
  viewportClassName?: string
}) {
  return (
    <div
      data-slot="scroll-area"
      className={cn("relative overflow-hidden", className)}
      {...props}
    >
      <div
        data-slot="scroll-area-viewport"
        className={cn(
          "h-full w-full overflow-y-auto overflow-x-hidden [scrollbar-gutter:stable]",
          viewportClassName
        )}
      >
        {children}
      </div>
    </div>
  )
}

function ScrollBar({
  className,
  orientation = "vertical",
  ...props
}: React.ComponentProps<"div"> & { orientation?: "vertical" | "horizontal" }) {
  return (
    <div
      data-slot="scroll-bar"
      data-orientation={orientation}
      className={cn(
        "pointer-events-none absolute flex touch-none select-none",
        orientation === "vertical" && "right-0.5 top-0.5 h-[calc(100%-1rem)] w-2.5",
        orientation === "horizontal" && "bottom-0.5 left-0.5 h-2.5 w-[calc(100%-1rem)]",
        className
      )}
      {...props}
    />
  )
}

export { ScrollArea, ScrollBar }
