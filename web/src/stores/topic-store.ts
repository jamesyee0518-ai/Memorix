"use client";

import { create } from "zustand";
import { topicApi } from "@/lib/api";
import type { Topic, TopicDetail, TopicCreateRequest, TopicUpdateRequest } from "@/lib/types";

interface TopicState {
  topics: Topic[];
  currentTopic: TopicDetail | null;
  isLoading: boolean;

  /** 获取专题列表 */
  fetchTopics: () => Promise<void>;
  /** 获取专题详情 */
  fetchTopic: (id: string) => Promise<void>;
  /** 创建专题 */
  createTopic: (data: TopicCreateRequest) => Promise<Topic>;
  /** 更新专题 */
  updateTopic: (id: string, data: TopicUpdateRequest) => Promise<void>;
  /** 删除专题 */
  deleteTopic: (id: string) => Promise<void>;
  /** 清空状态 */
  reset: () => void;
}

export const useTopicStore = create<TopicState>((set, get) => ({
  topics: [],
  currentTopic: null,
  isLoading: false,

  fetchTopics: async () => {
    set({ isLoading: true });
    try {
      const result = await topicApi.list();
      set({ topics: result.items, isLoading: false });
    } catch {
      set({ isLoading: false });
      throw new Error("获取专题列表失败");
    }
  },

  fetchTopic: async (id: string) => {
    set({ isLoading: true });
    try {
      const topic = await topicApi.get(id);
      set({ currentTopic: topic, isLoading: false });
    } catch {
      set({ isLoading: false });
      throw new Error("获取专题详情失败");
    }
  },

  createTopic: async (data: TopicCreateRequest) => {
    const response = await topicApi.create(data);
    const topic: Topic = {
      id: response.id,
      name: response.name,
      description: response.description,
      domain: response.domain,
      documentCount: 0,
      pendingCount: 0,
      failedCount: 0,
      createdAt: response.createdAt,
    };
    set({ topics: [topic, ...get().topics] });
    return topic;
  },

  updateTopic: async (id: string, data: TopicUpdateRequest) => {
    await topicApi.update(id, data);
    // 更新列表中的对应项
    const topics = get().topics.map((t) =>
      t.id === id
        ? {
            ...t,
            name: data.name ?? t.name,
            description: data.description ?? t.description,
            domain: data.domain ?? t.domain,
          }
        : t
    );
    set({ topics });
  },

  deleteTopic: async (id: string) => {
    await topicApi.delete(id);
    set({ topics: get().topics.filter((t) => t.id !== id) });
  },

  reset: () => {
    set({ topics: [], currentTopic: null, isLoading: false });
  },
}));
