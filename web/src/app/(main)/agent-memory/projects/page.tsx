"use client";

import Link from "next/link";
import { useQuery } from "@tanstack/react-query";
import {
  FolderGit2,
  Loader2,
  GitBranch,
  ExternalLink,
  Network,
} from "lucide-react";
import { agentMemoryApi } from "@/lib/api";
import { Badge } from "@/components/ui/badge";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
} from "@/components/ui/card";

export default function ProjectsPage() {
  const { data: projects, isLoading } = useQuery({
    queryKey: ["agent-memory-projects"],
    queryFn: () => agentMemoryApi.listProjects(),
  });

  const { data: sessions } = useQuery({
    queryKey: ["agent-memory-sessions-for-projects"],
    queryFn: () => agentMemoryApi.listSessions(200, 0),
  });

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold flex items-center gap-2">
          <FolderGit2 className="h-6 w-6 text-primary" />
          Project 归并视图
        </h1>
        <p className="text-muted-foreground text-sm mt-1">
          按 git 仓库归并的跨 agent 会话视图 — 同一仓库的不同 agent 共享记忆与交接
        </p>
      </div>

      {isLoading ? (
        <div className="flex justify-center py-12">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      ) : !projects || projects.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center justify-center py-16 text-muted-foreground">
            <FolderGit2 className="h-10 w-10 mb-3 opacity-50" />
            <p>暂无 Project</p>
            <p className="text-xs mt-1">
              当 agent 采集事件携带 git remote 时,Project 会自动创建
            </p>
          </CardContent>
        </Card>
      ) : (
        <div className="grid gap-4 md:grid-cols-2">
          {projects.map((p) => {
            const projectSessions = sessions?.filter((s) => s.projectId === p.id) ?? [];
            const agents = new Set(
              projectSessions.map((s) => s.externalSessionKey.split(":")[0])
            );

            return (
              <Card key={p.id}>
                <CardHeader className="pb-3">
                  <div className="flex items-start justify-between">
                    <div className="flex items-center gap-2">
                      <Network className="h-5 w-5 text-muted-foreground" />
                      <div>
                        <CardTitle className="text-base">{p.repoName}</CardTitle>
                        <CardDescription className="font-mono text-xs mt-0.5">
                          {p.projectKey.slice(0, 16)}...
                        </CardDescription>
                      </div>
                    </div>
                    <Badge variant="secondary">{projectSessions.length} 会话</Badge>
                  </div>
                </CardHeader>
                <CardContent className="space-y-3 pt-0">
                  {/* Git remote */}
                  {p.gitRemote && (
                    <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
                      <GitBranch className="h-3 w-3" />
                      <span className="truncate font-mono">{p.gitRemote}</span>
                    </div>
                  )}

                  {/* Agents involved */}
                  {agents.size > 0 && (
                    <div className="flex flex-wrap gap-1">
                      {Array.from(agents).map((agent) => (
                        <Badge key={agent} variant="outline" className="text-xs">
                          {agent}
                        </Badge>
                      ))}
                    </div>
                  )}

                  {/* Recent sessions */}
                  {projectSessions.length > 0 && (
                    <div className="space-y-1 pt-1">
                      <p className="text-xs font-medium text-muted-foreground">最近会话</p>
                      {projectSessions.slice(0, 3).map((s) => (
                        <Link
                          key={s.id}
                          href={`/agent-memory/sessions/${s.id}/events`}
                          className="flex items-center justify-between rounded-md p-1.5 text-xs hover:bg-muted transition-colors"
                        >
                          <span className="truncate">{s.taskTitle}</span>
                          <div className="flex items-center gap-1 shrink-0">
                            <Badge variant="outline" className="text-xs">
                              {s.externalSessionKey.split(":")[0]}
                            </Badge>
                            <ExternalLink className="h-3 w-3 text-muted-foreground" />
                          </div>
                        </Link>
                      ))}
                    </div>
                  )}

                  {/* Timestamps */}
                  <div className="flex justify-between text-xs text-muted-foreground pt-1 border-t">
                    <span>创建: {new Date(p.createdAt).toLocaleDateString("zh-CN")}</span>
                    <span>更新: {new Date(p.updatedAt).toLocaleDateString("zh-CN")}</span>
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
