import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  transpilePackages: ["@fms/shared", "@fms/ui"],
  experimental: {
    serverActions: {
      bodySizeLimit: "2mb",
    },
  },
};

export default nextConfig;
