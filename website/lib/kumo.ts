// A port of the parts of Kumo that decide what the radial menu offers and what an action produces:
// src/Kumo.Core/Operations (OperationRegistry, Summaries, PageRange, OutputNaming), src/Kumo.Core/Radial/RadialMenuModel
// and src/Kumo.Operations. Nothing is converted; the demo only creates the files Kumo would write.

export type Kind = "png" | "jpeg" | "webp" | "bmp" | "gif" | "pdf" | "txt";
export type Category = "Convert" | "Crop" | "Compress" | "Rotate" | "Merge" | "Split" | "Extract" | "Protect";
export type Requirement = "none" | "crop" | "pages" | "password";

export type CropRegion = { x: number; y: number; width: number; height: number };

/** How an edited picture is shown: a crop of the source picture, then a clockwise rotation. */
export type ImageView = { rotation: number; crop: CropRegion };

export type DemoFile = {
  id: string;
  name: string;
  kind: Kind | "folder";
  /** Page count of a PDF. */
  pages?: number;
  /** The pictures on the pages of a PDF made from images, in page order. */
  pageImages?: (DemoFile | null)[];
  /** Picture used for the icon and the viewers, and its size in pixels as it is now (after crops and rotations). */
  image?: { thumb: string; full: string; width: number; height: number };
  view?: ImageView;
  text?: string;
  password?: string;
  children?: DemoFile[];
  /** Position on the desktop, in pixels. */
  x?: number;
  y?: number;
  fresh?: boolean;
};
export type Options = { crop?: CropRegion; pages?: string; password?: string };

type Outcome = { outputs: DemoFile[]; failure?: string; note?: string };

type Operation = {
  category: Category;
  label: string;
  description: string;
  requirement: Requirement;
  combined: boolean;
  minimumFiles: number;
  supports: (file: DemoFile) => boolean;
  run: (files: DemoFile[], options: Options) => Outcome;
};

export type MenuEntry = { category: Category; label: string; description: string; requirement: Requirement; combined: boolean; operations: Operation[] };

const CATEGORY_ORDER: Category[] = ["Convert", "Crop", "Compress", "Rotate", "Merge", "Split", "Extract", "Protect"];

const extension: Record<Kind, string> = { png: ".png", jpeg: ".jpg", webp: ".webp", bmp: ".bmp", gif: ".gif", pdf: ".pdf", txt: ".txt" };
export const displayName = (kind: Kind) => (kind === "jpeg" ? "JPG" : kind.toUpperCase());

const decodable = (f: DemoFile) => f.kind === "png" || f.kind === "jpeg" || f.kind === "webp" || f.kind === "bmp" || f.kind === "gif";
const isPdf = (f: DemoFile) => f.kind === "pdf";
const sameFormat = (kind: Kind): Kind => (kind === "gif" ? "png" : kind);
let counter = 0;
export const newId = () => `f${++counter}`;

const pageNumber = (page: number, count: number) => String(page).padStart(Math.max(3, String(count).length), "0");

export const stem = (name: string) => (name.includes(".") ? name.slice(0, name.lastIndexOf(".")) : name);
export const kindOf = (name: string): Kind | "folder" => {
  const ext = name.includes(".") ? name.slice(name.lastIndexOf(".")).toLowerCase() : "";
  const found = (Object.keys(extension) as Kind[]).find((k) => extension[k] === ext || (k === "jpeg" && ext === ".jpeg"));
  return found ?? "folder";
};

const file = (source: DemoFile, name: string, kind: Kind, extra: Partial<DemoFile> = {}): DemoFile => ({
  id: newId(),
  name,
  kind,
  image: kind === "pdf" ? undefined : source.image,
  view: kind === "pdf" ? undefined : source.view,
  pages: kind === "pdf" ? (source.pages ?? 1) : undefined,
  pageImages: kind === "pdf" ? source.pageImages : undefined,
  ...extra,
});
export const folder = (name: string, children: DemoFile[] = []): DemoFile => ({ id: newId(), name, kind: "folder", children });

const pagePicture = (pdf: DemoFile, index: number) => pdf.pageImages?.[index] ?? null;

/** The view after rotating clockwise by a quarter turn or more. */
function rotated(f: DemoFile, degrees: number): Partial<DemoFile> {
  if (!f.image) return {};
  const quarter = degrees % 180 !== 0;
  const view = f.view ?? { rotation: 0, crop: { x: 0, y: 0, width: 1, height: 1 } };
  return {
    image: quarter ? { ...f.image, width: f.image.height, height: f.image.width } : f.image,
    view: { ...view, rotation: (view.rotation + degrees) % 360 },
  };
}

