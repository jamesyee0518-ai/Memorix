import * as Device from "expo-device";
import * as Notifications from "expo-notifications";

Notifications.setNotificationHandler({
  handleNotification: async () => ({
    shouldPlaySound: true,
    shouldSetBadge: false,
    shouldShowBanner: true,
    shouldShowList: true,
  }),
});

export async function registerPushToken() {
  if (!Device.isDevice) return undefined;

  const current = await Notifications.getPermissionsAsync();
  const granted =
    current.status === "granted" ||
    (await Notifications.requestPermissionsAsync()).status === "granted";

  if (!granted) return undefined;
  const token = await Notifications.getExpoPushTokenAsync();
  return token.data;
}
