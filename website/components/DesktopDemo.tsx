"use client";

import { useCallback, useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type PointerEvent as ReactPointerEvent } from "react";
import { demoFiles, type Action, type Category, type DemoFile, type Icon, type Item } from "@/lib/menu";
import styles from "./DesktopDemo.module.css";

const OUTER = 118;
const HUB = 44;
const SIZE = (OUTER + 8) * 2;
const DRAG_THRESHOLD = 5;
const TAP_MS = 450;
const CATEGORY_DWELL = 350;
const BACK_DWELL = 500;

type Desktop = { name: string; kind: "jpg" | "png" | "pdf" | "image" | "folder"; source: string; fresh?: boolean };
type Drag = { file: DemoFile; startX: number; startY: number; x: number; y: number; moved: boolean };
type Menu = {
  file: DemoFile;
  x: number;
  y: number;
  category: Category | null;
  interactive: boolean;
  hover: number | "hub" | null;
  phase: "menu" | "running" | "result" | "dialog";
  status: string;
  mascot: string;
};

const initialDesktop = (): Desktop[] => demoFiles.map((f) => ({ name: f.name, kind: f.type, source: f.id }));

function kindOf(name: string): Desktop["kind"] {
  if (!name.includes(".")) return "folder";
  if (name.endsWith(".pdf")) return "pdf";
  return "image";
}

function fileCount(file: DemoFile) {
  return file.type === "pdf" ? "1 PDF" : "1 image";
}

function hitTest(dx: number, dy: number, count: number): number | "hub" | null {
  const distance = Math.hypot(dx, dy);
  if (distance <= HUB) return "hub";
  if (distance > OUTER || count === 0) return null;
  const sweep = 360 / count;
  const angle = (Math.atan2(dx, -dy) * 180) / Math.PI;
  const normalized = (angle + 360 + sweep / 2) % 360;
  return Math.floor(normalized / sweep) % count;
}

function sector(index: number, count: number) {
  const sweep = (2 * Math.PI) / count;
  const start = index * sweep - sweep / 2 - Math.PI / 2;
  const end = start + sweep;
  const c = SIZE / 2;
  const point = (r: number, a: number) => `${c + r * Math.cos(a)} ${c + r * Math.sin(a)}`;
  const large = sweep > Math.PI ? 1 : 0;
  if (count === 1) {
    return `M ${c} ${c - OUTER} A ${OUTER} ${OUTER} 0 1 1 ${c - 0.01} ${c - OUTER} L ${c - 0.01} ${c - HUB - 6} A ${HUB + 6} ${HUB + 6} 0 1 0 ${c} ${c - HUB - 6} Z`;
  }
  return [
    `M ${point(HUB + 6, start)}`,
    `L ${point(OUTER, start)}`,
    `A ${OUTER} ${OUTER} 0 ${large} 1 ${point(OUTER, end)}`,
    `L ${point(HUB + 6, end)}`,
    `A ${HUB + 6} ${HUB + 6} 0 ${large} 0 ${point(HUB + 6, start)}`,
    "Z",
  ].join(" ");
}

function labelPosition(index: number, count: number) {
  const angle = (index * 2 * Math.PI) / count - Math.PI / 2;
  const r = (OUTER + HUB + 6) / 2;
  return { left: SIZE / 2 + r * Math.cos(angle), top: SIZE / 2 + r * Math.sin(angle) };
}