/** The view after cropping a region of what is shown now, mapped back onto the source picture. */
function cropped(f: DemoFile, r: CropRegion, width: number, height: number): Partial<DemoFile> {
  const view = f.view ?? { rotation: 0, crop: { x: 0, y: 0, width: 1, height: 1 } };
  const u0 = r.x, u1 = r.x + r.width, v0 = r.y, v1 = r.y + r.height;
  const box =
    view.rotation === 90 ? { x: v0, y: 1 - u1, width: v1 - v0, height: u1 - u0 }
    : view.rotation === 180 ? { x: 1 - u1, y: 1 - v1, width: u1 - u0, height: v1 - v0 }
    : view.rotation === 270 ? { x: 1 - v1, y: u0, width: v1 - v0, height: u1 - u0 }
    : r;
  const c = view.crop;
  return {
    image: { ...f.image!, width, height },
    view: { rotation: view.rotation, crop: { x: c.x + box.x * c.width, y: c.y + box.y * c.height, width: box.width * c.width, height: box.height * c.height } },
  };
}

const perFile = (fn: (f: DemoFile, options: Options) => Outcome) => (files: DemoFile[], options: Options) => {
  const results = files.map((f) => fn(f, options));
  return {
    outputs: results.flatMap((r) => r.outputs),
    failure: results.find((r) => r.failure)?.failure,
    note: results.find((r) => r.note)?.note,
    failed: results.filter((r) => r.failure).length,
  } as Outcome & { failed: number };
};

const convert = (target: Exclude<Kind, "pdf" | "gif">): Operation => ({
  category: "Convert",
  label: displayName(target),
  description: `Convert to ${displayName(target)}`,
  requirement: "none",
  combined: false,
  minimumFiles: 1,
  supports: (f) => decodable(f) && f.kind !== target,
  run: perFile((f) => ({ outputs: [file(f, `${stem(f.name)}${extension[target]}`, target)] })),
});

const render = (target: "png" | "jpeg"): Operation => ({
  category: "Convert",
  label: displayName(target),
  description: `Save pages as ${displayName(target)} images`,
  requirement: "none",
  combined: false,
  minimumFiles: 1,
  supports: isPdf,
  run: perFile((f) => {
    const count = f.pages ?? 1;
    const page = (i: number, name: string) => {
      const picture = pagePicture(f, i);
      return file(f, name, target, { image: picture?.image, view: picture?.view });
    };
    return {
      outputs: [
        count === 1
          ? page(0, `${stem(f.name)}${extension[target]}`)
          : folder(`${stem(f.name)} (images)`, Array.from({ length: count }, (_, i) => page(i, `${stem(f.name)} - page ${pageNumber(i + 1, count)}${extension[target]}`))),
      ],
    };
  }),
});

const compress = (label: string, description: string, lossless: boolean): Operation => ({
  category: "Compress",
  label,
  description,
  requirement: "none",
  combined: false,
  minimumFiles: 1,
  supports: (f) => (lossless ? f.kind === "png" : f.kind === "jpeg" || f.kind === "webp"),
  run: perFile((f) => ({ outputs: [file(f, `${stem(f.name)} ${lossless ? "(optimized)" : "(compressed)"}${extension[f.kind as Kind]}`, f.kind as Kind)] })),
});

const rotate = (degrees: 90 | 270 | 180): Operation => ({
  category: "Rotate",
  label: degrees === 90 ? "90° right" : degrees === 270 ? "90° left" : `${degrees}°`,
  description: degrees === 90 ? "Rotate 90° clockwise" : degrees === 270 ? "Rotate 90° counterclockwise" : `Rotate ${degrees}°`,
  requirement: "none",
  combined: false,
  minimumFiles: 1,
  supports: decodable,
  run: perFile((f) => {
    const kind = sameFormat(f.kind as Kind);
    return { outputs: [file(f, `${stem(f.name)} (rotated)${extension[kind]}`, kind, rotated(f, degrees))] };
  }),
});

const naturalOrder = (files: DemoFile[]) =>
  [...files].sort((a, b) => a.name.localeCompare(b.name, "en", { numeric: true, sensitivity: "base" }));

