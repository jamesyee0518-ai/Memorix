export default function AuthLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="flex min-h-screen items-center justify-center border-t-4 border-[#0D47A1] bg-[#F2F4F7]">
      <div className="w-full max-w-md px-4">{children}</div>
    </div>
  );
}
