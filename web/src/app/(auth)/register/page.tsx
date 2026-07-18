"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { Loader2 } from "lucide-react";
import { authApi, ApiRequestError } from "@/lib/api";
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

const registerSchema = z.object({
  nickname: z.string().min(1, "请输入昵称").max(50, "昵称不能超过50个字符"),
  email: z.string().email("请输入有效的邮箱地址"),
  password: z.string().min(8, "密码至少需要8位字符"),
});

type RegisterForm = z.infer<typeof registerSchema>;

export default function RegisterPage() {
  const router = useRouter();
  const [submitting, setSubmitting] = useState(false);

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<RegisterForm>({
    resolver: zodResolver(registerSchema),
  });

  const onSubmit = async (data: RegisterForm) => {
    setSubmitting(true);
    try {
      await authApi.register({
        email: data.email,
        password: data.password,
        nickname: data.nickname,
      });
      toast.success("注册成功，请登录");
      router.push("/login");
    } catch (err) {
      const message =
        err instanceof ApiRequestError ? err.message : "注册失败，请重试";
      toast.error(message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Card className="border-[#DBE2EA] shadow-sm">
      <CardHeader className="text-center">
        <MemorixBrand className="mx-auto mb-3" />
        <CardTitle className="text-xl">创建账号</CardTitle>
        <CardDescription>开始建立你的个人 AI 记忆体</CardDescription>
      </CardHeader>
      <form onSubmit={handleSubmit(onSubmit)}>
        <CardContent className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="nickname">昵称</Label>
            <Input
              id="nickname"
              placeholder="请输入昵称"
              {...register("nickname")}
            />
            {errors.nickname && (
              <p className="text-xs text-destructive">
                {errors.nickname.message}
              </p>
            )}
          </div>
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
              placeholder="至少8位字符"
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
            {submitting ? "注册中..." : "注册"}
          </Button>
          <p className="text-sm text-muted-foreground">
            已有账号？{" "}
            <Link
              href="/login"
              className="font-medium text-primary hover:underline"
            >
              返回登录
            </Link>
          </p>
        </CardFooter>
      </form>
    </Card>
  );
}
