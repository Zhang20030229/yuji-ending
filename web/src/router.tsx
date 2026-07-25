import { createBrowserRouter, Navigate } from "react-router-dom";
import AppLayout from "@/components/layout/AppLayout";
import AccessPage from "@/pages/access/AccessPage";

/** 主路由呈现 0.4 的我、AI、星图、生命之树信息架构，收件箱与拾光归档为子路由。 */
export const router = createBrowserRouter([
  { path: "/access", element: <AccessPage /> },
  {
    path: "/onboarding",
    lazy: async () => ({ Component: (await import("@/pages/onboarding/OnboardingPage")).default }),
    hydrateFallbackElement: <div className="min-h-dvh bg-[#070706]" aria-label="正在载入首次引导" />,
  },
  {
    path: "/app",
    element: <AppLayout />,
    children: [
      { index: true, element: <Navigate to="home" replace /> },
      {
        path: "home",
        lazy: async () => ({ Component: (await import("@/pages/home/HomePage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入" />,
      },
      {
        path: "insights",
        lazy: async () => ({ Component: (await import("@/pages/insights/InsightsPage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入情绪洞察" />,
      },
      {
        path: "companion",
        lazy: async () => ({ Component: (await import("@/pages/companion/CompanionPage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入对话" />,
      },
      {
        path: "map",
        lazy: async () => ({ Component: (await import("@/pages/map/MapPage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入星图" />,
      },
      {
        path: "timeline",
        lazy: async () => ({ Component: (await import("@/pages/map/TimelinePage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入时间线" />,
      },
      {
        path: "tree",
        lazy: async () => ({ Component: (await import("@/pages/tree/TreePage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入生命之树" />,
      },
      {
        path: "people",
        lazy: async () => ({ Component: (await import("@/pages/home/HomeEntityListPage")).PeopleListPage }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入人物列表" />,
      },
      {
        path: "people/:itemId",
        lazy: async () => ({ Component: (await import("@/pages/home/HomeDetailPage")).PersonDetailPage }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入人物详情" />,
      },
      {
        path: "places",
        lazy: async () => ({ Component: (await import("@/pages/home/HomeEntityListPage")).PlacesListPage }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入地点列表" />,
      },
      {
        path: "places/:itemId",
        lazy: async () => ({ Component: (await import("@/pages/home/HomeDetailPage")).PlaceDetailPage }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入地点详情" />,
      },
      {
        path: "recognitions",
        lazy: async () => ({ Component: (await import("@/pages/home/HomeEntityListPage")).RecognitionsListPage }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入认识列表" />,
      },
      {
        path: "recognitions/:itemId",
        lazy: async () => ({ Component: (await import("@/pages/home/HomeDetailPage")).RecognitionDetailPage }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入认识详情" />,
      },
      {
        path: "settings",
        lazy: async () => ({ Component: (await import("@/pages/settings/SettingsPage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入设置" />,
      },
      {
        path: "inbox",
        lazy: async () => ({ Component: (await import("@/pages/inbox/InboxPage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入收件箱" />,
      },
      {
        path: "inbox/:pendingId",
        lazy: async () => ({ Component: (await import("@/pages/inbox/InboxResolvePage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入收件箱" />,
      },
      {
        path: "fragments",
        lazy: async () => ({ Component: (await import("@/pages/archive/FragmentsPage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入片段" />,
      },
      {
        path: "fragments/:itemId",
        lazy: async () => ({ Component: (await import("@/pages/archive/FragmentsPage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入片段" />,
      },
      {
        path: "events",
        lazy: async () => ({ Component: (await import("@/pages/archive/EventsPage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入事件" />,
      },
      {
        path: "events/:itemId",
        lazy: async () => ({ Component: (await import("@/pages/archive/EventsPage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入事件" />,
      },
      {
        path: "reports",
        lazy: async () => ({ Component: (await import("@/pages/self/ReportsPage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入报告" />,
      },
      {
        path: "reports/:itemId",
        lazy: async () => ({ Component: (await import("@/pages/self/ReportsPage")).default }),
        hydrateFallbackElement: <div className="h-full bg-background" aria-label="正在载入报告" />,
      },
    ],
  },
  { path: "*", element: <Navigate to="/access" replace /> },
]);
