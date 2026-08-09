"use client";

import { useState } from "react";
import Link from "next/link";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  Loader2,
  FileClock,
  ArrowLeft,
  Sparkles,
  CheckCircle2,
} from "lucide-react";
import { meetingApi, ApiRequestError } from "@/lib/api";
import type { MeetingDto, MeetingMinutes } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

function formatDate(dateStr?: string): string {
  if (!dateStr) return "-";
  const d = new Date(dateStr);
  return d.toLocaleString("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

const minutesStatusConfig: Record<
  string,
  { label: string; className: string }
> = {
  pending: {
    label: "生成中",
    className: "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  },
  completed: {
    label: "已完成",
    className: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300",
  },
  done: {
    label: "已完成",
    className: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-300",
  },
  failed: {
    label: "失败",
    className: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
  },
};

export default function MinutesPage() {
  const queryClient = useQueryClient();
  const [selectedMeetingId, setSelectedMeetingId] = useState<string | null>(
    null,
  );

  const { data: meetings, isLoading: meetingsLoading } = useQuery({
    queryKey: ["meetings"],
    queryFn: () => meetingApi.list({ limit: 100 }),
  });

  const { data: minutes, isLoading: minutesLoading } = useQuery({
    queryKey: ["meeting-minutes", selectedMeetingId],
    queryFn: () => meetingApi.getMinutes(selectedMeetingId!),
    enabled: !!selectedMeetingId,
  });

  const generateMutation = useMutation({
    mutationFn: (meetingId: string) => meetingApi.generateMinutes(meetingId),
    onSuccess: (data) => {
      toast.success("纪要生成已触发");
      queryClient.invalidateQueries({
        queryKey: ["meeting-minutes", data.meetingId],
      });
    },
    onError: (error) => {
      const message =
        error instanceof ApiRequestError ? error.message : "生成纪要失败";
      toast.error(message);
    },
  });

  const setOfficialMutation = useMutation({
    mutationFn: ({
      meetingId,
      minutesId,
    }: {
      meetingId: string;
      minutesId: string;
    }) => meetingApi.setOfficialMinutes(meetingId, minutesId),
    onSuccess: (data) => {
      toast.success("已设为正式纪要");
      queryClient.invalidateQueries({
        queryKey: ["meeting-minutes", data.meetingId],
      });
    },
    onError: (error) => {
      const message =
        error instanceof ApiRequestError ? error.message : "设置失败";
      toast.error(message);
    },
  });

  const selectedMeeting = meetings?.find(
    (m: MeetingDto) => m.id === selectedMeetingId,
  );

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div>
        <div className="flex items-center gap-2">
          <Button variant="ghost" size="sm" render={<Link href="/capture" />}>
            <ArrowLeft className="mr-1.5 size-4" />
            采集中心
          </Button>
        </div>
        <h1 className="mt-2 text-2xl font-bold">会议纪要</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          为会议生成 AI 纪要，查看摘要、关键决策和待办事项
        </p>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        {/* 会议列表 */}
        <Card>
          <CardHeader>
            <CardTitle>会议列表</CardTitle>
            <CardDescription>选择一场会议查看或生成纪要</CardDescription>
          </CardHeader>
          <CardContent>
            {meetingsLoading ? (
              <div className="flex items-center justify-center py-8">
                <Loader2 className="size-6 animate-spin text-muted-foreground" />
              </div>
            ) : !meetings || meetings.length === 0 ? (
              <div className="flex flex-col items-center justify-center py-12 text-center">
                <FileClock className="mb-3 size-10 text-muted-foreground/50" />
                <p className="text-sm text-muted-foreground">
                  暂无会议，请先创建会议
                </p>
                <Button
                  className="mt-3"
                  size="sm"
                  render={<Link href="/capture/meetings" />}
                >
                  去创建会议
                </Button>
              </div>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>会议标题</TableHead>
                    <TableHead>创建时间</TableHead>
                    <TableHead className="text-right">操作</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {meetings.map((meeting: MeetingDto) => (
                    <TableRow
                      key={meeting.id}
                      className={
                        selectedMeetingId === meeting.id
                          ? "bg-muted/50"
                          : "cursor-pointer"
                      }
                      onClick={() => setSelectedMeetingId(meeting.id)}
                    >
                      <TableCell className="font-medium">
                        {meeting.title}
                      </TableCell>
                      <TableCell className="text-sm text-muted-foreground">
                        {formatDate(meeting.createdAt)}
                      </TableCell>
                      <TableCell className="text-right">
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={
                            generateMutation.isPending &&
                            generateMutation.variables === meeting.id
                          }
                          onClick={(e) => {
                            e.stopPropagation();
                            generateMutation.mutate(meeting.id);
                          }}
                        >
                          {generateMutation.isPending &&
                          generateMutation.variables === meeting.id ? (
                            <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                          ) : (
                            <Sparkles className="mr-1.5 size-3.5" />
                          )}
                          生成纪要
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </CardContent>
        </Card>

        {/* 纪要详情 */}
        <Card>
          <CardHeader>
            <CardTitle>纪要内容</CardTitle>
            <CardDescription>
              {selectedMeeting
                ? `会议：${selectedMeeting.title}`
                : "请从左侧选择一场会议"}
            </CardDescription>
          </CardHeader>
          <CardContent>
            {!selectedMeetingId ? (
              <div className="flex flex-col items-center justify-center py-12 text-center">
                <FileClock className="mb-3 size-10 text-muted-foreground/50" />
                <p className="text-sm text-muted-foreground">
                  选择会议后可查看已生成的纪要
                </p>
              </div>
            ) : minutesLoading ? (
              <div className="flex items-center justify-center py-8">
                <Loader2 className="size-6 animate-spin text-muted-foreground" />
              </div>
            ) : !minutes || minutes.length === 0 ? (
              <div className="flex flex-col items-center justify-center py-12 text-center">
                <p className="text-sm text-muted-foreground">
                  该会议暂无纪要
                </p>
                <Button
                  className="mt-3"
                  size="sm"
                  disabled={generateMutation.isPending}
                  onClick={() => generateMutation.mutate(selectedMeetingId)}
                >
                  {generateMutation.isPending ? (
                    <Loader2 className="mr-1.5 size-3.5 animate-spin" />
                  ) : (
                    <Sparkles className="mr-1.5 size-3.5" />
                  )}
                  生成纪要
                </Button>
              </div>
            ) : (
              <div className="space-y-4">
                {minutes.map((item: MeetingMinutes) => {
                  const statusConfig =
                    minutesStatusConfig[item.status] ?? {
                      label: item.status,
                      className: "bg-muted text-muted-foreground",
                    };
                  return (
                    <div
                      key={item.id}
                      className="rounded-lg border p-4 space-y-3"
                    >
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                          <Badge
                            variant="outline"
                            className={statusConfig.className}
                          >
                            {statusConfig.label}
                          </Badge>
                          {item.isOfficial && (
                            <Badge
                              variant="outline"
                              className="bg-indigo-100 text-indigo-700 dark:bg-indigo-900/40 dark:text-indigo-300"
                            >
                              <CheckCircle2 className="mr-1 size-3" />
                              正式
                            </Badge>
                          )}
                        </div>
                        <span className="text-xs text-muted-foreground">
                          {formatDate(item.createdAt)}
                        </span>
                      </div>

                      {item.summary && (
                        <div>
                          <p className="text-xs font-medium text-muted-foreground">
                            摘要
                          </p>
                          <p className="mt-1 text-sm">{item.summary}</p>
                        </div>
                      )}

                      {item.keyDecisions && (
                        <div>
                          <p className="text-xs font-medium text-muted-foreground">
                            关键决策
                          </p>
                          <p className="mt-1 whitespace-pre-wrap text-sm">
                            {item.keyDecisions}
                          </p>
                        </div>
                      )}

                      {item.actionItems && (
                        <div>
                          <p className="text-xs font-medium text-muted-foreground">
                            待办事项
                          </p>
                          <p className="mt-1 whitespace-pre-wrap text-sm">
                            {item.actionItems}
                          </p>
                        </div>
                      )}

                      {item.contentMarkdown && (
                        <div>
                          <p className="text-xs font-medium text-muted-foreground">
                            完整纪要
                          </p>
                          <div className="mt-1 max-h-64 overflow-y-auto rounded border bg-muted/30 p-3 text-sm">
                            <pre className="whitespace-pre-wrap break-words font-sans">
                              {item.contentMarkdown}
                            </pre>
                          </div>
                        </div>
                      )}

                      {!item.isOfficial && item.status !== "pending" && (
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={setOfficialMutation.isPending}
                          onClick={() =>
                            setOfficialMutation.mutate({
                              meetingId: item.meetingId,
                              minutesId: item.id,
                            })
                          }
                        >
                          <CheckCircle2 className="mr-1.5 size-3.5" />
                          设为正式纪要
                        </Button>
                      )}
                    </div>
                  );
                })}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