export default function DesktopDemo() {
  const desktopRef = useRef<HTMLDivElement>(null);
  const segmentRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const [desktop, setDesktop] = useState<Desktop[]>(initialDesktop);
  const [selected, setSelected] = useState<string | null>(null);
  const [drag, setDrag] = useState<Drag | null>(null);
  const [menu, setMenu] = useState<Menu | null>(null);
  const [hint, setHint] = useState<string>("");
  const shiftDownAt = useRef<number | null>(null);
  const dwell = useRef<{ target: number | "hub"; timer: number } | null>(null);
  const timers = useRef<number[]>([]);

  const items: Item[] = menu ? (menu.category ? menu.category.items : menu.file.menu) : [];

  const later = useCallback((fn: () => void, ms: number) => {
    const id = window.setTimeout(fn, ms);
    timers.current.push(id);
  }, []);

  useEffect(() => () => timers.current.forEach(clearTimeout), []);

  const cancelDwell = () => {
    if (dwell.current) clearTimeout(dwell.current.timer);
    dwell.current = null;
  };

  const local = (clientX: number, clientY: number) => {
    const rect = desktopRef.current!.getBoundingClientRect();
    return { x: clientX - rect.left, y: clientY - rect.top };
  };

  const clampCenter = (x: number, y: number) => {
    const rect = desktopRef.current!.getBoundingClientRect();
    const half = SIZE / 2;
    return {
      x: Math.min(Math.max(x, half), Math.max(half, rect.width - half)),
      y: Math.min(Math.max(y, half), Math.max(half, rect.height - half - 40)),
    };
  };

  const closeMenu = useCallback(() => {
    cancelDwell();
    setMenu(null);
  }, []);

  const openMenu = (file: DemoFile, x: number, y: number, interactive: boolean) => {
    const center = clampCenter(x, y);
    setHint("");
    setMenu({
      file,
      ...center,
      category: null,
      interactive,
      hover: null,
      phase: "menu",
      status: fileCount(file),
      mascot: interactive ? "idle" : "dragging",
    });
  };

  const enterCategory = (category: Category) => {
    cancelDwell();
    setMenu((m) => m && { ...m, category, hover: null, status: `${category.label} — ${fileCount(m.file)}` });
  };

  const goBack = () => {
    cancelDwell();
    setMenu((m) => m && { ...m, category: null, hover: null, status: fileCount(m.file) });
  };

  const run = (action: Action) => {
    cancelDwell();
    if ("dialog" in action.result) {
      setMenu((m) => m && { ...m, interactive: true, phase: "dialog", status: action.result && "dialog" in action.result ? action.result.dialog : "", mascot: "idle" });
      return;
    }
    const outputs = action.result.outputs;
    setMenu((m) => m && { ...m, interactive: true, phase: "running", hover: null, status: `${action.description}…`, mascot: action.mascot });
    later(() => {
      setMenu((m) => m && { ...m, phase: "result", status: `Saved ${outputs.length === 1 ? "1 file" : `${outputs.length} files`}`, mascot: "success" });
      setDesktop((d) => [
        ...d.map((f) => ({ ...f, fresh: false })),
        ...outputs.filter((name) => !d.some((f) => f.name === name)).map((name) => ({ name, kind: kindOf(name), source: menu?.file.id ?? "", fresh: true })),
      ]);
      later(() => setMenu((m) => (m?.phase === "result" ? null : m)), 2600);
    }, 900);
  };

  const activate = (index: number, fromDrop: boolean) => {
    const item = items[index];
    if (!item) return;
    if (item.kind === "category") {
      enterCategory(item);
      if (fromDrop) setMenu((m) => m && { ...m, interactive: true, mascot: "idle" });
      return;
    }
    run(item);
  };

  const updateHover = (target: number | "hub" | null) => {
    if (!menu || menu.phase !== "menu") return;
    if (target === menu.hover) return;
    cancelDwell();
    const item = typeof target === "number" ? items[target] : null;
    setMenu((m) => m && { ...m, hover: target, mascot: m.interactive ? "idle" : typeof target === "number" ? "hovering" : "dragging", status: item ? item.description : m.category ? `${m.category.label} — ${fileCount(m.file)}` : fileCount(m.file) });
    if (menu.interactive) return;
    if (item?.kind === "category") {
      dwell.current = { target: target as number, timer: window.setTimeout(() => enterCategory(item), CATEGORY_DWELL) };
    } else if (target === "hub" && menu.category) {
      dwell.current = { target, timer: window.setTimeout(goBack, BACK_DWELL) };
    }
  };

  // Dragging a file: pointer capture keeps the events coming while the pointer is over the menu or outside.
  const onFilePointerDown = (event: ReactPointerEvent, file: DemoFile) => {
    if (event.button !== 0 || menu?.phase === "running") return;
    if (menu) closeMenu();
    setSelected(file.id);
    if (event.pointerType !== "mouse") return;
    event.currentTarget.setPointerCapture(event.pointerId);
    const p = local(event.clientX, event.clientY);
    setDrag({ file, startX: p.x, startY: p.y, x: p.x, y: p.y, moved: false });
  };

  const onFilePointerMove = (event: ReactPointerEvent) => {
    if (!drag) return;
    const p = local(event.clientX, event.clientY);
    const moved = drag.moved || Math.hypot(p.x - drag.startX, p.y - drag.startY) > DRAG_THRESHOLD;
    setDrag({ ...drag, x: p.x, y: p.y, moved });
    if (menu && !menu.interactive) updateHover(hitTest(p.x - menu.x, p.y - menu.y, items.length));
    if (moved && !drag.moved && !menu) setHint("Now tap Shift");
  };

  const onFilePointerUp = (event: ReactPointerEvent, file: DemoFile) => {
    const current = drag;
    setDrag(null);
    shiftDownAt.current = null;
    if (event.pointerType !== "mouse") {
      const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
      const p = local(rect.left + rect.width / 2, rect.top + rect.height / 2);
      openMenu(file, p.x, p.y, true);
      return;
    }
    if (!current?.moved) {
      setHint("");
      return;
    }
    if (!menu || menu.interactive) {
      setHint(menu ? "" : "Tap Shift while you drag to open Kumo");
      return;
    }
    const p = local(event.clientX, event.clientY);
    const target = hitTest(p.x - menu.x, p.y - menu.y, items.length);
    cancelDwell();
    if (target === null) {
      closeMenu();
      return;
    }
    if (target === "hub") {
      setMenu((m) => m && { ...m, interactive: true, mascot: "idle", hover: null });
      return;
    }
    activate(target, true);
  };

  // Shift is a tap (pressed and released quickly) during a drag, like in Kumo; holding it keeps Explorer's move.
  useEffect(() => {
    if (!drag?.moved) return;
    const down = (e: KeyboardEvent) => {
      if (e.key === "Shift" && !e.repeat) shiftDownAt.current = performance.now();
      if (e.key === "Escape") {
        setDrag(null);
        closeMenu();
        setHint("");
      }
    };
    const up = (e: KeyboardEvent) => {
      if (e.key !== "Shift" || shiftDownAt.current === null) return;
      const quick = performance.now() - shiftDownAt.current <= TAP_MS;
      shiftDownAt.current = null;
      if (quick && !menu) openMenu(drag.file, drag.x, drag.y, false);
    };
    window.addEventListener("keydown", down);
    window.addEventListener("keyup", up);
    return () => {
      window.removeEventListener("keydown", down);
      window.removeEventListener("keyup", up);
    };
  });

  const openFromKeyboard = (file: DemoFile, element: HTMLElement) => {
    const rect = element.getBoundingClientRect();
    const p = local(rect.left + rect.width / 2, rect.top + rect.height / 2);
    openMenu(file, p.x + SIZE / 2, p.y, true);
    setSelected(file.id);
  };

  const onFileKeyDown = (event: ReactKeyboardEvent, file: DemoFile) => {
    const shortcut = event.ctrlKey && event.altKey && event.key.toLowerCase() === "k";
    if (event.key === "Enter" || event.key === " " || shortcut) {
      event.preventDefault();
      openFromKeyboard(file, event.currentTarget as HTMLElement);
    }
  };

  useEffect(() => {
    if (menu?.interactive && menu.phase === "menu") segmentRefs.current[0]?.focus();
  }, [menu?.interactive, menu?.phase, menu?.category]);

  const onMenuKeyDown = (event: ReactKeyboardEvent) => {
    if (!menu) return;
    if (event.key === "Escape") {
      event.preventDefault();
      if (menu.category && menu.phase === "menu") goBack();
      else closeMenu();
      return;
    }
    const step = event.key === "ArrowRight" || event.key === "ArrowDown" ? 1 : event.key === "ArrowLeft" || event.key === "ArrowUp" ? -1 : 0;
    if (!step || !items.length) return;
    event.preventDefault();
    const current = segmentRefs.current.findIndex((b) => b === document.activeElement);
    const next = current < 0 ? 0 : (current + step + items.length) % items.length;
    segmentRefs.current[next]?.focus();
  };

  const reset = () => {
    timers.current.forEach(clearTimeout);
    closeMenu();
    setDesktop(initialDesktop());
    setSelected(null);
    setHint("");
  };

  const dragging = drag?.moved;

  return (
    <div className={styles.demo}>
      <div
        ref={desktopRef}
        className={styles.desktop}
        onPointerDown={(e) => {
          if (e.target === e.currentTarget) {
            setSelected(null);
            if (menu?.interactive && menu.phase !== "running") closeMenu();
          }
        }}
      >
        <ul className={styles.icons} aria-label="Demo desktop">
          {desktop.map((entry) => {
            const file = demoFiles.find((f) => f.name === entry.name);
            const thumbSource = demoFiles.find((f) => f.id === entry.source);
            return (
              <li key={entry.name}>
                {file ? (
                  <button
                    type="button"
                    className={`${styles.icon} ${selected === file.id ? styles.selected : ""} ${dragging && drag?.file.id === file.id ? styles.lifted : ""}`}
                    onPointerDown={(e) => onFilePointerDown(e, file)}
                    onPointerMove={onFilePointerMove}
                    onPointerUp={(e) => onFilePointerUp(e, file)}
                    onPointerCancel={() => setDrag(null)}
                    onKeyDown={(e) => onFileKeyDown(e, file)}
                    aria-label={`${file.name}. Press Enter to open Kumo for this file.`}
                  >
                    <Thumb kind={entry.kind} source={entry.source} />
                    <span className={styles.name}>{entry.name}</span>
                  </button>
                ) : (
                  <div className={`${styles.icon} ${entry.fresh ? styles.fresh : ""}`} aria-label={`New file: ${entry.name}`} role="img">
                    <Thumb kind={entry.kind} source={thumbSource?.id ?? ""} />
                    <span className={styles.name}>{entry.name}</span>
                  </div>
                )}
              </li>
            );
          })}
        </ul>

        {dragging && drag && (
          <div className={styles.ghost} style={{ left: drag.x, top: drag.y }} aria-hidden="true">
            <Thumb kind={drag.file.type} source={drag.file.id} />
          </div>
        )}

        {menu && (
          <div
            className={styles.menu}
            style={{ left: menu.x - SIZE / 2, top: menu.y - SIZE / 2, width: SIZE }}
            role="menu"
            aria-label={`Kumo for ${menu.file.name}`}
            onKeyDown={onMenuKeyDown}
          >
            <svg width={SIZE} height={SIZE} viewBox={`0 0 ${SIZE} ${SIZE}`} aria-hidden="true" className={styles.ring}>
              <circle cx={SIZE / 2} cy={SIZE / 2} r={OUTER + 4} className={styles.disk} />
              {menu.phase === "menu" &&
                items.map((item, i) => (
                  <path key={`${menu.category?.label}-${item.label}`} d={sector(i, items.length)} className={`${styles.segment} ${menu.hover === i ? styles.hot : ""}`} />
                ))}
              {menu.phase === "running" && (
                <circle cx={SIZE / 2} cy={SIZE / 2} r={HUB + 3} className={styles.progress} pathLength={1} />
              )}
            </svg>

            {menu.phase === "menu" &&
              items.map((item, i) => {
                const pos = labelPosition(i, items.length);
                const icon = !menu.category && "icon" in item ? item.icon : undefined;
                return (
                  <button
                    key={`${menu.category?.label}-${item.label}`}
                    ref={(el) => {
                      segmentRefs.current[i] = el;
                    }}
                    type="button"
                    role="menuitem"
                    tabIndex={menu.interactive ? 0 : -1}
                    className={`${styles.label} ${menu.hover === i ? styles.labelHot : ""}`}
                    style={{ left: pos.left, top: pos.top }}
                    onClick={() => activate(i, false)}
                    onMouseEnter={() => menu.interactive && updateHover(i)}
                    onFocus={() => updateHover(i)}
                    aria-label={item.description}
                  >
                    {icon && <Glyph icon={icon} />}
                    <span>{item.label}</span>
                  </button>
                );
              })}

            <button
              type="button"
              className={`${styles.hub} ${menu.hover === "hub" ? styles.hubHot : ""}`}
              style={{ left: SIZE / 2 - HUB, top: SIZE / 2 - HUB, width: HUB * 2, height: HUB * 2 }}
              tabIndex={menu.interactive ? 0 : -1}
              onClick={() => (menu.category && menu.phase === "menu" ? goBack() : closeMenu())}
              aria-label={menu.category && menu.phase === "menu" ? "Back" : "Close"}
            >
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={`/mascot/${menu.mascot}.png`} alt="" width={64} height={64} />
            </button>

            <p className={styles.status} role="status">
              {menu.status}
            </p>
          </div>
        )}
      </div>

      <div className={styles.bar}>
        <p className={styles.hint} aria-live="polite">
          {hint || (
            <>
              <span className={styles.pointerOnly}>Drag a file and tap <kbd>Shift</kbd> while you hold it.</span>
              <span className={styles.touchOnly}>Tap a file to open Kumo.</span> Or select a file and press <kbd>Enter</kbd>.
            </>
          )}
        </p>
        <button type="button" className={styles.reset} onClick={reset} disabled={desktop.length === demoFiles.length && !menu}>
          Reset
        </button>
      </div>
    </div>
  );
}

