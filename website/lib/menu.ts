// Mirrors Kumo's radial menu (src/Kumo.Core/Radial/RadialMenuModel.cs and src/Kumo.Operations) for the demo files,
// including the labels, descriptions and output names the app uses.

export type Icon = "convert" | "crop" | "compress" | "rotate" | "split" | "extract" | "protect";
export type Mascot = "converting" | "compressing" | "processing";

export type Action = {
  kind: "action";
  label: string;
  description: string;
  /** What the demo shows: new desktop files, or a note for actions that open a dialog in Kumo. */
  result: { outputs: string[] } | { dialog: string };
  mascot: Mascot;
  icon?: Icon;
};

export type Category = { kind: "category"; label: string; description: string; icon: Icon; items: Action[] };
export type Item = Action | Category;

export type DemoFile = { id: string; name: string; type: "jpg" | "png" | "pdf"; menu: Item[] };

const convert = (stem: string, to: string, ext: string): Action => ({
  kind: "action",
  label: to,
  description: `Convert to ${to}`,
  result: { outputs: [`${stem}.${ext}`] },
  mascot: "converting",
});

const rotate = (stem: string, ext: string): Category => ({
  kind: "category",
  label: "Rotate",
  description: "Rotate images",
  icon: "rotate",
  items: [
    { kind: "action", label: "90° right", description: "Rotate 90° clockwise", result: { outputs: [`${stem} (rotated).${ext}`] }, mascot: "processing" },
    { kind: "action", label: "90° left", description: "Rotate 90° counterclockwise", result: { outputs: [`${stem} (rotated).${ext}`] }, mascot: "processing" },
    { kind: "action", label: "180°", description: "Rotate 180°", result: { outputs: [`${stem} (rotated).${ext}`] }, mascot: "processing" },
  ],
});

const crop: Action = {
  kind: "action",
  label: "Crop",
  description: "Crop…",
  icon: "crop",
  result: { dialog: "Kumo opens the crop editor, then saves the area you keep as a new image." },
  mascot: "processing",
};

export const demoFiles: DemoFile[] = [
  {
    id: "strawberry",
    name: "strawberry.jpg",
    type: "jpg",
    menu: [
      {
        kind: "category",
        label: "Convert",
        description: "Convert to another format",
        icon: "convert",
        items: [convert("strawberry", "PNG", "png"), convert("strawberry", "WEBP", "webp"), convert("strawberry", "BMP", "bmp"), convert("strawberry", "PDF", "pdf")],
      },
      crop,
      {
        kind: "category",
        label: "Compress",
        description: "Make files smaller",
        icon: "compress",
        items: [
          { kind: "action", label: "High quality", description: "Compress (high quality)", result: { outputs: ["strawberry (compressed).jpg"] }, mascot: "compressing" },
          { kind: "action", label: "Balanced", description: "Compress (balanced)", result: { outputs: ["strawberry (compressed).jpg"] }, mascot: "compressing" },
          { kind: "action", label: "Smallest", description: "Compress (smallest)", result: { outputs: ["strawberry (compressed).jpg"] }, mascot: "compressing" },
        ],
      },
      rotate("strawberry", "jpg"),
    ],
  },
  {
    id: "logo",
    name: "logo.png",
    type: "png",
    menu: [
      {
        kind: "category",
        label: "Convert",
        description: "Convert to another format",
        icon: "convert",
        items: [convert("logo", "JPG", "jpg"), convert("logo", "WEBP", "webp"), convert("logo", "BMP", "bmp"), convert("logo", "PDF", "pdf")],
      },
      crop,
      {
        kind: "action",
        label: "Compress",
        description: "Optimize PNG without quality loss",
        icon: "compress",
        result: { outputs: ["logo (optimized).png"] },
        mascot: "compressing",
      },
      rotate("logo", "png"),
    ],
  },
  {
    id: "notes",
    name: "notes.pdf",
    type: "pdf",
    menu: [
      {
        kind: "category",
        label: "Convert",
        description: "Convert to another format",
        icon: "convert",
        items: [
          { kind: "action", label: "PNG", description: "Save pages as PNG images", result: { outputs: ["notes (images)"] }, mascot: "converting" },
          { kind: "action", label: "JPG", description: "Save pages as JPG images", result: { outputs: ["notes (images)"] }, mascot: "converting" },
        ],
      },
      { kind: "action", label: "Split", description: "Split into one PDF per page", icon: "split", result: { outputs: ["notes (pages)"] }, mascot: "processing" },
      {
        kind: "action",
        label: "Extract",
        description: "Extract selected pages",
        icon: "extract",
        result: { dialog: "Kumo asks which pages to keep, then saves them as a new PDF." },
        mascot: "processing",
      },
      {
        kind: "action",
        label: "Protect",
        description: "Protect with a password",
        icon: "protect",
        result: { dialog: "Kumo asks for a password, then saves a protected copy." },
        mascot: "processing",
      },
    ],
  },
];
