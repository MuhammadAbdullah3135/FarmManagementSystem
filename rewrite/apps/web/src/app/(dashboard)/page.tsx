import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@fms/ui/card";
import {
  Bug,
  Heart,
  DollarSign,
  Users,
  TrendingUp,
  Activity,
} from "lucide-react";

const stats = [
  {
    title: "Total Animals",
    value: "—",
    description: "Register your first animal",
    icon: Bug,
    color: "text-blue-600",
  },
  {
    title: "Active Health Cases",
    value: "—",
    description: "No active cases",
    icon: Heart,
    color: "text-red-600",
  },
  {
    title: "Monthly Revenue",
    value: "—",
    description: "Record your first income",
    icon: DollarSign,
    color: "text-green-600",
  },
  {
    title: "Employees",
    value: "—",
    description: "Add your first employee",
    icon: Users,
    color: "text-purple-600",
  },
];

export default function DashboardPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-3xl font-bold tracking-tight">Dashboard</h1>
        <p className="text-muted-foreground">
          Welcome to the Farm Management System
        </p>
      </div>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        {stats.map((stat) => (
          <Card key={stat.title}>
            <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-2">
              <CardTitle className="text-sm font-medium">
                {stat.title}
              </CardTitle>
              <stat.icon className={`h-4 w-4 ${stat.color}`} />
            </CardHeader>
            <CardContent>
              <div className="text-2xl font-bold">{stat.value}</div>
              <CardDescription>{stat.description}</CardDescription>
            </CardContent>
          </Card>
        ))}
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <TrendingUp className="h-4 w-4" />
              Quick Start
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <p className="text-sm text-muted-foreground">
              To get started with the Farm Management System:
            </p>
            <ol className="list-inside list-decimal space-y-2 text-sm">
              <li>Create your farm profile</li>
              <li>Register your first animals</li>
              <li>Set up feed types and schedules</li>
              <li>Record weight and health data</li>
              <li>Track expenses and income</li>
            </ol>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Activity className="h-4 w-4" />
              System Status
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-2">
              <div className="flex items-center justify-between text-sm">
                <span className="text-muted-foreground">API Status</span>
                <span className="font-medium text-green-600">Connected</span>
              </div>
              <div className="flex items-center justify-between text-sm">
                <span className="text-muted-foreground">Database</span>
                <span className="font-medium text-green-600">Healthy</span>
              </div>
              <div className="flex items-center justify-between text-sm">
                <span className="text-muted-foreground">Auth Provider</span>
                <span className="font-medium text-green-600">Supabase</span>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