function Thumb({ kind, source }: { kind: Desktop["kind"]; source: string }) {
  if (kind === "folder") return <span className={styles.folder} aria-hidden="true" />;
  if (kind === "pdf") return <span className={styles.pdf} aria-hidden="true">PDF</span>;
  const src = source === "logo" ? "/assets/kumo-64.png" : "/assets/strawberry-thumb.webp";
  // eslint-disable-next-line @next/next/no-img-element
  return <img className={styles.thumb} src={src} alt="" width={48} height={48} draggable={false} />;
}

function Glyph({ icon }: { icon: Icon }) {
  const paths: Record<Icon, string> = {
    convert: "M4 7h13l-3-3M20 17H7l3 3",
    crop: "M7 3v14h14M3 7h14v14",
    compress: "M12 3v6m0 0-3-3m3 3 3-3M12 21v-6m0 0-3 3m3-3 3 3M5 12h14",
    rotate: "M20 12a8 8 0 1 1-2.3-5.7M20 4v5h-5",
    split: "M6 3h8l4 4v5M6 3v18h6M15 15l6 6M21 15l-6 6",
    extract: "M6 3h8l4 4v14H6zM10 13h4M10 17h4",
    protect: "M7 11V8a5 5 0 0 1 10 0v3M5 11h14v10H5z",
  };
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" className={styles.glyph}>
      <path d={paths[icon]} />
    </svg>
  );
}
