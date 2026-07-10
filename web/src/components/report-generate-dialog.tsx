"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import {
  Loader2,
  CalendarDays,
  CalendarRange,
  FileSearch,
} from "lucide-react";
import { useTopicStore } from "@/stores/topic-store";
import { reportApi, tagApi, entityApi, ApiRequestError } from "@/lib/api";
import type { Tag, EntityListItem } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

interface ReportGenerateDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  defaultTab?: string;
  defaultTopicId?: string;
}

// 获取今天的日期 YYYY-MM-DD
function getToday(): string {
  const d = new Date();
  const year = d.getFullYear();
  const month = String(d.getMonth() + 1).padStart(2, "0");
  const day = String(d.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export function ReportGenerateDialog({
  open,
  onOpenChange,
  defaultTab = "daily",
  defaultTopicId,
}: ReportGenerateDialogProps) {
  const router = useRouter();
  const { topics, fetchTopics } = useTopicStore();
  const [activeTab, setActiveTab] = useState<string>(defaultTab);
  const [submitting, setSubmitting] = useState(false);

  // 日报表单
  const [dailyTopicId, setDailyTopicId] = useState<string>("");
  const [dailyDate, setDailyDate] = useState<string>(getToday());

  // 周报表单
  const [weeklyTopicId, setWeeklyTopicId] = useState<string>("");
  const [weeklyStartDate, setWeeklyStartDate] = useState<string>("");
  const [weeklyEndDate, setWeeklyEndDate] = useState<string>("");

  // 专题报告表单
  const [topicReportTopicId, setTopicReportTopicId] = useState<string>("");
  const [topicReportTitle, setTopicReportTitle] = useState<string>("");
  const [topicReportQuestion, setTopicReportQuestion] = useState<string>("");
  const [topicReportDateFrom, setTopicReportDateFrom] = useState<string>("");
  const [topicReportDateTo, setTopicReportDateTo] = useState<string>("");
  const [topicReportMinValue, setTopicReportMinValue] = useState<number>(0);

  // 标签筛选（专题报告）
  const [tags, setTags] = useState<Tag[]>([]);
  const [selectedTagIds, setSelectedTagIds] = useState<string[]>([]);

  // 实体筛选（专题报告）
  const [entities, setEntities] = useState<EntityListItem[]>([]);
  const [selectedEntityIds, setSelectedEntityIds] = useState<string[]>([]);

  // 来源类型筛选
  const [selectedSourceTypes, setSelectedSourceTypes] = useState<string[]>([]);

  // 报告深度
  const [reportDepth, setReportDepth] = useState<string>("standard");

  // 语言
  const [language, setLanguage] = useState<string>("zh-CN");

  // 模板
  const [template, setTemplate] = useState<string>("default");

  // 加载专题列表
  useEffect(() => {
    if (open && topics.length === 0) {
      fetchTopics().catch(() => {});
    }
  }, [open, topics.length, fetchTopics]);

  // 加载标签和实体列表（专题报告筛选用）
  useEffect(() => {
    if (!open) return;
    tagApi.list().then(setTags).catch(() => {});
    entityApi.list().then((res) => setEntities(res.items)).catch(() => {});
  }, [open]);

  // 弹窗打开时设置默认值
  useEffect(() => {
    if (open) {
      setActiveTab(defaultTab);
      if (defaultTopicId) {
        setDailyTopicId(defaultTopicId);
        setWeeklyTopicId(defaultTopicId);
        setTopicReportTopicId(defaultTopicId);
      }
    }
  }, [open, defaultTab, defaultTopicId]);

  // 重置表单
  useEffect(() => {
    if (!open) {
      setDailyDate(getToday());
      setWeeklyStartDate("");
      setWeeklyEndDate("");
      setTopicReportTitle("");
      setTopicReportQuestion("");
      setTopicReportDateFrom("");
      setTopicReportDateTo("");
      setTopicReportMinValue(0);
      setSelectedTagIds([]);
      setSelectedEntityIds([]);
      setSelectedSourceTypes([]);
      setReportDepth("standard");
      setLanguage("zh-CN");
      setTemplate("default");
    }
  }, [open]);

  const handleError = (err: unknown) => {
    if (err instanceof ApiRequestError) {
      toast.error(err.message);
    } else {
      toast.error("报告生成失败，请重试");
    }
  };

  const handleSuccess = async (reportJobId: string) => {
    toast.success("报告生成中...");
    // 尝试通过 job 状态获取 reportId，以便直接跳转详情页并显示进度
    let reportId: string | undefined;
    try {
      const jobStatus = await reportApi.getJobStatus(reportJobId);
      reportId = jobStatus.reportId;
    } catch {
      // 忽略错误，回退到列表页
    }
    onOpenChange(false);
    if (reportId) {
      router.push(`/reports/${reportId}?jobId=${reportJobId}`);
    } else {
      router.push("/reports");
    }
  };

  // 日报提交
  const onDailySubmit = async () => {
    if (!dailyTopicId) {
      toast.error("请选择专题");
      return;
    }
    if (!dailyDate) {
      toast.error("请选择日期");
      return;
    }
    setSubmitting(true);
    try {
      const res = await reportApi.createDaily({
        topicId: dailyTopicId,
        date: dailyDate,
      });
      await handleSuccess(res.reportJobId);
    } catch (err) {
      handleError(err);
    } finally {
      setSubmitting(false);
    }
  };

  // 周报提交
  const onWeeklySubmit = async () => {
    if (!weeklyTopicId) {
      toast.error("请选择专题");
      return;
    }
    if (!weeklyStartDate) {
      toast.error("请选择开始日期");
      return;
    }
    if (!weeklyEndDate) {
      toast.error("请选择结束日期");
      return;
    }
    if (weeklyStartDate > weeklyEndDate) {
      toast.error("开始日期不能晚于结束日期");
      return;
    }
    setSubmitting(true);
    try {
      const res = await reportApi.createWeekly({
        topicId: weeklyTopicId,
        startDate: weeklyStartDate,
        endDate: weeklyEndDate,
      });
      await handleSuccess(res.reportJobId);
    } catch (err) {
      handleError(err);
    } finally {
      setSubmitting(false);
    }
  };

  // 专题报告提交
  const onTopicReportSubmit = async () => {
    if (!topicReportTopicId) {
      toast.error("请选择专题");
      return;
    }
    if (!topicReportTitle.trim()) {
      toast.error("请输入报告标题");
      return;
    }
    if (!topicReportQuestion.trim()) {
      toast.error("请输入研究问题");
      return;
    }

    const filters: {
      dateFrom?: string;
      dateTo?: string;
      minValueScore?: number;
      tagIds?: string[];
      entityIds?: string[];
      sourceTypes?: string[];
    } = {};
    if (topicReportDateFrom) filters.dateFrom = topicReportDateFrom;
    if (topicReportDateTo) filters.dateTo = topicReportDateTo;
    if (topicReportMinValue > 0) filters.minValueScore = topicReportMinValue;
    if (selectedTagIds.length > 0) filters.tagIds = selectedTagIds;
    if (selectedEntityIds.length > 0) filters.entityIds = selectedEntityIds;
    if (selectedSourceTypes.length > 0) filters.sourceTypes = selectedSourceTypes;

    setSubmitting(true);
    try {
      const res = await reportApi.createTopic({
        topicId: topicReportTopicId,
        title: topicReportTitle.trim(),
        question: topicReportQuestion.trim(),
        filters: Object.keys(filters).length > 0 ? filters : undefined,
        depth: reportDepth,
        language,
        template,
      });
      await handleSuccess(res.reportJobId);
    } catch (err) {
      handleError(err);
    } finally {
      setSubmitting(false);
    }
  };

  // 专题选择器
  const TopicSelect = ({
    value,
    onChange,
  }: {
    value: string;
    onChange: (v: string) => void;
  }) => (
    <div className="space-y-2">
      <Label>所属专题</Label>
      <Select value={value} onValueChange={(v) => onChange(v as string)}>
        <SelectTrigger className="w-full">
          <SelectValue placeholder="请选择专题" />
        </SelectTrigger>
        <SelectContent>
          {topics.map((t) => (
            <SelectItem key={t.id} value={t.id}>
              {t.name}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-xl max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>生成报告</DialogTitle>
          <DialogDescription>
            支持日报、周报和专题报告三种报告类型
          </DialogDescription>
        </DialogHeader>

        <Tabs
          value={activeTab}
          onValueChange={(v) => setActiveTab(v as string)}
        >
          <TabsList className="w-full">
            <TabsTrigger value="daily" className="flex-1">
              <CalendarDays className="mr-1 size-3.5" />
              日报
            </TabsTrigger>
            <TabsTrigger value="weekly" className="flex-1">
              <CalendarRange className="mr-1 size-3.5" />
              周报
            </TabsTrigger>
            <TabsTrigger value="topic" className="flex-1">
              <FileSearch className="mr-1 size-3.5" />
              专题报告
            </TabsTrigger>
          </TabsList>

          {/* 日报 Tab */}
          <TabsContent value="daily" className="mt-4">
            <div className="space-y-4">
              <TopicSelect
                value={dailyTopicId}
                onChange={setDailyTopicId}
              />
              <div className="space-y-2">
                <Label htmlFor="daily-date">日期</Label>
                <Input
                  id="daily-date"
                  type="date"
                  value={dailyDate}
                  onChange={(e) => setDailyDate(e.target.value)}
                />
              </div>
              <Button
                className="w-full"
                disabled={submitting}
                onClick={onDailySubmit}
              >
                {submitting && (
                  <Loader2 className="mr-2 size-4 animate-spin" />
                )}
                生成日报
              </Button>
            </div>
          </TabsContent>

          {/* 周报 Tab */}
          <TabsContent value="weekly" className="mt-4">
            <div className="space-y-4">
              <TopicSelect
                value={weeklyTopicId}
                onChange={setWeeklyTopicId}
              />
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-2">
                  <Label htmlFor="weekly-start">开始日期</Label>
                  <Input
                    id="weekly-start"
                    type="date"
                    value={weeklyStartDate}
                    onChange={(e) => setWeeklyStartDate(e.target.value)}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="weekly-end">结束日期</Label>
                  <Input
                    id="weekly-end"
                    type="date"
                    value={weeklyEndDate}
                    onChange={(e) => setWeeklyEndDate(e.target.value)}
                  />
                </div>
              </div>
              <Button
                className="w-full"
                disabled={submitting}
                onClick={onWeeklySubmit}
              >
                {submitting && (
                  <Loader2 className="mr-2 size-4 animate-spin" />
                )}
                生成周报
              </Button>
            </div>
          </TabsContent>

          {/* 专题报告 Tab */}
          <TabsContent value="topic" className="mt-4">
            <div className="space-y-4">
              <TopicSelect
                value={topicReportTopicId}
                onChange={setTopicReportTopicId}
              />
              <div className="space-y-2">
                <Label htmlFor="topic-title">报告标题</Label>
                <Input
                  id="topic-title"
                  placeholder="请输入报告标题"
                  value={topicReportTitle}
                  onChange={(e) => setTopicReportTitle(e.target.value)}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="topic-question">研究问题</Label>
                <Textarea
                  id="topic-question"
                  placeholder="请输入你希望报告回答的研究问题，例如：该领域最新技术趋势是什么？"
                  rows={4}
                  value={topicReportQuestion}
                  onChange={(e) => setTopicReportQuestion(e.target.value)}
                />
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-2">
                  <Label htmlFor="topic-date-from" className="text-xs text-muted-foreground">
                    时间范围（可选）
                  </Label>
                  <Input
                    id="topic-date-from"
                    type="date"
                    value={topicReportDateFrom}
                    onChange={(e) => setTopicReportDateFrom(e.target.value)}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="topic-date-to" className="text-xs text-muted-foreground">
                    至
                  </Label>
                  <Input
                    id="topic-date-to"
                    type="date"
                    value={topicReportDateTo}
                    onChange={(e) => setTopicReportDateTo(e.target.value)}
                  />
                </div>
              </div>
              <div className="space-y-2">
                <Label className="text-xs text-muted-foreground">
                  最低价值评分（可选）：
                  <span className="font-bold text-foreground">
                    {topicReportMinValue}
                  </span>
                </Label>
                <input
                  type="range"
                  min={0}
                  max={100}
                  step={10}
                  value={topicReportMinValue}
                  onChange={(e) =>
                    setTopicReportMinValue(Number(e.target.value))
                  }
                  className="w-full accent-primary"
                />
              </div>

              {/* 标签筛选 */}
              {tags.length > 0 && (
                <div className="space-y-2">
                  <Label className="text-xs text-muted-foreground">
                    标签筛选（可选）
                  </Label>
                  <div className="flex max-h-24 flex-wrap gap-1.5 overflow-y-auto rounded-lg border p-2">
                    {tags.map((tag) => {
                      const selected = selectedTagIds.includes(tag.id);
                      return (
                        <Badge
                          key={tag.id}
                          variant={selected ? "default" : "outline"}
                          className="cursor-pointer select-none"
                          render={
                            <button
                              type="button"
                              onClick={() => {
                                setSelectedTagIds((prev) =>
                                  prev.includes(tag.id)
                                    ? prev.filter((id) => id !== tag.id)
                                    : [...prev, tag.id]
                                );
                              }}
                            />
                          }
                        >
                          {tag.displayName || tag.name}
                        </Badge>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* 实体筛选 */}
              {entities.length > 0 && (
                <div className="space-y-2">
                  <Label className="text-xs text-muted-foreground">
                    实体筛选（可选）
                  </Label>
                  <div className="flex max-h-24 flex-wrap gap-1.5 overflow-y-auto rounded-lg border p-2">
                    {entities.map((entity) => {
                      const selected = selectedEntityIds.includes(entity.id);
                      return (
                        <Badge
                          key={entity.id}
                          variant={selected ? "default" : "outline"}
                          className="cursor-pointer select-none"
                          render={
                            <button
                              type="button"
                              onClick={() => {
                                setSelectedEntityIds((prev) =>
                                  prev.includes(entity.id)
                                    ? prev.filter((id) => id !== entity.id)
                                    : [...prev, entity.id]
                                );
                              }}
                            />
                          }
                        >
                          {entity.name}
                        </Badge>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* 来源类型筛选 */}
              <div className="space-y-2">
                <Label className="text-xs text-muted-foreground">
                  来源类型筛选（可选）
                </Label>
                <div className="flex flex-wrap gap-4">
                  {[
                    { value: "url", label: "URL" },
                    { value: "pdf", label: "PDF" },
                    { value: "text", label: "文本" },
                  ].map((item) => (
                    <label
                      key={item.value}
                      className="flex cursor-pointer items-center gap-1.5 text-sm"
                    >
                      <Checkbox
                        checked={selectedSourceTypes.includes(item.value)}
                        onCheckedChange={() => {
                          setSelectedSourceTypes((prev) =>
                            prev.includes(item.value)
                              ? prev.filter((v) => v !== item.value)
                              : [...prev, item.value]
                          );
                        }}
                      />
                      {item.label}
                    </label>
                  ))}
                </div>
              </div>

              {/* 报告深度 + 语言 + 模板 */}
              <div className="grid grid-cols-3 gap-3">
                <div className="space-y-2">
                  <Label className="text-xs text-muted-foreground">
                    报告深度
                  </Label>
                  <Select
                    value={reportDepth}
                    onValueChange={(v) => setReportDepth(v as string)}
                  >
                    <SelectTrigger className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="brief">简要</SelectItem>
                      <SelectItem value="standard">标准</SelectItem>
                      <SelectItem value="deep">深度</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label className="text-xs text-muted-foreground">语言</Label>
                  <Select
                    value={language}
                    onValueChange={(v) => setLanguage(v as string)}
                  >
                    <SelectTrigger className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="zh-CN">中文</SelectItem>
                      <SelectItem value="en-US">英文</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div className="space-y-2">
                  <Label className="text-xs text-muted-foreground">模板</Label>
                  <Select
                    value={template}
                    onValueChange={(v) => setTemplate(v as string)}
                  >
                    <SelectTrigger className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="default">默认模板</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </div>

              <Button
                className="w-full"
                disabled={submitting}
                onClick={onTopicReportSubmit}
              >
                {submitting && (
                  <Loader2 className="mr-2 size-4 animate-spin" />
                )}
                生成专题报告
              </Button>
            </div>
          </TabsContent>
        </Tabs>
      </DialogContent>
    </Dialog>
  );
}
