"use client";

import { useCallback, useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type PointerEvent as ReactPointerEvent, type ReactNode } from "react";
import {
  categoryItems,
  checkPages,
  entriesFor,
  execute,
  folder,
  initialDesktop,
  MINIMUM_PASSWORD_LENGTH,
  newId,
  progressText,
  rootItems,
  selectionText,
  stem,
  unavailableText,
  type Category,
  type DemoFile,
  type MenuEntry,
  type Options,
  type RadialItem,
} from "@/lib/kumo";
import CropEditor from "./CropEditor";
import { FolderView, ImageViewer, PaintApp, PdfViewer, TextEditor, Thumb, Window, type WindowState } from "./DemoWindows";
import styles from "./DesktopDemo.module.css";

const OUTER = 118;
const HUB = 44;
const SIZE = (OUTER + 8) * 2;
const DRAG_THRESHOLD = 5;
const TAP_MS = 450;
const CATEGORY_DWELL = 350;
const BACK_DWELL = 500;
const STEP_MS = 420;
const CELL_W = 92;
const CELL_H = 92;
const PAD_X = 10;
const PAD_Y = 12;

type Drag = { ids: string[]; startX: number; startY: number; x: number; y: number; moved: boolean };
type Marquee = { startX: number; startY: number; x: number; y: number; base: string[] };
type Phase = "menu" | "unavailable" | "dialog" | "running" | "result";
type Dialog = { entry: MenuEntry; requirement: "crop" | "pages" | "password" };
type Menu = {
  files: DemoFile[];
  entries: MenuEntry[];
  x: number;
  y: number;
  category: Category | null;
  interactive: boolean;
  hover: number | "hub" | null;
  phase: Phase;
  status: string;
  mascot: string;
  dialog?: Dialog;
};
type ContextTarget = { type: "desktop" } | { type: "file"; id: string } | { type: "child"; folderId: string; child: DemoFile };
type ContextMenu = { x: number; y: number; target: ContextTarget };
type Tip = "kumo" | "action" | "open" | "draw";

const TIPS: { id: Tip; text: ReactNode }[] = [
  {
    id: "kumo",
    text: (
      <>
        <span className={styles.pointerOnly}>
          Drag <b>strawberry.jpg</b> and tap <kbd>Shift</kbd>
        </span>
        <span className={styles.touchOnly}>
          Tap <b>strawberry.jpg</b>
        </span>
      </>
    ),
  },
  { id: "action", text: "Pick an action, like Rotate" },
  { id: "open", text: "Double-click a file to open it" },
  { id: "draw", text: "Right-click the desktop for a new drawing" },
];
const TIPS_KEY = "kumo-demo-tips";

function hitTest(dx: number, dy: number, count: number): number | "hub" | null {
  const distance = Math.hypot(dx, dy);
  if (distance <= HUB) return "hub";
  if (distance > OUTER || count === 0) return null;
  const sweep = 360 / count;
  const angle = (Math.atan2(dx, -dy) * 180) / Math.PI;
  return Math.floor(((angle + 360 + sweep / 2) % 360) / sweep) % count;
}

function sector(index: number, count: number) {
  const c = SIZE / 2;
  const inner = HUB + 6;
  if (count === 1) {
    return `M ${c} ${c - OUTER} A ${OUTER} ${OUTER} 0 1 1 ${c - 0.01} ${c - OUTER} L ${c - 0.01} ${c - inner} A ${inner} ${inner} 0 1 0 ${c} ${c - inner} Z`;
  }
  const sweep = (2 * Math.PI) / count;
  const start = index * sweep - sweep / 2 - Math.PI / 2;
  const end = start + sweep;
  const point = (r: number, a: number) => `${c + r * Math.cos(a)} ${c + r * Math.sin(a)}`;
  const large = sweep > Math.PI ? 1 : 0;
  return `M ${point(inner, start)} L ${point(OUTER, start)} A ${OUTER} ${OUTER} 0 ${large} 1 ${point(OUTER, end)} L ${point(inner, end)} A ${inner} ${inner} 0 ${large} 0 ${point(inner, start)} Z`;
}

function labelPosition(index: number, count: number) {
  const angle = (index * 2 * Math.PI) / count - Math.PI / 2;
  const r = (OUTER + HUB + 6) / 2;
  return { left: SIZE / 2 + r * Math.cos(angle), top: SIZE / 2 + r * Math.sin(angle) };
}

const intersects = (a: DOMRect, b: { left: number; top: number; right: number; bottom: number }) =>
  a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top;

/** Places icons without a position in the first free cells, column by column, like the Windows desktop. */
function arrange(files: DemoFile[], height: number): DemoFile[] {
  const rows = Math.max(1, Math.floor((height - PAD_Y) / CELL_H));
  const cell = (f: DemoFile) => `${Math.round((f.x! - PAD_X) / CELL_W)},${Math.round((f.y! - PAD_Y) / CELL_H)}`;
  const taken = new Set(files.filter((f) => f.x !== undefined).map(cell));
  let next = 0;
  return files.map((f) => {
    if (f.x !== undefined) return f;
    while (taken.has(`${Math.floor(next / rows)},${next % rows}`)) next++;
    const placed = { ...f, x: PAD_X + Math.floor(next / rows) * CELL_W, y: PAD_Y + (next % rows) * CELL_H };
    taken.add(`${Math.floor(next / rows)},${next % rows}`);
    return placed;
  });
}

/** Windows numbers a new item when its name is taken: "New folder", "New folder (2)". */
function uniqueName(base: string, extension: string, existing: DemoFile[]) {
  const names = new Set(existing.map((f) => f.name.toLowerCase()));
  for (let n = 1; ; n++) {
    const name = `${base}${n === 1 ? "" : ` (${n})`}${extension}`;
    if (!names.has(name.toLowerCase())) return name;
  }
}

