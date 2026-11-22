import { copyFile, mkdir, stat } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const desktopRoot = fileURLToPath(new URL("..", import.meta.url));
const source = join(
    desktopRoot,
    "src-tauri",
    "target",
    "release",
    "juju-desktop.exe",
);
const destination = join(desktopRoot, "artifacts", "Juju.exe");

await mkdir(dirname(destination), { recursive: true });
await copyFile(source, destination);

const { size } = await stat(destination);
console.log(`Standalone executable: ${destination} (${size} bytes)`);
