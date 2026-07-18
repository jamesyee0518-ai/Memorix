import Constants from "expo-constants";

type ExtraConfig = {
  apiBaseUrl?: string;
};

const extra = (Constants.expoConfig?.extra ?? {}) as ExtraConfig;

export const API_BASE_URL = extra.apiBaseUrl ?? "http://localhost:9101/api";
export const CLIENT_VERSION = "memorix-mobile/0.1.0";
