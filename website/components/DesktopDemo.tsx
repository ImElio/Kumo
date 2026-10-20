"use client";

import { useCallback, useEffect, useRef, useState, type KeyboardEvent as ReactKeyboardEvent, type PointerEvent as ReactPointerEvent } from "react";
import {
  categoryItems,
  checkPages,
  entriesFor,
  execute,
  initialDesktop,
  MINIMUM_PASSWORD_LENGTH,
  progressText,
  rootItems,
  selectionText,
  unavailableText,
  type Category,
  type DemoFile,
  type MenuEntry,
  type Options,
  type RadialItem,
} from "@/lib/kumo";
import CropEditor from "./CropEditor";
import styles from "./DesktopDemo.module.css";

const OUTER = 118;
const HUB = 44;
const SIZE = (OUTER + 8) * 2;
const DRAG_THRESHOLD = 5;
const TAP_MS = 450;
const CATEGORY_DWELL = 350;
const BACK_DWELL = 500;
const STEP_MS = 420;

type Drag = { files: DemoFile[]; startX: number; startY: number; x: number; y: number; moved: boolean };
type Marquee = { startX: number; startY: number; x: number; y: number; additive: boolean; base: string[] };
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

export default function DesktopDemo() {
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
  const [hint, setHint] = useState("");
  const shiftDownAt = useRef<number | null>(null);
  const dwell = useRef<number | null>(null);
  const timers = useRef<number[]>([]);

  const items: RadialItem[] = !menu || menu.phase !== "menu" ? [] : menu.category ? categoryItems(menu.entries, menu.category) : rootItems(menu.entries);

  const later = useCallback((fn: () => void, ms: number) => {
    timers.current.push(window.setTimeout(fn, ms));
  }, []);

  useEffect(() => () => timers.current.forEach(clearTimeout), []);

  const cancelDwell = () => {
    if (dwell.current) clearTimeout(dwell.current);
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
      y: Math.min(Math.max(y, half), Math.max(half, rect.height - half - 44)),
    };
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
    if (entries.length === 0) {
      setMenu({
        files,
        entries,
        ...center,
        category: null,
        interactive: true,
        hover: null,
        phase: "unavailable",
        status: unavailableText(files, dropped.length > files.length),
        mascot: "cancelled",
      });
      return;
    }
    setMenu({ files, entries, ...center, category: null, interactive, hover: null, phase: "menu", status: selectionText(files), mascot: interactive ? "idle" : "dragging" });
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

  const filesByName = (names: string[]) => desktop.filter((f) => names.includes(f.name));

  // Pointer capture keeps the drag going over the menu and outside the desktop.
  const onIconPointerDown = (event: ReactPointerEvent, entry: DemoFile) => {
    if (event.button !== 0 || menu?.phase === "running" || menu?.phase === "dialog") return;
    if (menu) closeMenu();
    event.stopPropagation();
    const additive = event.ctrlKey || event.metaKey;
    let selection = selected;
    if (additive) selection = selected.includes(entry.name) ? selected.filter((n) => n !== entry.name) : [...selected, entry.name];
    else if (!selected.includes(entry.name)) selection = [entry.name];
    setSelected(selection);
    if (event.pointerType !== "mouse" || additive || !selection.includes(entry.name)) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    const p = local(event.clientX, event.clientY);
    setDrag({ files: filesByName(selection), startX: p.x, startY: p.y, x: p.x, y: p.y, moved: false });
  };

  const onIconPointerMove = (event: ReactPointerEvent) => {
    if (!drag) return;
    const p = local(event.clientX, event.clientY);
    const moved = drag.moved || Math.hypot(p.x - drag.startX, p.y - drag.startY) > DRAG_THRESHOLD;
    setDrag({ ...drag, x: p.x, y: p.y, moved });
    if (menu && !menu.interactive && menu.phase === "menu") updateHover(hitTest(p.x - menu.x, p.y - menu.y, items.length));
    if (moved && !drag.moved && !menu) setHint("Now tap Shift");
  };

  const onIconPointerUp = (event: ReactPointerEvent, entry: DemoFile) => {
    const current = drag;
    setDrag(null);
    shiftDownAt.current = null;
    if (event.pointerType !== "mouse") {
      const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
      const p = local(rect.left + rect.width / 2, rect.top + rect.height / 2);
      openMenu(filesByName(selected.includes(entry.name) ? selected : [entry.name]), p.x, p.y, true);
      return;
    }
    if (!current?.moved) {
      if (!(event.ctrlKey || event.metaKey)) setSelected([entry.name]);
      setHint("");
      return;
    }
    if (!menu || menu.interactive) {
      setHint(menu ? "" : "Tap Shift while you drag to open Kumo");
      return;
    }
    const p = local(event.clientX, event.clientY);
    const target = menu.phase === "menu" ? hitTest(p.x - menu.x, p.y - menu.y, items.length) : null;
    cancelDwell();
    if (menu.phase !== "menu") {
      setMenu((m) => m && { ...m, interactive: true });
      return;
    }
    if (target === null) closeMenu();
    else if (target === "hub") setMenu((m) => m && { ...m, interactive: true, mascot: "idle", hover: null });
    else activate(target, true);
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
      if (quick && !menu) openMenu(drag.files, drag.x, drag.y, false);
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
    setMarquee({ startX: p.x, startY: p.y, x: p.x, y: p.y, additive, base: additive ? selected : [] });
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

  const selectInside = (next: Marquee) => {
    const origin = desktopRef.current!.getBoundingClientRect();
    const box = {
      left: origin.left + Math.min(next.startX, next.x),
      right: origin.left + Math.max(next.startX, next.x),
      top: origin.top + Math.min(next.startY, next.y),
      bottom: origin.top + Math.max(next.startY, next.y),
    };
    const inside = [...iconRefs.current.entries()].filter(([, el]) => intersects(el.getBoundingClientRect(), box)).map(([name]) => name);
    setSelected([...new Set([...next.base, ...inside])]);
  };

  const onIconKeyDown = (event: ReactKeyboardEvent, entry: DemoFile) => {
    const shortcut = event.ctrlKey && event.altKey && event.key.toLowerCase() === "k";
    if (event.key === " ") {
      event.preventDefault();
      setSelected((s) => (s.includes(entry.name) ? s.filter((n) => n !== entry.name) : [...s, entry.name]));
      return;
    }
    if (event.key === "Enter" || shortcut) {
      event.preventDefault();
      const names = selected.includes(entry.name) ? selected : [entry.name];
      setSelected(names);
      const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
      const p = local(rect.left + rect.width / 2, rect.top + rect.height / 2);
      openMenu(filesByName(names), p.x + SIZE / 2, p.y, true);
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
    setDesktop(initialDesktop());
    setSelected([]);
    setHint("");
  };

  const dragging = drag?.moved ? drag : null;
  const changed = desktop.length !== initialDesktop().length;

  return (
    <div className={styles.demo}>
      <div className={styles.monitor}>
        <div className={styles.screen}>
          <div
            ref={desktopRef}
            className={styles.desktop}
            onPointerDown={onDesktopPointerDown}
            onPointerMove={onDesktopPointerMove}
            onPointerUp={onDesktopPointerUp}
            onPointerCancel={() => setMarquee(null)}
          >
            <Wallpaper />
            <ul className={styles.icons} aria-label="Demo desktop. Select files with Space, then press Enter to open Kumo." data-surface="true">
              {desktop.map((entry) => (
                <li key={entry.name}>
                  <button
                    ref={(el) => {
                      if (el) iconRefs.current.set(entry.name, el);
                      else iconRefs.current.delete(entry.name);
                    }}
                    type="button"
                    className={`${styles.icon} ${selected.includes(entry.name) ? styles.selected : ""} ${dragging?.files.some((f) => f.name === entry.name) ? styles.lifted : ""} ${entry.fresh ? styles.fresh : ""}`}
                    aria-pressed={selected.includes(entry.name)}
                    aria-label={entry.name}
                    onPointerDown={(e) => onIconPointerDown(e, entry)}
                    onPointerMove={onIconPointerMove}
                    onPointerUp={(e) => onIconPointerUp(e, entry)}
                    onPointerCancel={() => setDrag(null)}
                    onKeyDown={(e) => onIconKeyDown(e, entry)}
                  >
                    <Thumb file={entry} />
                    <span className={styles.name}>{entry.name}</span>
                  </button>
                </li>
              ))}
            </ul>

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

            {dragging && (
              <div className={styles.ghost} style={{ left: dragging.x, top: dragging.y }} aria-hidden="true">
                <Thumb file={dragging.files[0]} />
                {dragging.files.length > 1 && <span className={styles.count}>{dragging.files.length}</span>}
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

            {menu?.phase === "dialog" && menu.dialog && (
              <div className={styles.scrim}>
                {menu.dialog.requirement === "crop" && (
                  <CropEditor file={menu.files[0]} onCancel={cancelDialog} onCrop={(crop) => run(menu.dialog!.entry, { crop })} />
                )}
                {menu.dialog.requirement === "pages" && (
                  <PagesDialog fileCount={menu.files.length} onCancel={cancelDialog} onConfirm={(pages) => run(menu.dialog!.entry, { pages })} />
                )}
                {menu.dialog.requirement === "password" && (
                  <PasswordDialog onCancel={cancelDialog} onConfirm={(password) => run(menu.dialog!.entry, { password })} />
                )}
              </div>
            )}
          </div>
          <Taskbar />
        </div>
      </div>
      <div className={styles.stand} aria-hidden="true" />

      <div className={styles.bar}>
        <p className={styles.hint} aria-live="polite">
          {hint || (
            <>
              <span className={styles.pointerOnly}>
                Drag files and tap <kbd>Shift</kbd> while you hold them. <kbd>Ctrl</kbd>-click or draw a box to pick several.
              </span>
              <span className={styles.touchOnly}>Tap a file to open Kumo.</span>
            </>
          )}
        </p>
        <button type="button" className={styles.reset} onClick={reset} disabled={!changed && !menu}>
          Reset
        </button>
      </div>
    </div>
  );
}

function Wallpaper() {
  return (
    <svg className={styles.wallpaper} viewBox="0 0 1000 460" preserveAspectRatio="xMidYMax slice" aria-hidden="true" focusable="false">
      <circle className={styles.wallSun} cx="780" cy="250" r="46" />
      <path className={styles.wallBack} d="M0 300C140 250 260 262 400 290s300-70 440-40 120 40 160 30V460H0Z" />
      <path className={styles.wallMid} d="M0 350c160-40 330-30 500-4s330-50 500-20V460H0Z" />
      <path className={styles.wallFront} d="M0 400c200-26 420-16 620 4s280-14 380-8V460H0Z" />
    </svg>
  );
}

/** A Windows-style taskbar with generic icons, Kumo in the notification area and the visitor's own clock. */
function Taskbar() {
  const [time, setTime] = useState("");
  useEffect(() => {
    const tick = () => setTime(new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }));
    tick();
    const id = window.setInterval(tick, 20_000);
    return () => clearInterval(id);
  }, []);
  return (
    <div className={styles.taskbar} aria-hidden="true">
      <div className={styles.taskApps}>
        <span className={styles.startButton}>
          <i />
          <i />
          <i />
          <i />
        </span>
        <span className={styles.search}>Search</span>
        <span className={styles.folderApp} />
      </div>
      <div className={styles.tray}>
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img src="/mascot/idle.png" alt="" width={18} height={18} />
        <span>{time}</span>
      </div>
    </div>
  );
}

function Thumb({ file }: { file: DemoFile }) {
  if (file.kind === "folder") return <span className={styles.folder} aria-hidden="true" />;
  if (file.kind === "pdf") return <span className={styles.pdf} aria-hidden="true">PDF</span>;
  if (!file.image) return <span className={styles.picture} aria-hidden="true" />;
  // eslint-disable-next-line @next/next/no-img-element
  return <img className={styles.thumb} src={file.image.thumb} alt="" width={48} height={48} draggable={false} />;
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
      <p id="pages-error" className={styles.dialogError}>{error}</p>
      <div className={styles.dialogActions}>
        <button type="submit" className={styles.primary}>Extract</button>
        <button type="button" onClick={onCancel}>Cancel</button>
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
      <p id="password-error" className={styles.dialogError}>{error}</p>
      <div className={styles.dialogActions}>
        <button type="submit" className={styles.primary}>Protect</button>
        <button type="button" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
