"use client";

import { create } from "zustand";

interface FeedbackState {
  /** 反馈弹窗是否打开 */
  isOpen: boolean;
  /** 关联对象类型（由调用方传入，隐藏字段） */
  relatedEntityType?: string;
  /** 关联对象ID（由调用方传入，隐藏字段） */
  relatedEntityId?: string;
  /** 预填模块 */
  prefillModule?: string;

  /** 打开反馈弹窗 */
  open: (options?: {
    relatedEntityType?: string;
    relatedEntityId?: string;
    prefillModule?: string;
  }) => void;
  /** 关闭反馈弹窗 */
  close: () => void;
}

export const useFeedbackStore = create<FeedbackState>((set) => ({
  isOpen: false,
  relatedEntityType: undefined,
  relatedEntityId: undefined,
  prefillModule: undefined,

  open: (options) =>
    set({
      isOpen: true,
      relatedEntityType: options?.relatedEntityType,
      relatedEntityId: options?.relatedEntityId,
      prefillModule: options?.prefillModule,
    }),

  close: () =>
    set({
      isOpen: false,
      relatedEntityType: undefined,
      relatedEntityId: undefined,
      prefillModule: undefined,
    }),
}));
