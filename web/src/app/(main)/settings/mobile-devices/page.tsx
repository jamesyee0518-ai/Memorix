"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { toast } from "sonner";
import {
  Clock,
  Loader2,
  RefreshCw,
  ShieldOff,
  Smartphone,
  Wifi,
} from "lucide-react";
import { ApiRequestError, mobileDevicesApi } from "@/lib/api";
import type { MobileDevice } from "@/lib/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { cn } from "@/lib/utils";

function formatDate(dateStr?: string) {
  if (!dateStr) return "-";
  return new Date(dateStr).toLocaleString("zh-CN");
}

function formatPlatform(platform?: string) {
  if (!platform) return "未知平台";
  const p = platform.toLowerCase();
  if (p.includes("ios")) return "iOS";
  if (p.includes("android")) return "Android";
  if (p.includes("mac")) return "macOS";
  if (p.includes("win")) return "Windows";
  return platform;
}

function statusLabel(status: string) {
  return status === "active" ? "已启用" : status === "revoked" ? "已停用" : status;
}

export default function MobileDevicesPage() {
  const [devices, setDevices] = useState<MobileDevice[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isCreatingPairingCode, setIsCreatingPairingCode] = useState(false);
  const [pairingCode, setPairingCode] = useState<{ code: string; expiresAt: string } | null>(null);
  const [deactivatingClientId, setDeactivatingClientId] = useState<string | null>(null);

  const loadDevices = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await mobileDevicesApi.list();
      setDevices(data);
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "加载移动设备失败";
      toast.error(message);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    loadDevices();
  }, [loadDevices]);

  const stats = useMemo(() => {
    const active = devices.filter((d) => d.status === "active").length;
    const withPush = devices.filter((d) => Boolean(d.pushToken)).length;
    return { total: devices.length, active, withPush };
  }, [devices]);

  const deactivate = async (device: MobileDevice) => {
    const confirmed = window.confirm(`停用设备「${device.deviceName || device.clientId}」？`);
    if (!confirmed) return;

    setDeactivatingClientId(device.clientId);
    try {
      await mobileDevicesApi.deactivate(device.clientId);
      toast.success("设备已停用");
      await loadDevices();
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "停用设备失败";
      toast.error(message);
    } finally {
      setDeactivatingClientId(null);
    }
  };

  const createPairingCode = async () => {
    setIsCreatingPairingCode(true);
    try {
      const code = await mobileDevicesApi.createPairingCode();
      setPairingCode(code);
      toast.success("配对码已生成");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "生成配对码失败";
      toast.error(message);
    } finally {
      setIsCreatingPairingCode(false);
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-16">
        <Loader2 className="size-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">移动设备</h2>
          <p className="text-sm text-muted-foreground">
            管理已绑定的手机采集设备
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            disabled={isCreatingPairingCode}
            onClick={createPairingCode}
          >
            {isCreatingPairingCode ? (
              <Loader2 className="mr-2 size-4 animate-spin" />
            ) : (
              <Smartphone className="mr-2 size-4" />
            )}
            生成配对码
          </Button>
          <Button variant="outline" size="sm" onClick={loadDevices}>
            <RefreshCw className="mr-2 size-4" />
            刷新
          </Button>
        </div>
      </div>

      {pairingCode && (
        <Card>
          <CardContent className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <p className="text-sm font-medium">手机端配对码</p>
              <p className="mt-1 text-xs text-muted-foreground">
                在手机 App 中输入此配对码完成设备绑定
              </p>
            </div>
            <div className="flex items-center gap-4">
              <span className="font-mono text-3xl font-semibold tracking-widest">
                {pairingCode.code}
              </span>
              <Badge variant="outline">
                {formatDate(pairingCode.expiresAt)} 过期
              </Badge>
            </div>
          </CardContent>
        </Card>
      )}

      <div className="grid gap-3 sm:grid-cols-3">
        <Card>
          <CardContent className="flex items-center justify-between p-4">
            <div>
              <p className="text-xs text-muted-foreground">全部设备</p>
              <p className="mt-1 text-2xl font-semibold">{stats.total}</p>
            </div>
            <Smartphone className="size-5 text-primary" />
          </CardContent>
        </Card>
        <Card>
          <CardContent className="flex items-center justify-between p-4">
            <div>
              <p className="text-xs text-muted-foreground">启用中</p>
              <p className="mt-1 text-2xl font-semibold">{stats.active}</p>
            </div>
            <Wifi className="size-5 text-green-600" />
          </CardContent>
        </Card>
        <Card>
          <CardContent className="flex items-center justify-between p-4">
            <div>
              <p className="text-xs text-muted-foreground">可推送</p>
              <p className="mt-1 text-2xl font-semibold">{stats.withPush}</p>
            </div>
            <Clock className="size-5 text-blue-600" />
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">设备列表</CardTitle>
          <CardDescription>停用后，该设备的刷新凭证会立即失效</CardDescription>
        </CardHeader>
        <CardContent>
          {devices.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <Smartphone className="mb-4 size-12 text-muted-foreground/50" />
              <p className="text-sm font-medium">暂无移动设备</p>
              <p className="mt-1 text-sm text-muted-foreground">
                使用手机采集页或原生 App 绑定后会显示在这里
              </p>
            </div>
          ) : (
            <div className="divide-y">
              {devices.map((device) => {
                const active = device.status === "active";
                const deactivating = deactivatingClientId === device.clientId;
                return (
                  <div
                    key={device.id}
                    className="flex flex-col gap-3 py-4 sm:flex-row sm:items-center sm:justify-between"
                  >
                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="truncate text-sm font-medium">
                          {device.deviceName || "未命名设备"}
                        </p>
                        <Badge
                          variant="secondary"
                          className={cn(
                            active
                              ? "bg-green-100 text-green-700"
                              : "bg-muted text-muted-foreground"
                          )}
                        >
                          {statusLabel(device.status)}
                        </Badge>
                        <Badge variant="outline">
                          {formatPlatform(device.platform)}
                        </Badge>
                        {device.pushToken && (
                          <Badge variant="outline">推送已注册</Badge>
                        )}
                      </div>
                      <div className="mt-2 grid gap-1 text-xs text-muted-foreground sm:grid-cols-2 lg:grid-cols-4">
                        <span className="truncate">设备 ID：{device.clientId}</span>
                        <span>绑定：{formatDate(device.boundAt)}</span>
                        <span>最后活跃：{formatDate(device.lastSeenAt)}</span>
                        <span>刷新凭证：{formatDate(device.refreshTokenExpiresAt)}</span>
                      </div>
                    </div>
                    <div className="flex items-center justify-end gap-2">
                      {active ? (
                        <Button
                          variant="destructive"
                          size="sm"
                          disabled={deactivating}
                          onClick={() => deactivate(device)}
                        >
                          {deactivating ? (
                            <Loader2 className="mr-2 size-4 animate-spin" />
                          ) : (
                            <ShieldOff className="mr-2 size-4" />
                          )}
                          停用
                        </Button>
                      ) : (
                        <span className="text-xs text-muted-foreground">已失效</span>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
