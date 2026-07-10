import AsyncStorage from "@react-native-async-storage/async-storage";

const CLIENT_ID_KEY = "memorix.mobile.client_id";

export async function getClientId() {
  const existing = await AsyncStorage.getItem(CLIENT_ID_KEY);
  if (existing) return existing;

  const id = `mobile-${Date.now()}-${Math.random().toString(16).slice(2)}`;
  await AsyncStorage.setItem(CLIENT_ID_KEY, id);
  return id;
}
