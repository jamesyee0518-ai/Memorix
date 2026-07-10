"use client";

import { create } from "zustand";
import { authApi, setToken, clearToken, getToken } from "@/lib/api";
import type { User } from "@/lib/types";

interface AuthState {
  token: string | null;
  user: User | null;
  isLoading: boolean;
  isAuthenticated: boolean;
  isLocalAnonymous: boolean;

  /** 初始化：从 localStorage 恢复 token 并获取用户信息 */
  init: () => Promise<void>;
  /** 登录 */
  login: (email: string, password: string) => Promise<void>;
  /** 退出登录 */
  logout: () => Promise<void>;
  /** 获取当前用户信息 */
  fetchMe: () => Promise<void>;
  /** 进入本地匿名模式 */
  enterLocalAnonymous: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  token: null,
  user: null,
  isLoading: false,
  isAuthenticated: false,
  isLocalAnonymous: false,

  init: async () => {
    const token = getToken();
    if (!token) {
      set({
        token: null,
        user: null,
        isAuthenticated: false,
        isLocalAnonymous: false,
      });
      return;
    }
    set({ token, isLoading: true });
    try {
      const user = await authApi.me();
      set({
        user,
        isLoading: false,
        isAuthenticated: true,
        isLocalAnonymous: false,
      });
    } catch {
      clearToken();
      set({
        token: null,
        user: null,
        isLoading: false,
        isAuthenticated: false,
        isLocalAnonymous: false,
      });
    }
  },

  login: async (email: string, password: string) => {
    const res = await authApi.login(email, password);
    setToken(res.token);
    set({
      token: res.token,
      user: {
        userId: res.userId,
        email: res.email,
        nickname: res.nickname ?? "",
        avatarUrl: res.avatarUrl,
        planCode: res.planCode,
      },
      isAuthenticated: true,
      isLocalAnonymous: false,
    });
  },

  logout: async () => {
    try {
      await authApi.logout();
    } catch {
      // 忽略登出接口错误
    }
    clearToken();
    set({
      token: null,
      user: null,
      isAuthenticated: false,
      isLocalAnonymous: false,
    });
  },

  fetchMe: async () => {
    try {
      const user = await authApi.me();
      set({ user, isAuthenticated: true, isLocalAnonymous: false });
    } catch {
      clearToken();
      set({
        token: null,
        user: null,
        isAuthenticated: false,
        isLocalAnonymous: false,
      });
    }
  },

  enterLocalAnonymous: () => {
    clearToken();
    set({
      token: null,
      user: {
        userId: "00000000-0000-0000-0000-000000000001",
        email: "local@knowledge-engine.local",
        nickname: "本地用户",
        planCode: "local",
      },
      isLoading: false,
      isAuthenticated: false,
      isLocalAnonymous: true,
    });
  },
}));
