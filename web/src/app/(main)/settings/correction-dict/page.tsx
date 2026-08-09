"use client";

import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BookCheck, Loader2, Plus, Sparkles, Trash2, Wand2 } from "lucide-react";
import { toast } from "sonner";
import { correctionApi, ApiRequestError } from "@/lib/api";
import type { CorrectionDictionaryDto, AddCorrectionEntryRequest, CorrectionResult } from "@/lib/types";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Textarea } from "@/components/ui/textarea";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

const emptyForm: AddCorrectionEntryRequest = {
  original: "",
  corrected: "",
  category: "general",
};

export default function CorrectionDictPage() {
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState<AddCorrectionEntryRequest>(emptyForm);
  const [testText, setTestText] = useState("");
  const [testResult, setTestResult] = useState<CorrectionResult | null>(null);
  const queryClient = useQueryClient();

  const entries = useQuery({
    queryKey: ["correction-entries"],
    queryFn: () => correctionApi.listEntries(),
  });

  const addEntry = useMutation({
    mutationFn: (data: AddCorrectionEntryRequest) => correctionApi.addEntry(data),
    onSuccess: () => {
      toast.success("词条已添加");
      setOpen(false);
      setForm(emptyForm);
      queryClient.invalidateQueries({ queryKey: ["correction-entries"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "添加词条失败"),
  });

  const deleteEntry = useMutation({
    mutationFn: (id: string) => correctionApi.deleteEntry(id),
    onSuccess: () => {
      toast.success("词条已删除");
      queryClient.invalidateQueries({ queryKey: ["correction-entries"] });
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "删除失败"),
  });

  const correct = useMutation({
    mutationFn: (text: string) => correctionApi.correct({ text }),
    onSuccess: (data) => {
      setTestResult(data);
      toast.success(`已应用 ${data.correctionsApplied} 处纠正`);
    },
    onError: (error) =>
      toast.error(error instanceof ApiRequestError ? error.message : "纠错失败"),
  });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-lg font-semibold">纠错词典</h2>
          <p className="text-sm text-muted-foreground">管理文本纠错词条并测试纠错效果</p>
        </div>
        <Button onClick={() => { setForm(emptyForm); setOpen(true); }}>
          <Plus className="mr-2 size-4" />添加词条
        </Button>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">词条列表</CardTitle>
          <CardDescription>已有纠错词条</CardDescription>
        </CardHeader>
        <CardContent className="p-0">
          {entries.isLoading ? (
            <div className="flex justify-center py-12">
              <Loader2 className="size-6 animate-spin text-muted-foreground" />
            </div>
          ) : (entries.data?.length ?? 0) === 0 ? (
            <div className="py-12 text-center text-sm text-muted-foreground">
              <BookCheck className="mx-auto mb-3 size-8 opacity-40" />
              暂无词条
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>原文</TableHead>
                  <TableHead>纠正</TableHead>
                  <TableHead>分类</TableHead>
                  <TableHead>启用</TableHead>
                  <TableHead>操作</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {entries.data!.map((e: CorrectionDictionaryDto) => (
                  <TableRow key={e.id}>
                    <TableCell className="font-medium">{e.originalText}</TableCell>
                    <TableCell>{e.correctedText}</TableCell>
                    <TableCell>
                      <Badge variant="outline">{e.category}</Badge>
                    </TableCell>
                    <TableCell>
                      <Badge variant={e.isActive ? "default" : "secondary"}>
                        {e.isActive ? "启用" : "停用"}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={deleteEntry.isPending}
                        onClick={() => deleteEntry.mutate(e.id)}
                      >
                        <Trash2 className="size-3.5 text-destructive" />
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {/* 纠错测试 */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2 text-base">
            <Wand2 className="size-5 text-primary" />
            纠错测试
          </CardTitle>
          <CardDescription>输入文本测试纠错效果</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="test-text">输入文本</Label>
            <Textarea
              id="test-text"
              value={testText}
              onChange={(e) => setTestText(e.target.value)}
              placeholder="输入需要纠错的文本…"
              className="min-h-20"
            />
          </div>
          <Button
            disabled={!testText.trim() || correct.isPending}
            onClick={() => correct.mutate(testText)}
          >
            {correct.isPending ? (
              <Loader2 className="mr-2 size-4 animate-spin" />
            ) : (
              <Sparkles className="mr-2 size-4" />
            )}
            执行纠错
          </Button>
          {testResult && (
            <div className="space-y-3 rounded-lg border bg-muted/30 p-4">
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium">纠错结果</span>
                <Badge variant="secondary">应用 {testResult.correctionsApplied} 处</Badge>
              </div>
              <p className="text-sm">{testResult.correctedText}</p>
              {testResult.corrections.length > 0 && (
                <div className="space-y-1">
                  <span className="text-xs text-muted-foreground">修改详情:</span>
                  {testResult.corrections.map((c, i) => (
                    <div key={i} className="text-xs text-muted-foreground">
                      <span className="line-through">{c.original}</span>
                      {" → "}
                      <span className="font-medium text-foreground">{c.corrected}</span>
                      <Badge variant="outline" className="ml-2">{c.category}</Badge>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}
        </CardContent>
      </Card>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>添加词条</DialogTitle>
            <DialogDescription>填写原文与纠正后的文本</DialogDescription>
          </DialogHeader>
          <div className="space-y-4">
            <div className="space-y-2">
              <Label>原文</Label>
              <Input
                value={form.original}
                onChange={(e) => setForm({ ...form, original: e.target.value })}
                placeholder="需要纠正的原文"
              />
            </div>
            <div className="space-y-2">
              <Label>纠正</Label>
              <Input
                value={form.corrected}
                onChange={(e) => setForm({ ...form, corrected: e.target.value })}
                placeholder="纠正后的文本"
              />
            </div>
            <div className="space-y-2">
              <Label>分类</Label>
              <Select
                value={form.category}
                onValueChange={(v) => setForm({ ...form, category: v ?? "general" })}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="general">通用</SelectItem>
                  <SelectItem value="typo">错别字</SelectItem>
                  <SelectItem value="entity">实体名</SelectItem>
                  <SelectItem value="terminology">术语</SelectItem>
                  <SelectItem value="brand">品牌名</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)}>取消</Button>
            <Button
              disabled={!form.original.trim() || !form.corrected.trim() || addEntry.isPending}
              onClick={() => addEntry.mutate(form)}
            >
              {addEntry.isPending && <Loader2 className="mr-2 size-4 animate-spin" />}
              添加
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