const operations: Operation[] = [
  convert("png"),
  convert("jpeg"),
  convert("webp"),
  convert("bmp"),
  {
    category: "Convert",
    label: "PDF",
    description: "Convert to PDF",
    requirement: "none",
    combined: false,
    minimumFiles: 1,
    supports: decodable,
    run: perFile((f) => ({ outputs: [file(f, `${stem(f.name)}.pdf`, "pdf", { pages: 1, pageImages: [f] })] })),
  },
  render("png"),
  render("jpeg"),
  {
    category: "Crop",
    label: "Crop",
    description: "Crop…",
    requirement: "crop",
    combined: false,
    minimumFiles: 1,
    supports: decodable,
    run: perFile((f, { crop }) => {
      const w = f.image?.width ?? 1;
      const h = f.image?.height ?? 1;
      const px = Math.max(1, Math.round(crop!.width * w));
      const py = Math.max(1, Math.round(crop!.height * h));
      if (px >= w && py >= h) return { outputs: [], note: `The crop area covers all of ${f.name}.` };
      const kind = sameFormat(f.kind as Kind);
      return { outputs: [file(f, `${stem(f.name)} (cropped ${px}×${py})${extension[kind]}`, kind, cropped(f, crop!, px, py))] };
    }),
  },
  compress("High quality", "Compress (high quality)", false),
  compress("Balanced", "Compress (balanced)", false),
  compress("Smallest", "Compress (smallest)", false),
  compress("Lossless", "Optimize PNG without quality loss", true),
  rotate(90),
  rotate(270),
  rotate(180),
  {
    category: "Merge",
    label: "Merge PDFs",
    description: "Merge into one PDF (sorted by name)",
    requirement: "none",
    combined: true,
    minimumFiles: 2,
    supports: isPdf,
    run: (files) => {
      const first = naturalOrder(files)[0];
      const ordered = naturalOrder(files);
      const pageImages = ordered.flatMap((f) => Array.from({ length: f.pages ?? 1 }, (_, i) => pagePicture(f, i)));
      return { outputs: [file(first, `${stem(first.name)} (merged).pdf`, "pdf", { pages: pageImages.length, pageImages })] };
    },
  },
  {
    category: "Merge",
    label: "Combine into PDF",
    description: "Combine images into one PDF (sorted by name)",
    requirement: "none",
    combined: true,
    minimumFiles: 2,
    supports: decodable,
    run: (files) => {
      const first = naturalOrder(files)[0];
      return { outputs: [file(first, `${stem(first.name)} (combined).pdf`, "pdf", { pages: files.length, pageImages: naturalOrder(files) })] };
    },
  },
  {
    category: "Split",
    label: "Every page",
    description: "Split into one PDF per page",
    requirement: "none",
    combined: false,
    minimumFiles: 1,
    supports: isPdf,
    run: perFile((f) => {
      const count = f.pages ?? 1;
      if (count === 1) return { outputs: [], note: `${f.name} has only one page.` };
      const pages = Array.from({ length: count }, (_, i) =>
        file(f, `${stem(f.name)} - page ${pageNumber(i + 1, count)}.pdf`, "pdf", { pages: 1, pageImages: [pagePicture(f, i)] }));
      return { outputs: [folder(`${stem(f.name)} (pages)`, pages)] };
    }),
  },
  {
    category: "Extract",
    label: "Pages…",
    description: "Extract selected pages",
    requirement: "pages",
    combined: false,
    minimumFiles: 1,
    supports: isPdf,
    run: perFile((f, { pages }) => {
      const parsed = parsePages(pages ?? "", f.pages ?? 1);
      if ("error" in parsed) return { outputs: [], failure: parsed.error };
      const range = (pages ?? "").replace(/ /g, "").slice(0, 40);
      return { outputs: [file(f, `${stem(f.name)} (pages ${range}).pdf`, "pdf", { pages: parsed.pages.length, pageImages: parsed.pages.map((p) => pagePicture(f, p - 1)) })] };
    }),
  },
  {
    category: "Protect",
    label: "Password…",
    description: "Protect with a password",
    requirement: "password",
    combined: false,
    minimumFiles: 1,
    supports: isPdf,
    run: perFile((f, { password }) => ({ outputs: [file(f, `${stem(f.name)} (protected).pdf`, "pdf", { password })] })),
  },
];

