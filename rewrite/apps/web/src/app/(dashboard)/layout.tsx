"use client";

import { useEffect, useState } from "react";
import { useRouter, usePathname } from "next/navigation";
import Link from "next/link";
import { createClient } from "@/lib/supabase/client";
import { cn } from "@fms/ui/lib/utils";
import { Button } from "@fms/ui/button";
import { ScrollArea } from "@fms/ui/scroll-area";
import { Separator } from "@fms/ui/separator";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@fms/ui/dropdown-menu";
import {
  LayoutDashboard,
  Bug,
  Heart,
  DollarSign,
  Users,
  BarChart3,
  Package,
  Coffee,
  Calendar,
  Settings,
  LogOut,
  ChevronDown,
  Activity,
  Shield,
} from "lucide-react";
import type { User } from "@supabase/supabase-js";

interface NavItem {
  label: string;
  href: string;
  icon: React.ComponentType<{ className?: string }>;
}

const navGroups: { title: string; items: NavItem[] }[] = [
  {
    title: "Overview",
    items: [
      { label: "Dashboard", href: "/dashboard", icon: LayoutDashboard },
    ],
  },
  {
    title: "Operations",
    items: [
      { label: "Animals", href: "/dashboard/animals", icon: Bug },
      { label: "Feed Management", href: "/dashboard/feed", icon: Coffee },
      { label: "Tasks", href: "/dashboard/tasks", icon: Calendar },
    ],
  },
  {
    title: "Health",
    items: [
      { label: "Medical Records", href: "/dashboard/health", icon: Heart },
      { label: "Breeding", href: "/dashboard/breeding", icon: Activity },
    ],
  },
  {
    title: "Business",
    items: [
      { label: "Finance", href: "/dashboard/finance", icon: DollarSign },
      { label: "Inventory", href: "/dashboard/inventory", icon: Package },
      { label: "Employees", href: "/dashboard/hr", icon: Users },
    ],
  },
  {
    title: "Insights",
    items: [
      { label: "Reports", href: "/dashboard/reports", icon: BarChart3 },
      { label: "Audit Log", href: "/dashboard/admin/audit-log", icon: Shield },
    ],
  },
];

export default function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const router = useRouter();
  const pathname = usePathname();
  const supabase = createClient();

  useEffect(() => {
    async function getUser() {
      const {
        data: { user },
      } = await supabase.auth.getUser();
      setUser(user);
      setLoading(false);
    }
    getUser();
  }, [supabase]);

  async function handleLogout() {
    await supabase.auth.signOut();
    router.push("/login");
    router.refresh();
  }

  if (loading) {
    return (
      <div className="flex h-screen items-center justify-center">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-primary border-t-transparent" />
      </div>
    );
  }

  return (
    <div className="flex h-screen">
      {/* Sidebar */}
      <aside className="hidden w-64 flex-shrink-0 border-r bg-sidebar lg:block">
        <div className="flex h-14 items-center border-b px-4">
          <Link href="/dashboard" className="text-lg font-bold text-sidebar-foreground">
            🐄 FMS
          </Link>
        </div>
        <ScrollArea className="h-[calc(100vh-3.5rem)]">
          <div className="space-y-6 p-4">
            {navGroups.map((group) => (
              <div key={group.title}>
                <h4 className="mb-2 text-xs font-semibold uppercase tracking-wider text-sidebar-foreground/60">
                  {group.title}
                </h4>
                <nav className="space-y-1">
                  {group.items.map((item) => {
                    const isActive =
                      pathname === item.href ||
                      (item.href !== "/dashboard" &&
                        pathname.startsWith(item.href));
                    return (
                      <Link
                        key={item.href}
                        href={item.href}
                        className={cn(
                          "flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors",
                          isActive
                            ? "bg-sidebar-accent text-sidebar-accent-foreground"
                            : "text-sidebar-foreground/80 hover:bg-sidebar-accent/50 hover:text-sidebar-accent-foreground"
                        )}
                      >
                        <item.icon className="h-4 w-4" />
                        {item.label}
                      </Link>
                    );
                  })}
                </nav>
              </div>
            ))}
          </div>
        </ScrollArea>
      </aside>

      {/* Main content */}
      <div className="flex flex-1 flex-col overflow-hidden">
        {/* Header */}
        <header className="flex h-14 items-center justify-between border-b bg-background px-6">
          <div />
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" className="flex items-center gap-2">
                <div className="flex h-8 w-8 items-center justify-center rounded-full bg-primary text-primary-foreground text-sm font-medium">
                  {user?.email?.[0]?.toUpperCase() || "?"}
                </div>
                <span className="hidden text-sm md:inline-block">
                  {user?.email}
                </span>
                <ChevronDown className="h-4 w-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-56">
              <DropdownMenuLabel>
                <div className="flex flex-col space-y-1">
                  <p className="text-sm font-medium">
                    {user?.user_metadata?.first_name}{" "}
                    {user?.user_metadata?.last_name}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {user?.email}
                  </p>
                </div>
              </DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={handleLogout}>
                <LogOut className="mr-2 h-4 w-4" />
                Sign out
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </header>

        {/* Page content */}
        <main className="flex-1 overflow-y-auto p-6">{children}</main>
      </div>
    </div>
  );
}
