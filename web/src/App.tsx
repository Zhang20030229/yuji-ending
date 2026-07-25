import { RouterProvider } from "react-router-dom";
import { router } from "./router";

/** ECHORA Web 应用入口。 */
export default function App() {
  return <RouterProvider router={router} />;
}
