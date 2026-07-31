import { redirect } from "next/navigation";

export default function LegacyUsagePage() {
  redirect("/billing/usage");
}
