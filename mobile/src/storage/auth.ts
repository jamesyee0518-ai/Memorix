import AsyncStorage from "@react-native-async-storage/async-storage";

const DEVICE_ACCESS_TOKEN_KEY = "memorix.mobile.device_access_token";
const DEVICE_REFRESH_TOKEN_KEY = "memorix.mobile.device_refresh_token";

export async function getDeviceAccessToken() {
  return AsyncStorage.getItem(DEVICE_ACCESS_TOKEN_KEY);
}

export async function setDeviceAccessToken(token: string) {
  await AsyncStorage.setItem(DEVICE_ACCESS_TOKEN_KEY, token);
}

export async function getDeviceRefreshToken() {
  return AsyncStorage.getItem(DEVICE_REFRESH_TOKEN_KEY);
}

export async function setDeviceRefreshToken(token: string) {
  await AsyncStorage.setItem(DEVICE_REFRESH_TOKEN_KEY, token);
}

export async function clearDeviceTokens() {
  await AsyncStorage.multiRemove([DEVICE_ACCESS_TOKEN_KEY, DEVICE_REFRESH_TOKEN_KEY]);
}
