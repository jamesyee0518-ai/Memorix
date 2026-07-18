"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { Loader2 } from "lucide-react";
import { useAuthStore } from "@/stores/auth-store";
import { ApiRequestError } from "@/lib/api";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { MemorixBrand } from "@/components/brand/memorix-brand";

const loginSchema = z.object({
  email: z.string().email("请输入有效的邮箱地址"),
  password: z.string().min(1, "请输入密码"),
});

type LoginForm = z.infer<typeof loginSchema>;

export default function LoginPage() {
  const router = useRouter();
  const { login } = useAuthStore();
  const [submitting, setSubmitting] = useState(false);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LoginForm>({
    resolver: zodResolver(loginSchema),
  });

  const onSubmit = async (data: LoginForm) => {
    setSubmitting(true);
    try {
      await login(data.email, data.password);
      toast.success("登录成功");
      router.push("/dashboard");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "登录失败，请重试";
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Card className="border-[#DBE2EA] shadow-sm">
      <CardHeader className="text-center">
        <MemorixBrand className="mx-auto mb-3" />
        <CardTitle className="text-xl">欢迎回来</CardTitle>
        <CardDescription>登录你的个人 AI 记忆体</CardDescription>
      </CardHeader>
      <form onSubmit={handleSubmit(onSubmit)}>
        <CardContent className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="email">邮箱</Label>
            <Input
              id="email"
              type="email"
              placeholder="you@example.com"
              {...register("email")}
            />
            {errors.email && (
              <p className="text-xs text-destructive">{errors.email.message}</p>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="password">密码</Label>
            <Input
              id="password"
              type="password"
              placeholder="请输入密码"
              {...register("password")}
            />
            {errors.password && (
              <p className="text-xs text-destructive">
                {errors.password.message}
              </p>
            )}
          </div>
        </CardContent>
        <CardFooter className="flex flex-col gap-4">
          <Button
            type="submit"
            className="w-full"
            size="lg"
            disabled={submitting}
          >
            {submitting && <Loader2 className="mr-2 size-4 animate-spin" />}
            {submitting ? "登录中..." : "登录"}
          </Button>
          <div className="w-full rounded-lg border border-dashed border-slate-300 bg-slate-50 p-3 text-center">
            <p className="text-xs text-muted-foreground">
              测试账号：test@example.com / 12345678
            </p>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="mt-1 h-7 text-xs"
              onClick={() => {
                const form = (document.getElementById("email") as HTMLInputElement);
                const pass = (document.getElementById("password") as HTMLInputElement);
                if (form) form.value = "test@example.com";
                if (pass) pass.value = "12345678";
                handleSubmit(onSubmit)();
              }}
            >
              一键填充并登录
            </Button>
          </div>
          <p className="text-sm text-muted-foreground">
            还没有账号？{" "}
            <Link
              href="/register"
              className="font-medium text-primary hover:underline"
            >
              立即注册
            </Link>
          </p>
        </CardFooter>
      </form>
    </Card>
  );
}
