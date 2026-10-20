import type { Metadata, Viewport } from "next";
import SiteFooter from "@/components/SiteFooter";
import SiteHeader from "@/components/SiteHeader";
import { site } from "@/lib/site";
import "./globals.css";

const description = "Kumo is a free, open-source Windows utility. Drag files, tap Shift, and convert, crop, compress or combine them from a radial menu.";

export const metadata: Metadata = {
  ...(site.url ? { metadataBase: new URL(site.url), alternates: { canonical: "/" } } : {}),
  title: { default: "Kumo — quick file actions for Windows", template: "%s — Kumo" },
  description,
  icons: {
    icon: [{ url: "/favicon.ico", sizes: "any" }, { url: "/assets/favicon-32.png", type: "image/png" }],
    apple: "/assets/apple-touch-icon.png",
  },
  openGraph: {
    type: "website",
    title: "Kumo — quick file actions for Windows",
    description: "Drag files, tap Shift, and convert, crop, compress or combine them from a radial menu. Free and open source.",
    ...(site.url ? { url: site.url, images: [{ url: "/assets/og.jpg", width: 1200, height: 630 }] } : {}),
  },
  twitter: { card: "summary_large_image" },
};

export const viewport: Viewport = { themeColor: "#faf6f0" };

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <a className="skip" href="#main">
          Skip to content
        </a>
        <SiteHeader />
        <main id="main">{children}</main>
        <SiteFooter />
      </body>
    </html>
  );
}
