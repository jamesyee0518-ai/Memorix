import { cn } from "@/lib/utils";

type MemorixBrandProps = {
  className?: string;
  inverted?: boolean;
  markOnly?: boolean;
  showTagline?: boolean;
};

export function MemorixBrand({
  className,
  inverted = false,
  markOnly = false,
  showTagline = true,
}: MemorixBrandProps) {
  const primary = inverted ? "#FFFFFF" : "#0D47A1";

  return (
    <div className={cn("inline-flex min-w-0 items-center gap-2.5", className)}>
      <svg
        viewBox="0 0 128 128"
        className="size-9 shrink-0"
        role="img"
        aria-label="Memorix"
      >
        <path
          d="M79 15H29c-8 0-14 6-14 14v70c0 8 6 14 14 14h70c8 0 14-6 14-14V78"
          fill="none"
          stroke={primary}
          strokeWidth="10"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
        <path d="M87 16v25h25z" fill="#00B8C8" />
        <circle cx="63" cy="67" r="17" fill={primary} />
        <circle
          cx="63"
          cy="67"
          r="32"
          fill="none"
          stroke={primary}
          strokeWidth="9"
          strokeLinecap="round"
          strokeDasharray="38 13"
          transform="rotate(-7 63 67)"
        />
      </svg>
      {!markOnly && (
        <span className="min-w-0 leading-none">
          <span
            className={cn(
              "block truncate text-xl font-bold",
              inverted ? "text-white" : "text-[#0D47A1]"
            )}
          >
            Memorix
          </span>
          {showTagline && (
            <span
              className={cn(
                "mt-1 block truncate text-[10px] font-medium",
                inverted ? "text-white/70" : "text-[#1F2937]/70"
              )}
            >
              个人的 AI 记忆体
            </span>
          )}
        </span>
      )}
    </div>
  );
}