/** OperationRegistry.EntriesFor: combined actions for the whole selection, and per-file actions every file supports. */
export function entriesFor(files: DemoFile[]): MenuEntry[] {
  // OperationRegistry: a file Kumo cannot read means no action for the whole selection.
  if (files.length === 0 || files.some((f) => f.kind === "txt")) return [];
  const entries: (MenuEntry & { order: number })[] = [];
  operations.forEach((op, index) => {
    if (op.combined && files.every(op.supports) && files.length >= op.minimumFiles)
      entries.push({ ...pick(op), combined: true, operations: [op], order: index });
  });
  const groups = new Map<string, Operation[]>();
  for (const op of operations.filter((o) => !o.combined)) {
    const key = `${op.category}|${op.label}`;
    groups.set(key, [...(groups.get(key) ?? []), op]);
  }
  for (const group of groups.values()) {
    if (!files.every((f) => group.some((o) => o.supports(f)))) continue;
    const used = group.filter((o) => files.some(o.supports));
    entries.push({ ...pick(group[0]), combined: false, operations: used, order: Math.min(...used.map((o) => operations.indexOf(o))) });
  }
  return entries
    .sort((a, b) => CATEGORY_ORDER.indexOf(a.category) - CATEGORY_ORDER.indexOf(b.category) || a.order - b.order)
    .map(({ order: _order, ...entry }) => entry);
}

const pick = (op: Operation) => ({ category: op.category, label: op.label, description: op.description, requirement: op.requirement });

export type RadialItem =
  | { kind: "action"; label: string; description: string; entry: MenuEntry; icon: Category | null }
  | { kind: "category"; label: string; description: string; category: Category; icon: Category };

const categoryDescription: Record<Category, string> = {
  Convert: "Convert to another format",
  Crop: "Crop images",
  Compress: "Make files smaller",
  Rotate: "Rotate images",
  Merge: "Combine files",
  Split: "Split into separate files",
  Extract: "Extract part of a file",
  Protect: "Protect with a password",
};

/** RadialMenuModel: categories with one entry become that action directly. */
export function rootItems(entries: MenuEntry[]): RadialItem[] {
  return CATEGORY_ORDER.filter((c) => entries.some((e) => e.category === c)).map((category) => {
    const inCategory = entries.filter((e) => e.category === category);
    if (inCategory.length === 1) {
      const entry = inCategory[0];
      const label = category === "Convert" ? `To ${entry.label}` : category === "Merge" ? entry.label : category;
      return { kind: "action", label, description: entry.description, entry, icon: category };
    }
    return { kind: "category", label: category, description: categoryDescription[category], category, icon: category };
  });
}

export function categoryItems(entries: MenuEntry[], category: Category): RadialItem[] {
  return entries.filter((e) => e.category === category).map((entry) => ({ kind: "action", label: entry.label, description: entry.description, entry, icon: null }));
}

export function selectionText(files: DemoFile[]) {
  if (files.length === 0) return "No files";
  if (files.every(decodable)) return files.length === 1 ? "1 image" : `${files.length} images`;
  if (files.every(isPdf)) return files.length === 1 ? "1 PDF" : `${files.length} PDFs`;
  return countFiles(files.length);
}

export function unavailableText(files: DemoFile[], includedFolders: boolean) {
  if (files.length === 0) return includedFolders ? "Kumo works with files, not folders." : "Kumo couldn't read these files.";
  const unknown = files.find((f) => f.kind === "txt");
  if (unknown) return `Kumo doesn't support ${unknown.name.slice(unknown.name.lastIndexOf(".") + 1).toUpperCase()} files yet.`;
  return new Set(files.map((f) => f.kind)).size > 1
    ? "These files have no action in common. Try them separately."
    : `There's nothing Kumo can do with ${displayName(files[0].kind as Kind)} files yet.`;
}

const verbs: Record<Category, string> = {
  Convert: "Converting",
  Crop: "Cropping",
  Compress: "Compressing",
  Rotate: "Rotating",
  Merge: "Combining",
  Split: "Splitting",
  Extract: "Extracting",
  Protect: "Protecting",
};

export const progressText = (entry: MenuEntry, completed: number, total: number) =>
  total <= 1 ? `${verbs[entry.category]}…` : `${verbs[entry.category]} ${Math.min(completed + 1, total)} of ${total}…`;

export const countFiles = (n: number) => (n === 1 ? "1 file" : `${n} files`);

export type Result = { headline: string; detail?: string; tone: "success" | "nothing" | "partial" | "failure"; outputs: DemoFile[] };