const extensionOf = (f: DemoFile) => (f.kind === "folder" ? "" : f.name.slice(stem(f.name).length));

/** `entered` turns true when the desktop becomes visible; the icons, tips and welcome notification arrive then. */
export default function DesktopDemo({ immersive = false, entered = true }: { immersive?: boolean; entered?: boolean }) {
  const desktopRef = useRef<HTMLDivElement>(null);
  const iconRefs = useRef(new Map<string, HTMLElement>());
  const segmentRefs = useRef<(HTMLButtonElement | null)[]>([]);
  const [desktop, setDesktop] = useState<DemoFile[]>(initialDesktop);
  const desktopNow = useRef(desktop);
  desktopNow.current = desktop;
  const [selected, setSelected] = useState<string[]>([]);
  const [drag, setDrag] = useState<Drag | null>(null);
  const [marquee, setMarquee] = useState<Marquee | null>(null);
  const [menu, setMenu] = useState<Menu | null>(null);
  const [context, setContext] = useState<ContextMenu | null>(null);
  const [renaming, setRenaming] = useState<string | null>(null);
  const [windows, setWindows] = useState<WindowState[]>([]);
  const [hint, setHint] = useState("");
  const [arriving, setArriving] = useState(true);
  const [welcome, setWelcome] = useState(false);
  const [showTips, setShowTips] = useState(true);
  const [tried, setTried] = useState<Tip[]>([]);
  const shiftDownAt = useRef<number | null>(null);
  const dwell = useRef<number | null>(null);
  const timers = useRef<number[]>([]);
  const zTop = useRef(10);

  const items: RadialItem[] = !menu || menu.phase !== "menu" ? [] : menu.category ? categoryItems(menu.entries, menu.category) : rootItems(menu.entries);

  const later = useCallback((fn: () => void, ms: number) => {
    timers.current.push(window.setTimeout(fn, ms));
  }, []);

  useEffect(() => () => timers.current.forEach(clearTimeout), []);

  useEffect(() => {
    try {
      if (localStorage.getItem(TIPS_KEY) === "hidden") setShowTips(false);
    } catch {
      // Storage can be unavailable (private windows, blocked site data); the tips then stay visible.
    }
  }, []);

  useEffect(() => {
    if (!entered) return;
    const done = window.setTimeout(() => setArriving(false), 1400);
    const show = window.setTimeout(() => setWelcome(true), 900);
    const hide = window.setTimeout(() => setWelcome(false), 9000);
    return () => [done, show, hide].forEach(clearTimeout);
  }, [entered]);

  const mark = (tip: Tip) => setTried((t) => (t.includes(tip) ? t : [...t, tip]));

  const toggleTips = (visible: boolean) => {
    setShowTips(visible);
    try {
      if (visible) localStorage.removeItem(TIPS_KEY);
      else localStorage.setItem(TIPS_KEY, "hidden");
    } catch {
      // Not remembered for the next visit.
    }
  };

  useLayoutEffect(() => {
    if (desktop.some((f) => f.x === undefined) && desktopRef.current) setDesktop((d) => arrange(d, desktopRef.current!.clientHeight));
  }, [desktop]);

  const cancelDwell = () => {
    if (dwell.current) clearTimeout(dwell.current);
    dwell.current = null;
  };

  const local = (clientX: number, clientY: number) => {
    const rect = desktopRef.current!.getBoundingClientRect();
    return { x: clientX - rect.left, y: clientY - rect.top };
  };

  const bounds = () => ({ w: desktopRef.current!.clientWidth, h: desktopRef.current!.clientHeight });

  const clampCenter = (x: number, y: number) => {
    const { w, h } = bounds();
    const half = SIZE / 2;
    return { x: Math.min(Math.max(x, half), Math.max(half, w - half)), y: Math.min(Math.max(y, half), Math.max(half, h - half - 44)) };
  };

  const closeMenu = useCallback(() => {
    cancelDwell();
    setMenu(null);
  }, []);

  const statusFor = (m: Pick<Menu, "files" | "category">) => (m.category ? `${m.category} — ${selectionText(m.files)}` : selectionText(m.files));

  // OverlayController.BeginLoading: folders are ignored unless nothing else was dropped.
  const openMenu = (dropped: DemoFile[], x: number, y: number, interactive: boolean) => {
    const center = clampCenter(x, y);
    const files = dropped.filter((f) => f.kind !== "folder");
    const entries = entriesFor(files);
    setHint("");
    setContext(null);
    if (entries.length === 0) {
      setMenu({ files, entries, ...center, category: null, interactive: true, hover: null, phase: "unavailable", status: unavailableText(files, dropped.length > files.length), mascot: "cancelled" });
      return;
    }
    setMenu({ files, entries, ...center, category: null, interactive, hover: null, phase: "menu", status: selectionText(files), mascot: interactive ? "idle" : "dragging" });
    setWelcome(false);
    mark("kumo");
  };

  const enterCategory = (category: Category) => {
    cancelDwell();
    setMenu((m) => m && { ...m, category, hover: null, status: statusFor({ files: m.files, category }) });
  };

  const goBack = () => {
    cancelDwell();
    setMenu((m) => m && { ...m, category: null, hover: null, status: selectionText(m.files) });
  };

  const run = (entry: MenuEntry, options: Options) => {
    if (!menu) return;
    const files = menu.files;
    const total = entry.combined ? 1 : files.length;
    const mascot = entry.category === "Convert" ? "converting" : entry.category === "Compress" ? "compressing" : "processing";
    setMenu((m) => m && { ...m, phase: "running", interactive: true, hover: null, dialog: undefined, status: progressText(entry, 0, total), mascot });
    for (let i = 1; i < total; i++) later(() => setMenu((m) => m && { ...m, status: progressText(entry, i, total) }), i * STEP_MS);
    later(() => {
      const current = desktopNow.current;
      const result = execute(entry, files, options, current.map((f) => f.name));
      const tone = result.tone;
      if (tone === "success") mark("action");
      setDesktop([...current.map((f) => ({ ...f, fresh: false })), ...result.outputs]);
      setMenu((m) => m && {
        ...m,
        phase: "result",
        status: result.detail && tone !== "success" ? `${result.headline}. ${result.detail}` : result.headline,
        mascot: tone === "success" ? "success" : tone === "nothing" ? "idle" : "error",
      });
      later(() => setMenu((m) => (m?.phase === "result" ? null : m)), tone === "success" ? 2600 : tone === "nothing" ? 4000 : 8000);
    }, Math.max(1, total) * STEP_MS + 300);
  };

  const choose = (entry: MenuEntry) => {
    cancelDwell();
    if (entry.requirement === "none") {
      run(entry, {});
      return;
    }
    setMenu((m) => m && { ...m, phase: "dialog", interactive: true, hover: null, dialog: { entry, requirement: entry.requirement as Dialog["requirement"] }, mascot: "idle" });
  };

  const cancelDialog = () => setMenu((m) => m && { ...m, phase: "menu", dialog: undefined, status: statusFor(m) });

  const activate = (index: number, fromDrop: boolean) => {
    const item = items[index];
    if (!item) return;
    if (item.kind === "category") {
      enterCategory(item.category);
      if (fromDrop) setMenu((m) => m && { ...m, interactive: true, mascot: "idle" });
      return;
    }
    choose(item.entry);
  };

  const updateHover = (target: number | "hub" | null) => {
    if (!menu || menu.phase !== "menu" || target === menu.hover) return;
    cancelDwell();
    const item = typeof target === "number" ? items[target] : null;
    setMenu((m) => m && { ...m, hover: target, mascot: m.interactive ? "idle" : item ? "hovering" : "dragging", status: item ? item.description : statusFor(m) });
    if (menu.interactive) return;
    if (item?.kind === "category") dwell.current = window.setTimeout(() => enterCategory(item.category), CATEGORY_DWELL);
    else if (target === "hub" && menu.category) dwell.current = window.setTimeout(goBack, BACK_DWELL);
  };

  const filesById = (ids: string[]) => desktop.filter((f) => ids.includes(f.id));

  // ---- Windows ----

  const focusWindow = (wid: string) => setWindows((ws) => ws.map((w) => (w.wid === wid ? { ...w, z: ++zTop.current } : w)));

  const openWindow = (file: DemoFile, fileId?: string, app: WindowState["app"] = "file") => {
    if (app === "file") mark("open");
    const existing = windows.find((w) => (fileId ? w.fileId === fileId : w.file.id === file.id) && w.app === app);
    if (existing) {
      focusWindow(existing.wid);
      return;
    }
    const { w: dw, h: dh } = bounds();
    let w = 420;
    let h = 320;
    if (app === "paint") [w, h] = [600, 470];
    else if (file.kind === "pdf") [w, h] = [400, 460];
    else if (file.kind === "folder") [w, h] = [440, 300];
    else if (file.image) {
      const scale = Math.min(560 / file.image.width, 380 / file.image.height, 1.2);
      [w, h] = [Math.max(260, file.image.width * scale + 24), Math.max(180, file.image.height * scale + 58)];
    }
    w = Math.min(w, dw - 16);
    h = Math.min(h, dh - 16);
    const offset = (windows.length % 6) * 26;
    const x = Math.max(8, Math.min(dw - w - 8, (dw - w) / 2 + offset - 60));
    const y = Math.max(8, Math.min(dh - h - 8, (dh - h) / 2 + offset - 40));
    setWindows((ws) => [...ws, { wid: newId(), fileId, file, app, x, y, w, h, z: ++zTop.current }]);
  };

  const closeWindow = (wid: string) => setWindows((ws) => ws.filter((w) => w.wid !== wid));

  const moveWindow = (wid: string, x: number, y: number) => {
    const { w: dw, h: dh } = bounds();
    setWindows((ws) => ws.map((w) => (w.wid === wid ? { ...w, x: Math.min(Math.max(x, -w.w + 80), dw - 80), y: Math.min(Math.max(y, 0), dh - 34) } : w)));
  };

  const openFile = (file: DemoFile) => openWindow(file, file.id);

  const windowFile = (w: WindowState) => (w.fileId ? desktop.find((f) => f.id === w.fileId) : undefined) ?? w.file;

  const updateFile = (id: string, change: Partial<DemoFile>) => setDesktop((d) => d.map((f) => (f.id === id ? { ...f, ...change } : f)));

  // ---- Creating, renaming, deleting and moving ----

  const createAt = (x: number, y: number, file: DemoFile) => {
    const { w, h } = bounds();
    const placed = { ...file, x: Math.min(Math.max(x - CELL_W / 2, 0), w - CELL_W), y: Math.min(Math.max(y - 30, 0), h - CELL_H), fresh: true };
    setDesktop((d) => [...d.map((f) => ({ ...f, fresh: false })), placed]);
    setSelected([placed.id]);
    return placed;
  };

  const newFolder = (x: number, y: number) => {
    const made = createAt(x, y, folder(uniqueName("New folder", "", desktop)));
    setRenaming(made.id);
  };

  const newText = (x: number, y: number) => {
    const made = createAt(x, y, { id: newId(), name: uniqueName("New Text Document", ".txt", desktop), kind: "txt", text: "" });
    setRenaming(made.id);
  };

  const newDrawing = (x: number, y: number) => {
    const placeholder: DemoFile = { id: newId(), name: "Drawing.png", kind: "png", x, y };
    openWindow(placeholder, undefined, "paint");
  };

  const saveDrawing = (win: WindowState, dataUrl: string, width: number, height: number) => {
    const image = { thumb: dataUrl, full: dataUrl, width, height };
    mark("draw");
    if (win.fileId && desktopNow.current.some((f) => f.id === win.fileId)) {
      updateFile(win.fileId, { image, view: undefined });
      return;
    }
    const made = createAt(win.file.x ?? 120, win.file.y ?? 120, { id: newId(), name: uniqueName("Drawing", ".png", desktopNow.current), kind: "png", image });
    setWindows((ws) => ws.map((w) => (w.wid === win.wid ? { ...w, fileId: made.id, file: made } : w)));
  };

  const remove = (ids: string[]) => {
    setDesktop((d) => d.filter((f) => !ids.includes(f.id)));
    setWindows((ws) => ws.filter((w) => !w.fileId || !ids.includes(w.fileId)));
    setSelected((s) => s.filter((id) => !ids.includes(id)));
  };

  const commitRename = (id: string, value: string) => {
    setRenaming(null);
    const file = desktop.find((f) => f.id === id);
    const base = value.trim().replace(/[\\/:*?"<>|]/g, "");
    if (!file || !base) return;
    const ext = extensionOf(file);
    if (`${base}${ext}` === file.name) return;
    updateFile(id, { name: uniqueName(base, ext, desktop.filter((f) => f.id !== id)) });
  };

  const moveIntoFolder = (folderId: string, ids: string[]) => {
    setDesktop((d) => {
      const moving = d.filter((f) => ids.includes(f.id)).map(({ x: _x, y: _y, fresh: _f, ...rest }) => rest as DemoFile);
      return d.filter((f) => !ids.includes(f.id)).map((f) => (f.id === folderId ? { ...f, children: [...(f.children ?? []), ...moving] } : f));
    });
    setWindows((ws) => ws.filter((w) => !w.fileId || !ids.includes(w.fileId)));
    setSelected([folderId]);
  };

  const moveToDesktop = (folderId: string, child: DemoFile) => {
    setDesktop((d) => [...d.map((f) => (f.id === folderId ? { ...f, children: (f.children ?? []).filter((c) => c.id !== child.id) } : f)), { ...child, x: undefined, y: undefined, fresh: true }]);
  };

  // ---- Dragging icons: moves them, or opens Kumo when Shift is tapped on the way ----

  // Pointer capture keeps the drag going over the menu and outside the desktop.
  const onIconPointerDown = (event: ReactPointerEvent, entry: DemoFile) => {
    if (event.button !== 0 || menu?.phase === "running" || menu?.phase === "dialog" || renaming === entry.id) return;
    if (menu) closeMenu();
    setContext(null);
    event.stopPropagation();
    const additive = event.ctrlKey || event.metaKey;
    let selection = selected;
    if (additive) selection = selected.includes(entry.id) ? selected.filter((n) => n !== entry.id) : [...selected, entry.id];
    else if (!selected.includes(entry.id)) selection = [entry.id];
    setSelected(selection);
    if (event.pointerType !== "mouse" || additive || !selection.includes(entry.id)) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    const p = local(event.clientX, event.clientY);
    setDrag({ ids: selection, startX: p.x, startY: p.y, x: p.x, y: p.y, moved: false });
  };

  const onIconPointerMove = (event: ReactPointerEvent) => {
    if (!drag) return;
    const p = local(event.clientX, event.clientY);
    const moved = drag.moved || Math.hypot(p.x - drag.startX, p.y - drag.startY) > DRAG_THRESHOLD;
    setDrag({ ...drag, x: p.x, y: p.y, moved });
    if (menu && !menu.interactive && menu.phase === "menu") updateHover(hitTest(p.x - menu.x, p.y - menu.y, items.length));
    if (moved && !drag.moved && !menu) setHint("Drop to move it, or tap Shift for Kumo");
  };

  const folderUnder = (clientX: number, clientY: number, exclude: string[]) =>
    desktop.find((f) => {
      if (f.kind !== "folder" || exclude.includes(f.id)) return false;
      const r = iconRefs.current.get(f.id)?.getBoundingClientRect();
      return !!r && clientX >= r.left && clientX <= r.right && clientY >= r.top && clientY <= r.bottom;
    });

  const onIconPointerUp = (event: ReactPointerEvent, entry: DemoFile) => {
    const current = drag;
    setDrag(null);
    shiftDownAt.current = null;
    if (event.pointerType !== "mouse") {
      const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
      const p = local(rect.left + rect.width / 2, rect.top + rect.height / 2);
      openMenu(filesById(selected.includes(entry.id) ? selected : [entry.id]), p.x, p.y, true);
      return;
    }
    if (!current?.moved) {
      if (!(event.ctrlKey || event.metaKey)) setSelected([entry.id]);
      setHint("");
      return;
    }
    setHint("");
    if (menu && !menu.interactive) {
      const p = local(event.clientX, event.clientY);
      const target = menu.phase === "menu" ? hitTest(p.x - menu.x, p.y - menu.y, items.length) : null;
      cancelDwell();
      if (menu.phase !== "menu") setMenu((m) => m && { ...m, interactive: true });
      else if (target === null) closeMenu();
      else if (target === "hub") setMenu((m) => m && { ...m, interactive: true, mascot: "idle", hover: null });
      else activate(target, true);
      return;
    }
    const into = folderUnder(event.clientX, event.clientY, current.ids);
    if (into) {
      moveIntoFolder(into.id, current.ids);
      return;
    }
    const { w, h } = bounds();
    const dx = current.x - current.startX;
    const dy = current.y - current.startY;
    setDesktop((d) =>
      d.map((f) => (current.ids.includes(f.id) ? { ...f, x: Math.min(Math.max((f.x ?? 0) + dx, 0), w - CELL_W), y: Math.min(Math.max((f.y ?? 0) + dy, 0), h - CELL_H) } : f)),
    );
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
      if (quick && !menu) openMenu(filesById(drag.ids), drag.x, drag.y, false);
    };
    window.addEventListener("keydown", down);
    window.addEventListener("keyup", up);
    return () => {
      window.removeEventListener("keydown", down);
      window.removeEventListener("keyup", up);
    };
  });

  // Rubber-band selection on empty desktop space, as in File Explorer.
  const onDesktopPointerDown = (event: ReactPointerEvent) => {
    setContext(null);
    if (event.target !== event.currentTarget && !(event.target as HTMLElement).dataset.surface) return;
    if (menu) {
      if (menu.phase === "running" || menu.phase === "dialog") return;
      closeMenu();
    }
    const additive = event.ctrlKey || event.metaKey;
    if (!additive) setSelected([]);
    if (event.pointerType !== "mouse" || event.button !== 0) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    const p = local(event.clientX, event.clientY);
    setMarquee({ startX: p.x, startY: p.y, x: p.x, y: p.y, base: additive ? selected : [] });
  };

  const selectInside = (next: Marquee) => {
    const origin = desktopRef.current!.getBoundingClientRect();
    const box = {
      left: origin.left + Math.min(next.startX, next.x),
      right: origin.left + Math.max(next.startX, next.x),
      top: origin.top + Math.min(next.startY, next.y),
      bottom: origin.top + Math.max(next.startY, next.y),
    };
    const inside = [...iconRefs.current.entries()].filter(([, el]) => intersects(el.getBoundingClientRect(), box)).map(([id]) => id);
    setSelected([...new Set([...next.base, ...inside])]);
  };

  const onDesktopPointerMove = (event: ReactPointerEvent) => {
    if (!marquee) return;
    const p = local(event.clientX, event.clientY);
    const next = { ...marquee, x: p.x, y: p.y };
    setMarquee(next);
    selectInside(next);
  };

  const onDesktopPointerUp = (event: ReactPointerEvent) => {
    if (!marquee) return;
    const p = local(event.clientX, event.clientY);
    selectInside({ ...marquee, x: p.x, y: p.y });
    setMarquee(null);
  };

  const showContext = (event: React.MouseEvent, target: ContextTarget) => {
    event.preventDefault();
    event.stopPropagation();
    if (menu && (menu.phase === "running" || menu.phase === "dialog")) return;
    closeMenu();
    const p = local(event.clientX, event.clientY);
    const { w, h } = bounds();
    if (target.type === "file" && !selected.includes(target.id)) setSelected([target.id]);
    setContext({ x: Math.min(p.x, w - 200), y: Math.min(p.y, h - 220), target });
  };

  const openKumoFor = (ids: string[]) => {
    const anchor = iconRefs.current.get(ids[0])?.getBoundingClientRect();
    if (!anchor) return;
    const p = local(anchor.left + anchor.width / 2, anchor.top + anchor.height / 2);
    openMenu(filesById(ids), p.x + SIZE / 2, p.y, true);
  };

  const onIconKeyDown = (event: ReactKeyboardEvent, entry: DemoFile) => {
    if (renaming) return;
    const ids = selected.includes(entry.id) ? selected : [entry.id];
    if (event.ctrlKey && event.altKey && event.key.toLowerCase() === "k") {
      event.preventDefault();
      setSelected(ids);
      openKumoFor(ids);
    } else if (event.key === " ") {
      event.preventDefault();
      setSelected((s) => (s.includes(entry.id) ? s.filter((n) => n !== entry.id) : [...s, entry.id]));
    } else if (event.key === "Enter") {
      event.preventDefault();
      openFile(entry);
    } else if (event.key === "F2") {
      event.preventDefault();
      setRenaming(entry.id);
    } else if (event.key === "Delete") {
      event.preventDefault();
      remove(ids);
    } else if (event.key === "ContextMenu" || (event.shiftKey && event.key === "F10")) {
      event.preventDefault();
      const r = event.currentTarget.getBoundingClientRect();
      const p = local(r.right, r.top + 20);
      setContext({ x: p.x, y: p.y, target: { type: "file", id: entry.id } });
    }
  };

  useEffect(() => {
    if (menu?.interactive && menu.phase === "menu") segmentRefs.current[0]?.focus();
  }, [menu?.interactive, menu?.phase, menu?.category]);

  const onMenuKeyDown = (event: ReactKeyboardEvent) => {
    if (!menu || menu.phase === "dialog") return;
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
    segmentRefs.current[(current < 0 ? 0 : current + step + items.length) % items.length]?.focus();
  };

  const reset = () => {
    timers.current.forEach(clearTimeout);
    timers.current = [];
    closeMenu();
    setContext(null);
    setRenaming(null);
    setWindows([]);
    setDesktop(initialDesktop());
    setSelected([]);
    setHint("");
  };

  const dragOffset = drag?.moved && !menu ? { x: drag.x - drag.startX, y: drag.y - drag.startY } : null;
  const activeWindow = windows.reduce<WindowState | null>((top, w) => (!top || w.z > top.z ? w : top), null);
  const contextFile = context?.target.type === "file" ? desktop.find((f) => f.id === (context.target as { id: string }).id) : undefined;

  const contextItems: { label: string; action: () => void; separator?: boolean }[] = !context
    ? []
    : context.target.type === "desktop"
      ? [
          { label: "New folder", action: () => newFolder(context.x, context.y) },
          { label: "New text document", action: () => newText(context.x, context.y) },
          { label: "New drawing", action: () => newDrawing(context.x, context.y) },
          { label: "Arrange icons", action: () => setDesktop((d) => arrange(d.map((f) => ({ ...f, x: undefined, y: undefined })), bounds().h)), separator: true },
          { label: "Reset desktop", action: reset },
        ]
      : context.target.type === "child"
        ? [
            { label: "Open", action: () => openWindow((context.target as { child: DemoFile }).child) },
            { label: "Move to desktop", action: () => moveToDesktop((context.target as { folderId: string }).folderId, (context.target as { child: DemoFile }).child) },
          ]
        : contextFile
          ? [
              { label: "Open", action: () => openFile(contextFile) },
              ...(contextFile.kind === "folder" ? [] : [{ label: "Open with Kumo", action: () => openKumoFor(selected.includes(contextFile.id) ? selected : [contextFile.id]) }]),
              ...(contextFile.kind === "png" && contextFile.image?.full.startsWith("data:") ? [{ label: "Edit drawing", action: () => openWindow(contextFile, contextFile.id, "paint") }] : []),
              { label: "Rename", action: () => setRenaming(contextFile.id), separator: true },
              { label: "Delete", action: () => remove(selected.includes(contextFile.id) ? selected : [contextFile.id]) },
            ]
          : [];

  return (
    <div className={`${styles.demo} ${immersive ? styles.immersiveDemo : ""}`}>
      <div className={styles.monitor}>
        <div className={styles.screen}>
          <div
            ref={desktopRef}
            className={styles.desktop}
            onPointerDown={onDesktopPointerDown}
            onPointerMove={onDesktopPointerMove}
            onPointerUp={onDesktopPointerUp}
            onPointerCancel={() => setMarquee(null)}
            onContextMenu={(e) => showContext(e, { type: "desktop" })}
          >
            <Wallpaper />
            <ul className={styles.icons} aria-label="Demo desktop. Enter opens a file, Ctrl+Alt+K opens Kumo, Space selects, F2 renames." data-surface="true">
              {desktop.map((entry, index) => {
                const lifted = dragOffset && drag?.ids.includes(entry.id);
                return (
                  <li
                    key={entry.id}
                    className={`${styles.iconSlot} ${arriving ? (entered ? styles.iconArrive : styles.iconWaiting) : ""}`}
                    style={{
                      animationDelay: arriving ? `${index * 70}ms` : undefined,
                      left: entry.x ?? 0,
                      top: entry.y ?? 0,
                      visibility: entry.x === undefined ? "hidden" : undefined,
                      transform: lifted ? `translate(${dragOffset.x}px, ${dragOffset.y}px)` : undefined,
                      zIndex: lifted ? 3 : undefined,
                    }}
                  >
                    <button
                      ref={(el) => {
                        if (el) iconRefs.current.set(entry.id, el);
                        else iconRefs.current.delete(entry.id);
                      }}
                      type="button"
                      className={`${styles.icon} ${selected.includes(entry.id) ? styles.selected : ""} ${lifted ? styles.lifted : ""} ${entry.fresh ? styles.fresh : ""}`}
                      aria-pressed={selected.includes(entry.id)}
                      aria-label={entry.name}
                      onPointerDown={(e) => onIconPointerDown(e, entry)}
                      onPointerMove={onIconPointerMove}
                      onPointerUp={(e) => onIconPointerUp(e, entry)}
                      onPointerCancel={() => setDrag(null)}
                      onDoubleClick={() => openFile(entry)}
                      onKeyDown={(e) => onIconKeyDown(e, entry)}
                      onContextMenu={(e) => showContext(e, { type: "file", id: entry.id })}
                    >
                      <Thumb file={entry} />
                      {renaming === entry.id ? (
                        <input
                          className={styles.renameInput}
                          autoFocus
                          defaultValue={entry.kind === "folder" ? entry.name : stem(entry.name)}
                          onFocus={(e) => e.currentTarget.select()}
                          onPointerDown={(e) => e.stopPropagation()}
                          onKeyDown={(e) => {
                            e.stopPropagation();
                            if (e.key === "Enter") commitRename(entry.id, e.currentTarget.value);
                            if (e.key === "Escape") setRenaming(null);
                          }}
                          onBlur={(e) => commitRename(entry.id, e.currentTarget.value)}
                          aria-label={`New name for ${entry.name}`}
                        />
                      ) : (
                        <span className={styles.name}>{entry.name}</span>
                      )}
                    </button>
                  </li>
                );
              })}
            </ul>

            {windows.map((win) => {
              const file = windowFile(win);
              return (
                <Window
                  key={win.wid}
                  win={win}
                  title={win.app === "paint" ? `${file.name} — Paint` : file.name}
                  active={activeWindow?.wid === win.wid}
                  onFocus={() => focusWindow(win.wid)}
                  onMove={(x, y) => moveWindow(win.wid, x, y)}
                  onClose={() => closeWindow(win.wid)}
                >
                  {win.app === "paint" ? (
                    <PaintApp onSave={(url, w, h) => saveDrawing(win, url, w, h)} />
                  ) : file.kind === "pdf" ? (
                    <PdfViewer file={file} width={win.w} />
                  ) : file.kind === "txt" ? (
                    <TextEditor file={file} onChange={(text) => win.fileId && updateFile(win.fileId, { text })} />
                  ) : file.kind === "folder" ? (
                    <FolderView
                      file={file}
                      onOpen={(child) => openWindow(child)}
                      onMenu={(child, x, y) => {
                        const p = local(x, y);
                        setContext({ x: p.x, y: p.y, target: { type: "child", folderId: file.id, child } });
                      }}
                    />
                  ) : (
                    <ImageViewer file={file} width={win.w} height={win.h} />
                  )}
                </Window>
              );
            })}

            {marquee && (
              <div
                className={styles.marquee}
                style={{
                  left: Math.min(marquee.startX, marquee.x),
                  top: Math.min(marquee.startY, marquee.y),
                  width: Math.abs(marquee.x - marquee.startX),
                  height: Math.abs(marquee.y - marquee.startY),
                }}
                aria-hidden="true"
              />
            )}

            {drag?.moved && menu && (
              <div className={styles.ghost} style={{ left: drag.x, top: drag.y }} aria-hidden="true">
                <Thumb file={filesById(drag.ids)[0]} />
                {drag.ids.length > 1 && <span className={styles.count}>{drag.ids.length}</span>}
              </div>
            )}

            {context && contextItems.length > 0 && (
              <div className={styles.contextMenu} style={{ left: context.x, top: context.y }} role="menu" onPointerDown={(e) => e.stopPropagation()} onKeyDown={(e) => e.key === "Escape" && setContext(null)}>
                {contextItems.map((item, i) => (
                  <div key={item.label}>
                    {item.separator && i > 0 && <hr />}
                    <button
                      type="button"
                      role="menuitem"
                      autoFocus={i === 0}
                      onClick={() => {
                        setContext(null);
                        item.action();
                      }}
                    >
                      {item.label}
                    </button>
                  </div>
                ))}
              </div>
            )}

            {menu && (
              <div
                className={styles.menu}
                style={{ left: menu.x - SIZE / 2, top: menu.y - SIZE / 2, width: SIZE }}
                role="menu"
                aria-label={`Kumo for ${selectionText(menu.files)}`}
                onKeyDown={onMenuKeyDown}
              >
                <svg width={SIZE} height={SIZE} viewBox={`0 0 ${SIZE} ${SIZE}`} aria-hidden="true" className={styles.ring}>
                  <circle cx={SIZE / 2} cy={SIZE / 2} r={OUTER + 4} className={`${styles.disk} ${menu.phase === "running" || menu.phase === "result" || menu.phase === "unavailable" ? styles.diskHidden : ""}`} />
                  {items.map((item, i) => (
                    <path key={`${menu.category}-${item.label}`} d={sector(i, items.length)} className={`${styles.segment} ${menu.hover === i ? styles.hot : ""}`} />
                  ))}
                  {menu.phase === "running" && <circle cx={SIZE / 2} cy={SIZE / 2} r={HUB + 4} className={styles.progress} pathLength={1} />}
                </svg>

                {items.map((item, i) => {
                  const pos = labelPosition(i, items.length);
                  const icon = !menu.category ? item.icon : null;
                  return (
                    <button
                      key={`${menu.category}-${item.label}`}
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
                      aria-haspopup={item.kind === "category" ? "menu" : undefined}
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
                  onClick={() => (menu.category && menu.phase === "menu" ? goBack() : menu.phase !== "running" && closeMenu())}
                  aria-label={menu.category && menu.phase === "menu" ? "Back" : "Close"}
                >
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img src={`/mascot/${menu.mascot}.png`} alt="" width={64} height={64} />
                </button>

                <div className={styles.status} role="status">
                  <span>{menu.status}</span>
                  {(menu.phase === "unavailable" || menu.phase === "result") && (
                    <button type="button" className={styles.statusAction} onClick={closeMenu}>
                      Close
                    </button>
                  )}
                </div>
              </div>
            )}

            {showTips && entered && (
              <aside className={styles.tips} aria-label="Things to try" onPointerDown={(e) => e.stopPropagation()} onContextMenu={(e) => e.stopPropagation()}>
                <button type="button" className={styles.tipsClose} onClick={() => toggleTips(false)} aria-label="Hide things to try">
                  ×
                </button>
                <p className={styles.tipsTitle}>Things to try</p>
                <ul>
                  {TIPS.map((tip) => (
                    <li key={tip.id} className={tried.includes(tip.id) ? styles.tipDone : undefined}>
                      <span className={styles.tipBox} aria-hidden="true" />
                      <span>
                        {tip.text}
                        {tried.includes(tip.id) && <span className={styles.srOnly}> (done)</span>}
                      </span>
                    </li>
                  ))}
                </ul>
                {tried.length === TIPS.length && (
                  <p className={styles.tipsFinished}>
                    That&apos;s Kumo. It works the same way on your own files. <a href="/download/">Get it for Windows</a>
                  </p>
                )}
              </aside>
            )}

            {welcome && (
              <div className={styles.toast} role="status">
                <div className={styles.toastHead}>
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img src="/mascot/idle.png" alt="" width={14} height={14} />
                  <span>Kumo</span>
                  <button type="button" onClick={() => setWelcome(false)} aria-label="Dismiss notification">
                    ×
                  </button>
                </div>
                <div className={styles.toastBody}>
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img src="/mascot/success.png" alt="" width={44} height={44} />
                  <div>
                    <strong>Kumo is ready</strong>
                    <p>
                      <span className={styles.pointerOnly}>Drag files and tap Shift to open Kumo.</span>
                      <span className={styles.touchOnly}>Tap a file to open Kumo.</span>
                    </p>
                  </div>
                </div>
              </div>
            )}

            {menu?.phase === "dialog" && menu.dialog && (
              <div className={styles.scrim}>
                {menu.dialog.requirement === "crop" && <CropEditor file={menu.files[0]} onCancel={cancelDialog} onCrop={(crop) => run(menu.dialog!.entry, { crop })} />}
                {menu.dialog.requirement === "pages" && (
                  <PagesDialog fileCount={menu.files.length} onCancel={cancelDialog} onConfirm={(pages) => run(menu.dialog!.entry, { pages })} />
                )}
                {menu.dialog.requirement === "password" && <PasswordDialog onCancel={cancelDialog} onConfirm={(password) => run(menu.dialog!.entry, { password })} />}
              </div>
            )}
          </div>
          <Taskbar windows={windows} active={activeWindow?.wid} titleOf={(w) => windowFile(w).name} onSelect={focusWindow} />
        </div>
      </div>
      <div className={styles.stand} aria-hidden="true" />

      <div className={styles.bar}>
        <p className={styles.hint} aria-live="polite">
          {hint || (
            <>
              <span className={styles.pointerOnly}>
                Drag files and tap <kbd>Shift</kbd> for Kumo. Double-click to open, right-click for new files.
              </span>
              <span className={styles.touchOnly}>Tap a file to open Kumo.</span>
            </>
          )}
        </p>
        <span className={styles.barButtons}>
          {!showTips && (
            <button type="button" className={styles.reset} onClick={() => toggleTips(true)}>
              Things to try
            </button>
          )}
          <button type="button" className={styles.reset} onClick={reset}>
            Reset
          </button>
        </span>
      </div>
    </div>
  );
}

export function Wallpaper() {
  return (
    <svg className={styles.wallpaper} viewBox="0 0 1000 460" preserveAspectRatio="xMidYMax slice" aria-hidden="true" focusable="false">
      <circle className={styles.wallSun} cx="780" cy="250" r="46" />
      <path className={styles.wallBack} d="M0 300C140 250 260 262 400 290s300-70 440-40 120 40 160 30V460H0Z" />
      <path className={styles.wallMid} d="M0 350c160-40 330-30 500-4s330-50 500-20V460H0Z" />
      <path className={styles.wallFront} d="M0 400c200-26 420-16 620 4s280-14 380-8V460H0Z" />
    </svg>
  );
}

/** A Windows-style taskbar: open windows, Kumo in the notification area and the visitor's own clock. */
function Taskbar({ windows, active, titleOf, onSelect }: { windows: WindowState[]; active?: string; titleOf: (w: WindowState) => string; onSelect: (wid: string) => void }) {
  const [time, setTime] = useState("");
  useEffect(() => {
    const tick = () => setTime(new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }));
    tick();
    const id = window.setInterval(tick, 20_000);
    return () => clearInterval(id);
  }, []);
  return (
    <div className={styles.taskbar}>
      <div className={styles.taskApps}>
        <span className={styles.startButton} aria-hidden="true">
          <i />
          <i />
          <i />
          <i />
        </span>
        <span className={styles.search} aria-hidden="true">
          Search
        </span>
        <span className={styles.folderApp} aria-hidden="true" />
        {windows.map((w) => (
          <button key={w.wid} type="button" className={`${styles.taskWindow} ${active === w.wid ? styles.taskWindowOn : ""}`} onClick={() => onSelect(w.wid)} title={titleOf(w)} aria-label={`Show ${titleOf(w)}`}>
            {w.app === "paint" ? <span className={styles.paintIcon} aria-hidden="true" /> : <Thumb file={w.file} size={20} />}
          </button>
        ))}
      </div>
      <div className={styles.tray} aria-hidden="true">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img src="/mascot/idle.png" alt="" width={18} height={18} />
        <span>{time}</span>
      </div>
    </div>
  );
}

function Glyph({ icon }: { icon: Category }) {
  const paths: Record<Category, string> = {
    Convert: "M4 7h13l-3-3M20 17H7l3 3",
    Crop: "M7 3v14h14M3 7h14v14",
    Compress: "M12 3v6m0 0-3-3m3 3 3-3M12 21v-6m0 0-3 3m3-3 3 3M5 12h14",
    Rotate: "M20 12a8 8 0 1 1-2.3-5.7M20 4v5h-5",
    Merge: "M5 4v5a5 5 0 0 0 5 5h9m0 0-3-3m3 3-3 3M5 20v-6",
    Split: "M6 3h8l4 4v5M6 3v18h6M15 15l6 6M21 15l-6 6",
    Extract: "M6 3h8l4 4v14H6zM10 13h4M10 17h4",
    Protect: "M7 11V8a5 5 0 0 1 10 0v3M5 11h14v10H5z",
  };
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" className={styles.glyph}>
      <path d={paths[icon]} />
    </svg>
  );
}

function PagesDialog({ fileCount, onCancel, onConfirm }: { fileCount: number; onCancel: () => void; onConfirm: (pages: string) => void }) {
  const [value, setValue] = useState("");
  const [error, setError] = useState("");
  const submit = () => {
    const problem = checkPages(value);
    if (problem) setError(problem);
    else onConfirm(value.trim());
  };
  return (
    <form
      className={styles.dialog}
      role="dialog"
      aria-modal="true"
      aria-labelledby="pages-title"
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
      onKeyDown={(e) => e.key === "Escape" && onCancel()}
    >
      <h3 id="pages-title">Extract pages</h3>
      <label htmlFor="pages-input">{fileCount === 1 ? "Which pages do you want to keep?" : `Which pages do you want to keep from each of the ${fileCount} PDFs?`}</label>
      <input id="pages-input" autoFocus value={value} placeholder="1-3, 5" onChange={(e) => setValue(e.target.value)} aria-invalid={!!error} aria-describedby="pages-error" />
      <p id="pages-error" className={styles.dialogError}>
        {error}
      </p>
      <div className={styles.dialogActions}>
        <button type="submit" className={styles.primary}>
          Extract
        </button>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  );
}

function PasswordDialog({ onCancel, onConfirm }: { onCancel: () => void; onConfirm: (password: string) => void }) {
  const [password, setPassword] = useState("");
  const [repeat, setRepeat] = useState("");
  const [error, setError] = useState("");
  const submit = () => {
    if (password.length < MINIMUM_PASSWORD_LENGTH) setError(`Use at least ${MINIMUM_PASSWORD_LENGTH} characters.`);
    else if (password !== repeat) setError("The passwords don't match.");
    else onConfirm(password);
  };
  return (
    <form
      className={styles.dialog}
      role="dialog"
      aria-modal="true"
      aria-labelledby="password-title"
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
      onKeyDown={(e) => e.key === "Escape" && onCancel()}
    >
      <h3 id="password-title">Protect PDF</h3>
      <label htmlFor="password-input">Choose a password for the protected copy.</label>
      <input id="password-input" type="password" autoFocus autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} />
      <label htmlFor="password-repeat">Repeat the password</label>
      <input id="password-repeat" type="password" autoComplete="new-password" value={repeat} onChange={(e) => setRepeat(e.target.value)} aria-describedby="password-error" />
      <p id="password-error" className={styles.dialogError}>
        {error}
      </p>
      <div className={styles.dialogActions}>
        <button type="submit" className={styles.primary}>
          Protect
        </button>
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  );
}
