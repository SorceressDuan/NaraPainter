// One-shot product rename: Compositor -> NaraDreamPainter.
//
// Two things must survive untouched: the upstream reference in legacy/, and every mention of the
// original project by name in prose (the provenance notice, issue links, the migration docs). So
// this is not a blind replace - it renames the identifiers the code and the build use, and leaves
// sentences about the upstream project alone.
//
// Run from the repository root:  node tools/rename/rename.js --dry
const fs = require("fs");
const path = require("path");

const OLD = "Compositor";
const NEW = "NaraDreamPainter";
const dryRun = process.argv.includes("--dry");

const root = path.resolve(__dirname, "..", "..");

// Directories whose contents are never rewritten.
const SKIP_DIRS = new Set([".git", ".tools", "bin", "obj", "dist", "legacy", "node_modules"]);

// Extensions whose text is rewritten.
const TEXT_EXT = new Set([
    ".cs", ".xaml", ".csproj", ".sln", ".props", ".targets", ".md", ".ps1", ".txt",
    ".json", ".manifest", ".appxmanifest", ".resx", ".config", ".xml"
]);

// Identifier-level rewrites. Order matters: the longest, most specific patterns run first so that
// "Compositor.App" is not partially rewritten by a broader rule.
const RULES = [
    [`${OLD}.App`, `${NEW}.App`],
    [`${OLD}.Models`, `${NEW}.Models`],
    [`${OLD}.Imaging`, `${NEW}.Imaging`],
    [`${OLD}.Compositing`, `${NEW}.Compositing`],
    [`${OLD}.Tests`, `${NEW}.Tests`],
    [`${OLD}.TestRunner`, `${NEW}.TestRunner`],
    [`${OLD}.Assets`, `${NEW}.Assets`],
    [`${OLD}.Check`, `${NEW}.Check`],
    // Bare namespace prefix: "namespace Compositor;" and "using Compositor;"
    [`namespace ${OLD};`, `namespace ${NEW};`],
    [`using ${OLD};`, `using ${NEW};`],
    // Assembly and executable name.
    ["<AssemblyName>Compositor</AssemblyName>", `<AssemblyName>${NEW}</AssemblyName>`],
    // Window title and other display strings, but only where the old name stands as a word on its
    // own. Prose such as "the original Compositor project" is left for the docs pass.
    [`${OLD} — `, `${NEW} — `],
    [`Compositor.exe`, `${NEW}.exe`],
    [`"Compositor"`, `"${NEW}"`],
    [`'Compositor'`, `'${NEW}'`],
    [`compositor-selftest`, "naradreampainter-selftest"],
    [`compositor-startup`, "naradreampainter-startup"],
    [`compositor-notary`, "naradreampainter-notary"]
];

function isTextFile(name) {
    return TEXT_EXT.has(path.extname(name).toLowerCase());
}

function walk(dir, out = []) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        if (entry.isDirectory()) {
            if (SKIP_DIRS.has(entry.name)) continue;
            walk(path.join(dir, entry.name), out);
        } else {
            out.push(path.join(dir, entry.name));
        }
    }
    return out;
}

function applyRules(text) {
    let result = text;
    let hits = 0;
    for (const [from, to] of RULES) {
        const parts = result.split(from);
        if (parts.length > 1) {
            hits += parts.length - 1;
            result = parts.join(to);
        }
    }
    return { result, hits };
}

// Renames a path segment: Compositor.App -> NaraDreamPainter.App, and a bare Compositor -> NaraDreamPainter.
function renameSegment(segment) {
    if (segment === OLD) return NEW;
    if (segment.startsWith(OLD + ".")) return NEW + segment.slice(OLD.length);
    return segment;
}

function renamePath(relPath) {
    const parts = relPath.split(path.sep);
    let changed = false;
    const mapped = parts.map((part) => {
        const next = renameSegment(part);
        if (next !== part) changed = true;
        return next;
    });
    return { path: mapped.join(path.sep), changed };
}

function movePath(from, to) {
    if (dryRun) return;
    fs.mkdirSync(path.dirname(to), { recursive: true });
    fs.renameSync(from, to);
}

function main() {
    const files = walk(root);
    let rewrittenFiles = 0;
    let totalHits = 0;
    const movedPaths = [];
    const pending = [];

    for (const file of files) {
        const rel = path.relative(root, file);

        // Rewrite content first, while the file is still at its old path.
        if (isTextFile(file)) {
            const original = fs.readFileSync(file, "utf8");
            const { result, hits } = applyRules(original);
            if (hits > 0) {
                rewrittenFiles++;
                totalHits += hits;
                if (!dryRun) fs.writeFileSync(file, result, "utf8");
            }
        }

        const { path: newRel, changed } = renamePath(rel);
        if (changed) {
            movedPaths.push([rel, newRel]);
        }
    }

    // Deepest paths first, so renaming a parent directory never invalidates a child path.
    movedPaths.sort((a, b) => b[0].length - a[0].length);
    for (const [from, to] of movedPaths) {
        const fromAbs = path.join(root, from);
        const toAbs = path.join(root, to);
        if (!fs.existsSync(fromAbs)) continue;
        if (dryRun) {
            console.log(`  move  ${from}  ->  ${to}`);
        } else {
            movePath(fromAbs, toAbs);
        }
    }

    console.log("");
    console.log(`${dryRun ? "[dry run] " : ""}rewrote ${totalHits} occurrences across ${rewrittenFiles} files`);
    console.log(`${dryRun ? "[dry run] " : ""}renamed ${movedPaths.length} files or directories`);
}

main();