/** Runs an entry and names its outputs the way OutputWriter does: an existing name gets " (1)", " (2)" and so on. */
export function execute(entry: MenuEntry, files: DemoFile[], options: Options, existing: string[]): Result {
  const taken = new Set(existing.map((n) => n.toLowerCase()));
  const outcomes: (Outcome & { failed?: number })[] = entry.combined
    ? [entry.operations[0].run(files, options)]
    : files.map((f) => entry.operations.find((o) => o.supports(f))!.run([f], options));

  const outputs: DemoFile[] = [];
  for (const output of outcomes.flatMap((o) => o.outputs)) {
    let attempt = 0;
    let name = output.name;
    while (taken.has(name.toLowerCase())) {
      attempt++;
      name = output.kind === "folder" ? `${output.name} (${attempt})` : `${stem(output.name)} (${attempt})${output.name.slice(stem(output.name).length)}`;
    }
    taken.add(name.toLowerCase());
    outputs.push({ ...output, name, fresh: true });
  }

  const failed = outcomes.reduce((n, o) => n + (o.failed ?? (o.failure ? 1 : 0)), 0);
  const failure = outcomes.find((o) => o.failure)?.failure;
  const note = outcomes.find((o) => o.note)?.note;
  const saved = outputs.length;
  const total = entry.combined ? 1 : files.length;
  if (failed === 0)
    return saved === 0 ? { headline: "Nothing to save", detail: note, tone: "nothing", outputs } : { headline: `Saved ${countFiles(saved)}`, detail: note, tone: "success", outputs };
  if (saved > 0) return { headline: `Saved ${countFiles(saved)}, ${failed} failed`, detail: failure, tone: "partial", outputs };
  return total === 1
    ? { headline: failure ?? "The file couldn't be processed.", tone: "failure", outputs }
    : { headline: `${countFiles(failed)} couldn't be processed`, detail: failure, tone: "failure", outputs };
}

/** PageRange.TryParse. */
export function parsePages(text: string, pageCount: number): { pages: number[] } | { error: string } {
  if (!text.trim()) return { error: "Enter the pages to extract, for example 1-3, 5." };
  const selected: number[] = [];
  for (const raw of text.split(/[,;]/).map((s) => s.trim()).filter(Boolean)) {
    const dash = raw.indexOf("-");
    let first: number;
    let last: number;
    if (dash < 0) {
      first = toPage(raw);
      if (!first) return { error: `"${raw}" is not a page number.` };
      last = first;
    } else {
      const start = raw.slice(0, dash).trim();
      const end = raw.slice(dash + 1).trim();
      first = toPage(start || "1");
      if (!first) return { error: `"${raw}" is not a valid range.` };
      last = end ? toPage(end) : pageCount;
      if (!last) return { error: `"${raw}" is not a valid range.` };
    }
    if (first > last) return { error: `"${raw}" runs backwards.` };
    if (last > pageCount) return { error: pageCount === 1 ? "The document has only 1 page." : `The document has only ${pageCount} pages.` };
    for (let p = first; p <= last; p++) if (!selected.includes(p)) selected.push(p);
  }
  return selected.length ? { pages: selected } : { error: "No pages were selected." };
}

/** PageRange.IsWellFormed: the dialog checks the syntax before page counts are known. */
export function checkPages(text: string): string | null {
  const result = parsePages(text, Number.MAX_SAFE_INTEGER);
  return "error" in result ? result.error : null;
}

const toPage = (text: string) => (/^\d+$/.test(text) && Number(text) >= 1 ? Number(text) : 0);

export const MINIMUM_PASSWORD_LENGTH = 4;

export const initialDesktop = (): DemoFile[] => [
  { id: "strawberry", name: "strawberry.jpg", kind: "jpeg", image: { thumb: "/demo/strawberry-thumb.webp", full: "/demo/strawberry.webp", width: 752, height: 407 } },
  { id: "invoice", name: "invoice.pdf", kind: "pdf", pages: 1 },
  { id: "clouds", name: "clouds.webp", kind: "webp", image: { thumb: "/demo/clouds-thumb.webp", full: "/demo/clouds.webp", width: 744, height: 153 } },
  { id: "logo", name: "logo.png", kind: "png", image: { thumb: "/demo/logo.webp", full: "/demo/logo.webp", width: 256, height: 256 } },
  { id: "notes", name: "notes.pdf", kind: "pdf", pages: 3 },
  {
    id: "readme",
    name: "readme.txt",
    kind: "txt",
    text: "Welcome to the Kumo demo desktop!\n\nDouble-click files to open them, drag them around, right-click for new files.\nTo use Kumo, start dragging files and tap Shift.\n",
  },
];
